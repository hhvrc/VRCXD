import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { NotificationReceivedSchema, PresenceChangedSchema, ServerEventSchema, type ServerEvent } from '../gen/vrcx_live_pb.js';

const get = vi.hoisted(() => vi.fn());
vi.mock('#lib/api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn(async () => ({})) } }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});
// The socket itself is live-socket.test.ts's; here the events and reconnects are handed in.
const hooks = vi.hoisted(() => ({ events: [] as ((event: ServerEvent) => void)[], reconnects: [] as (() => void)[] }));
vi.mock('#lib/api/live.svelte.js', () => ({
	onLiveEvent: (listener: (event: ServerEvent) => void) => (hooks.events.push(listener), () => {}),
	onReconnect: (listener: () => void) => (hooks.reconnects.push(listener), () => {})
}));

// The topics deliver nothing in these tests: what is counted is what the wiring reads over REST.
vi.mock('#lib/live/topics/friends.svelte.js', () => ({
	friendsTopic: () => ({ value: undefined, status: 'connecting', listen: (listener: (value: unknown, status: string) => void) => (listener(undefined, 'connecting'), () => {}), release: () => {} })
}));
vi.mock('#lib/live/topics/notifications.svelte.js', () => ({
	notificationsTopic: () => ({ value: undefined, status: 'connecting', listen: (listener: (value: unknown, status: string) => void) => (listener(undefined, 'connecting'), () => {}), release: () => {} })
}));

const { startSidebarLive } = await import('../people/sidebar-live.js');

const asked = (suffix: string) => get.mock.calls.filter(([path]) => (path as string).endsWith(suffix)).length;
const send = (event: ServerEvent['event']) => hooks.events.forEach((listener) => listener(create(ServerEventSchema, { event })));

beforeEach(async () => {
	vi.useFakeTimers();
	get.mockReset();
	get.mockImplementation(async (path: string) => {
		if (path.endsWith('/notifications')) return { items: [] };
		if (path.endsWith('/favorites')) return { groups: [], favorites: [] };
		if (path.endsWith('/game/instance')) return { location: null, playerIds: [] };
		if (path.endsWith('/me')) return { id: 'usr_me' };
		return [];
	});
	startSidebarLive();
	await vi.advanceTimersByTimeAsync(20_000);
	get.mockClear();
});
afterEach(() => vi.useRealTimers());

// The sidebar's state and the notification center, kept current for the session by one wiring.
describe('sidebar live wiring', () => {
	test('starting again (every layout run) subscribes and reads nothing more', async () => {
		startSidebarLive();
		startSidebarLive();
		await vi.advanceTimersByTimeAsync(10_000);
		expect(get).not.toHaveBeenCalled();
		expect(hooks.events).toHaveLength(1);
		expect(hooks.reconnects).toHaveLength(1);
	});

	test('a reconnect reads the favorites, profile, game and groups once, and leaves friends and notifications to their topics', async () => {
		hooks.reconnects.forEach((listener) => listener());
		await vi.advanceTimersByTimeAsync(20_000);
		for (const path of ['/favorites', '/me', '/game/instance', '/group-instances']) expect(asked(path)).toBe(1);
		for (const path of ['/friends', '/notifications']) expect(asked(path)).toBe(0);
	});

	test('a notification reads nothing: the notification center follows its topic', async () => {
		send({ case: 'notification', value: create(NotificationReceivedSchema, { ownerUserId: 'usr_me', type: 'invite' }) });
		await vi.advanceTimersByTimeAsync(20_000);
		expect(get).not.toHaveBeenCalled();
	});

	test('presence reads nothing', async () => {
		send({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online' }) });
		await vi.advanceTimersByTimeAsync(20_000);
		expect(get).not.toHaveBeenCalled();
	});
});
