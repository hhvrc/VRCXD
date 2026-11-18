import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { timestampFromDate } from '@bufbuild/protobuf/wkt';
import { FriendshipChangedSchema, InstanceVisitChangedSchema, PresenceChangedSchema, ServerEventSchema, UserChangedSchema, type ServerEvent } from '../gen/vrcx_live_pb.js';

const get = vi.hoisted(() => vi.fn());
vi.mock('#lib/api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn(async () => ({})) } }));
const auth = vi.hoisted(() => ({ ownerUserId: 'usr_me' }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => ({ vrchatAuth: auth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(auth.ownerUserId)}${rest}` }));

// The friends topic, handed in: it waits ('connecting') until the test sends a snapshot.
type Listener = (value: unknown, status: string) => void;
const topic = vi.hoisted(() => ({
	listeners: [] as Listener[],
	accounts: [] as string[],
	released: [] as string[],
	send(value: unknown, status = 'live') {
		for (const listener of this.listeners) listener(value, status);
	}
}));
vi.mock('#lib/live/topics/friends.svelte.js', () => ({
	friendsTopic: (account: string) => {
		topic.accounts.push(account);
		const mine: Listener[] = [];
		return {
			value: undefined,
			status: 'connecting',
			listen(listener: Listener) {
				mine.push(listener);
				topic.listeners.push(listener);
				listener(undefined, 'connecting');
				return () => {};
			},
			release() {
				topic.released.push(account);
				topic.listeners = topic.listeners.filter((l) => !mine.includes(l));
			}
		};
	}
}));

const { sidebarData, FRIENDS_TOPIC_WAIT_MS } = await import('../people/sidebar-data.svelte.js');

const bob = { userId: 'usr_bob', displayName: 'Bob', state: 'offline', location: 'offline', platform: 'standalonewindows', friendNumber: 2 };
const alice = { userId: 'usr_alice', displayName: 'Alice', state: 'online', location: 'wrld_a:1', friendNumber: 1 };
const ev = (event: ServerEvent['event']) => create(ServerEventSchema, { at: timestampFromDate(new Date('2026-11-18T12:00:00Z')), event });

/** What the fake server answers, and how often each path was asked. The friend list is not among them: it has no REST read. */
function serve() {
	get.mockImplementation(async (path: string) => {
		if (path.endsWith('/favorites')) return { groups: [{ name: 'group_0', displayName: 'Best' }], favorites: [{ groupName: 'group_0', targetId: 'usr_bob' }] };
		if (path.endsWith('/favorites/local') || path.endsWith('/favorites/local/groups')) return [];
		if (path.endsWith('/me')) return { id: 'usr_me', displayName: 'Me', currentLocation: 'wrld_a:1' };
		if (path.endsWith('/game/instance')) return { location: 'wrld_a:1', playerIds: ['usr_alice'], joinedAt: null };
		if (path.endsWith('/group-instances')) return [];
		throw new Error(`unexpected ${path}`);
	});
}
const asked = (suffix: string) => get.mock.calls.filter(([path]) => (path as string).endsWith(suffix)).length;
const settle = async () => {
	for (let i = 0; i < 10; i++) await Promise.resolve();
};

let account = 0;
/** A fresh account (start() runs once per account), its first reads done, the friend list not yet delivered. */
async function startAccount() {
	auth.ownerUserId = `usr_me${++account}`;
	sidebarData.start();
	await settle();
	get.mockClear();
}

beforeEach(async () => {
	vi.useFakeTimers();
	// Each test an hour after the last: the store's debounces outlive a test and remember when they
	// last ran, which must not lie in this test's future.
	vi.setSystemTime(Date.now() + (account + 1) * 3_600_000);
	get.mockReset();
	serve();
	await startAccount();
});
afterEach(() => vi.useRealTimers());

const me = () => auth.ownerUserId;

describe('the friend list from the friends topic', () => {
	test('the snapshot is the list, and nothing reads friends over REST', async () => {
		const waiting = sidebarData.ensureFriends();
		expect(sidebarData.friendsLoaded).toBe(false);
		topic.send([alice, bob]);
		expect(await waiting).toHaveLength(2);
		expect(sidebarData.friendsLoaded).toBe(true);
		expect(sidebarData.onlineCount).toBe(1);
		expect(sidebarData.isFriend('usr_bob')).toBe(true);
		await vi.advanceTimersByTimeAsync(FRIENDS_TOPIC_WAIT_MS * 2);
		expect(asked('/friends')).toBe(0);
		expect(sidebarData.error).toBe('');
	});

	test('deltas replace the list; presence, status and friendship events read nothing and change nothing', async () => {
		topic.send([alice, bob]);
		topic.send([alice, { ...bob, state: 'online', location: 'wrld_b:2' }]);
		expect(sidebarData.friends[1]).toMatchObject({ state: 'online', location: 'wrld_b:2' });
		sidebarData.apply(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'offline', location: 'offline' }) }));
		sidebarData.apply(ev({ case: 'user', value: create(UserChangedSchema, { userId: 'usr_bob', changed: ['status'] }) }));
		sidebarData.apply(ev({ case: 'friendship', value: create(FriendshipChangedSchema, { ownerUserId: me(), friendUserId: 'usr_new', isFriend: true }) }));
		// The topic's row stands until the topic says otherwise.
		expect(sidebarData.friends[1]).toMatchObject({ state: 'online', location: 'wrld_b:2' });
		await vi.advanceTimersByTimeAsync(20_000);
		expect(get).not.toHaveBeenCalled();
	});

	test('no snapshot in time is said, with nothing read in its place; the snapshot coming clears it', async () => {
		await vi.advanceTimersByTimeAsync(FRIENDS_TOPIC_WAIT_MS - 1);
		expect(sidebarData.error).toBe('');
		await vi.advanceTimersByTimeAsync(1);
		expect(sidebarData.error).not.toBe('');
		expect(sidebarData.friendsLoaded).toBe(false);
		expect(get).not.toHaveBeenCalled();

		topic.send([alice]);
		expect(sidebarData.error).toBe('');
		expect(sidebarData.friends.map((f) => f.userId)).toEqual(['usr_alice']);
	});

	test('a subscription the server refuses stops the wait: whoever waits for the list gets an empty one', async () => {
		const waiting = sidebarData.ensureFriends();
		topic.send(undefined, 'closed');
		expect(await waiting).toEqual([]);
		await vi.advanceTimersByTimeAsync(FRIENDS_TOPIC_WAIT_MS * 2);
		expect(sidebarData.error).toBe('');
	});

	test('the refresh button asks VRChat and leaves the list to the deltas', async () => {
		topic.send([alice]);
		await sidebarData.refresh();
		expect(asked('/friends')).toBe(0);
	});

	test('another account releases the old topic, starts empty and follows its own', async () => {
		topic.send([alice]);
		const before = me();
		await startAccount();
		expect(topic.released).toContain(before);
		expect(topic.accounts[topic.accounts.length - 1]).toBe(me());
		expect(sidebarData.friends).toEqual([]);
		expect(sidebarData.friendsLoaded).toBe(false);
	});
});

describe('shared sidebar state without a topic yet', () => {
	test('loads once per account: starting again (the sidebar mounting again) reads nothing', async () => {
		sidebarData.start();
		sidebarData.start();
		await vi.advanceTimersByTimeAsync(1000);
		expect(get).not.toHaveBeenCalled();
	});

	test('a second ask while a read runs shares it', async () => {
		await Promise.all([sidebarData.loadMe(), sidebarData.loadMe(), sidebarData.loadMe()]);
		expect(asked('/me')).toBe(1);
	});

	test("your own visit reads you and the game again; a friend's visit elsewhere does not", async () => {
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_bob', instanceLocation: 'wrld_z:9', joined: true, source: 'pipeline' }) }));
		await vi.advanceTimersByTimeAsync(20_000);
		expect(get).not.toHaveBeenCalled();
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: me(), instanceLocation: 'wrld_a:1', joined: true, source: 'game_log' }) }));
		await vi.advanceTimersByTimeAsync(20_000);
		expect(asked('/me')).toBe(1);
		expect(asked('/game/instance')).toBe(1);
	});

	test("someone joining the game's instance by its log is added to its players as it is", async () => {
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_bob', instanceLocation: 'wrld_a:1', joined: true, source: 'game_log' }) }));
		expect([...sidebarData.gameInstance.playerIds]).toEqual(['usr_alice', 'usr_bob']);
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_alice', instanceLocation: 'wrld_a:1', joined: false, source: 'game_log' }) }));
		expect([...sidebarData.gameInstance.playerIds]).toEqual(['usr_bob']);
		await vi.advanceTimersByTimeAsync(20_000);
		expect(get).not.toHaveBeenCalled();
	});

	test('your own profile keeps being read under a steady stream of changes: at least every 10 seconds', async () => {
		for (let i = 0; i < 25; i++) {
			sidebarData.apply(ev({ case: 'user', value: create(UserChangedSchema, { userId: me(), changed: ['status'] }) }));
			await vi.advanceTimersByTimeAsync(1000);
		}
		expect(asked('/me')).toBe(2);
	});

	test('a reconnect leaves the friends to the topic and reads the rest once', async () => {
		topic.send([alice]);
		sidebarData.resync();
		await vi.advanceTimersByTimeAsync(5000);
		expect(asked('/friends')).toBe(0);
		for (const path of ['/favorites', '/me', '/game/instance', '/group-instances']) expect(asked(path)).toBe(1);
	});

	test('an event read again while an older read runs gets a read of its own after it', async () => {
		let release: (value: unknown) => void = () => {};
		get.mockImplementationOnce(() => new Promise((resolve) => (release = resolve)));
		void sidebarData.loadMe();
		await settle();
		const fresh = sidebarData.loadMe(true);
		release({ id: 'usr_me', displayName: 'Me' });
		await fresh;
		expect(asked('/me')).toBe(2);
	});
});
