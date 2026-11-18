import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { timestampFromDate } from '@bufbuild/protobuf/wkt';
import { FriendshipChangedSchema, InstanceVisitChangedSchema, PresenceChangedSchema, ServerEventSchema, UserChangedSchema, type ServerEvent } from '../gen/vrcx_live_pb.js';

const get = vi.hoisted(() => vi.fn());
vi.mock('#lib/api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn(async () => ({})) } }));
const auth = vi.hoisted(() => ({ ownerUserId: 'usr_me' }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => ({ vrchatAuth: auth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(auth.ownerUserId)}${rest}` }));

const { sidebarData } = await import('../people/sidebar-data.svelte.js');

const bob = { userId: 'usr_bob', displayName: 'Bob', state: 'offline', location: 'offline', platform: 'standalonewindows' };
const ev = (event: ServerEvent['event']) => create(ServerEventSchema, { at: timestampFromDate(new Date('2026-11-18T12:00:00Z')), event });

/** What the fake server answers, and how often each path was asked. */
function serve(friends = [bob]) {
	get.mockImplementation(async (path: string) => {
		if (path.endsWith('/friends')) return friends;
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
beforeEach(async () => {
	vi.useFakeTimers();
	get.mockReset();
	serve();
	// A fresh account each test: start() loads once per account.
	auth.ownerUserId = `usr_me${++account}`;
	sidebarData.start();
	await vi.runAllTimersAsync();
	get.mockClear();
});
afterEach(() => vi.useRealTimers());

const me = () => auth.ownerUserId;

describe('shared sidebar state', () => {
	test('loads once per account: starting again (the sidebar mounting again) reads nothing', async () => {
		expect(sidebarData.friendsLoaded).toBe(true);
		sidebarData.start();
		sidebarData.start();
		expect(await sidebarData.ensureFriends()).toHaveLength(1);
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
	});

	test('a second ask while a read runs shares it', async () => {
		auth.ownerUserId = `usr_me${++account}`;
		sidebarData.start();
		await Promise.all([sidebarData.ensureFriends(), sidebarData.ensureFriends(), sidebarData.loadMe()]);
		expect(asked('/friends')).toBe(1);
		expect(asked('/me')).toBe(1);
	});

	test("a friend's presence is applied to the list without a single read", async () => {
		sidebarData.apply(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online', kind: 'online', location: 'wrld_b:2', platform: 'android' }) }));
		expect(sidebarData.friends[0]).toMatchObject({ state: 'online', location: 'wrld_b:2', platform: 'android', locationSince: '2026-11-18T12:00:00.000Z' });
		expect(sidebarData.onlineCount).toBe(1);
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
	});

	test('a burst of presence changes reads nothing, ever', async () => {
		for (let i = 0; i < 50; i++) {
			sidebarData.apply(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: i % 2 ? 'offline' : 'online', location: i % 2 ? 'offline' : `wrld_b:${i}` }) }));
			vi.advanceTimersByTime(200);
		}
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
	});

	test('a new friendship reads the friend list again, not the favorites', async () => {
		serve([bob, { ...bob, userId: 'usr_new', displayName: 'New' }]);
		sidebarData.apply(ev({ case: 'friendship', value: create(FriendshipChangedSchema, { ownerUserId: me(), friendUserId: 'usr_new', isFriend: true }) }));
		await vi.advanceTimersByTimeAsync(1500);
		await settle();
		expect(asked('/friends')).toBe(1);
		expect(asked('/favorites')).toBe(0);
		expect(sidebarData.friends.map((f) => f.userId)).toEqual(['usr_bob', 'usr_new']);
	});

	test("another account's friendship, and a stranger's status, read nothing", async () => {
		sidebarData.apply(ev({ case: 'friendship', value: create(FriendshipChangedSchema, { ownerUserId: 'usr_other', friendUserId: 'usr_new', isFriend: true }) }));
		sidebarData.apply(ev({ case: 'user', value: create(UserChangedSchema, { userId: 'usr_stranger', changed: ['status'] }) }));
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
	});

	test("your own visit reads you and the game again; a friend's visit elsewhere does not", async () => {
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_bob', instanceLocation: 'wrld_z:9', joined: true, source: 'pipeline' }) }));
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: me(), instanceLocation: 'wrld_a:1', joined: true, source: 'game_log' }) }));
		await vi.runAllTimersAsync();
		expect(asked('/me')).toBe(1);
		expect(asked('/game/instance')).toBe(1);
		expect(asked('/friends')).toBe(0);
	});

	test("someone joining the game's instance by its log is added to its players as it is", async () => {
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_bob', instanceLocation: 'wrld_a:1', joined: true, source: 'game_log' }) }));
		expect([...sidebarData.gameInstance.playerIds]).toEqual(['usr_alice', 'usr_bob']);
		sidebarData.apply(ev({ case: 'visit', value: create(InstanceVisitChangedSchema, { userId: 'usr_alice', instanceLocation: 'wrld_a:1', joined: false, source: 'game_log' }) }));
		expect([...sidebarData.gameInstance.playerIds]).toEqual(['usr_bob']);
		await vi.runAllTimersAsync();
		expect(get).not.toHaveBeenCalled();
	});

	test('status changes keep reading under a steady stream: at least every 10 seconds', async () => {
		for (let i = 0; i < 25; i++) {
			sidebarData.apply(ev({ case: 'user', value: create(UserChangedSchema, { userId: 'usr_bob', changed: ['status'] }) }));
			await vi.advanceTimersByTimeAsync(1000);
		}
		expect(asked('/friends')).toBe(2);
	});

	test('a reconnect reads everything once', async () => {
		sidebarData.resync();
		await vi.runAllTimersAsync();
		for (const path of ['/friends', '/favorites', '/me', '/game/instance', '/group-instances']) expect(asked(path)).toBe(1);
	});

	test('an event read again while an older read runs gets a read of its own after it', async () => {
		let release: (value: unknown) => void = () => {};
		get.mockImplementationOnce(() => new Promise((resolve) => (release = resolve)));
		void sidebarData.loadFriends();
		await settle();
		const fresh = sidebarData.loadFriends(true);
		release([bob]);
		await fresh;
		expect(asked('/friends')).toBe(2);
	});

	test('another account starts over', async () => {
		auth.ownerUserId = `usr_me${++account}`;
		sidebarData.start();
		expect(sidebarData.friends).toEqual([]);
		await vi.runAllTimersAsync();
		expect(asked('/friends')).toBe(1);
		expect(sidebarData.friendsLoaded).toBe(true);
	});
});
