import { describe, expect, test } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { PresenceChangedSchema, ServerEventSchema, type ServerEvent } from '../gen/vrcx_live_pb.js';
import type { Friend } from '../api/types.js';
import { feedEventMatters, gameLogEventMatters, patchFriendPresence, planSidebarEvent, type SidebarContext } from '../people/sidebar-events.js';

const ev = (event: ServerEvent['event']): ServerEvent => create(ServerEventSchema, { event });
const ctx: SidebarContext = { owner: 'usr_me', isFriend: (id) => id === 'usr_bob', gameLocation: 'wrld_a:1' };

const bob: Friend = {
	friendNumber: 2,
	friendsSince: '2026-01-01T00:00:00Z',
	userId: 'usr_bob',
	displayName: 'Bob',
	trustLevel: 'known',
	state: 'offline',
	status: 'active',
	location: 'offline',
	travelingToLocation: null,
	locationSince: '2026-11-01T00:00:00Z',
	onlineFor: '2026-11-01T00:00:00Z',
	platform: 'standalonewindows',
	pendingOffline: true
};
const alice: Friend = { ...bob, userId: 'usr_alice', displayName: 'Alice', state: 'online', location: 'wrld_a:1' };

// What each live event means for the shared friend state, with the stream carrying every account's events.
describe('sidebar event plan', () => {
	test("a friend's presence is applied as it is, with nothing read again", () => {
		const plan = planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online', location: 'wrld_a:2' }) }), ctx);
		expect(plan).toEqual({ presence: expect.objectContaining({ userId: 'usr_bob' }) });
	});

	test("a stranger's presence is nothing, and your own reads your profile again", () => {
		expect(planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_stranger' }) }), ctx)).toEqual({});
		expect(planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_me' }) }), ctx)).toEqual({ me: true });
	});

	test('a profile change only carries field names: the list is read again for the fields it shows', () => {
		const user = (userId: string, changed: string[]) => ev({ case: 'user', value: { $typeName: 'vrcx.live.UserChanged', userId, displayName: '', changed } });
		expect(planSidebarEvent(user('usr_bob', ['status']), ctx)).toEqual({ friends: true });
		expect(planSidebarEvent(user('usr_bob', ['bio']), ctx)).toEqual({});
		expect(planSidebarEvent(user('usr_stranger', ['status']), ctx)).toEqual({});
		expect(planSidebarEvent(user('usr_me', ['avatar']), ctx)).toEqual({ me: true });
	});

	test('friendships, notifications and sessions count only for the signed-in account', () => {
		const friendship = (owner: string) => ev({ case: 'friendship', value: { $typeName: 'vrcx.live.FriendshipChanged', ownerUserId: owner, friendUserId: 'usr_new', displayName: 'New', isFriend: true, friendNumber: 9 } });
		expect(planSidebarEvent(friendship('usr_me'), ctx)).toEqual({ friends: true });
		expect(planSidebarEvent(friendship('usr_other'), ctx)).toEqual({});
		const note = (owner: string) => ev({ case: 'notification', value: { $typeName: 'vrcx.live.NotificationReceived', ownerUserId: owner, notificationId: 'n', version: 1, type: 'invite', senderUserId: '', senderDisplayName: '', title: '', message: '' } });
		expect(planSidebarEvent(note('usr_me'), ctx)).toEqual({ notifications: true });
		expect(planSidebarEvent(note('usr_other'), ctx)).toEqual({});
		const account = (id: string) => ev({ case: 'account', value: { $typeName: 'vrcx.live.AccountSessionChanged', accountId: id, displayName: '', sessionValid: true, autoLogin: '', autoLoginDelay: 0 } });
		expect(planSidebarEvent(account('usr_me'), ctx)).toEqual({ me: true });
		expect(planSidebarEvent(account('usr_other'), ctx)).toEqual({});
	});

	test("visits: your own move reads you and the game again, others only count in the game's instance by its log", () => {
		const visit = (userId: string, instanceLocation: string, source = 'game_log', joined = true) =>
			ev({ case: 'visit', value: { $typeName: 'vrcx.live.InstanceVisitChanged', userId, displayName: '', instanceLocation, worldId: '', joined, source } });
		expect(planSidebarEvent(visit('usr_me', 'wrld_b:1'), ctx)).toEqual({ me: true, game: true });
		expect(planSidebarEvent(visit('usr_bob', 'wrld_a:1'), ctx)).toEqual({ player: { userId: 'usr_bob', joined: true } });
		expect(planSidebarEvent(visit('usr_bob', 'wrld_a:1', 'game_log', false), ctx)).toEqual({ player: { userId: 'usr_bob', joined: false } });
		// A friend's visit somewhere else, or the pipeline's view of one here, is not the game's.
		expect(planSidebarEvent(visit('usr_bob', 'wrld_c:1'), ctx)).toEqual({});
		expect(planSidebarEvent(visit('usr_bob', 'wrld_a:1', 'pipeline'), ctx)).toEqual({});
		expect(planSidebarEvent(visit('usr_bob', 'wrld_a:1'), { ...ctx, gameLocation: null })).toEqual({});
	});

	test('favorites are never read again for presence; only a favorites refresh of this account', () => {
		const refresh = (accountId: string, contentType: string) => ev({ case: 'contentRefresh', value: { $typeName: 'vrcx.live.AccountContentRefreshed', accountId, contentType, actionType: '' } });
		expect(planSidebarEvent(refresh('usr_me', 'favorite'), ctx)).toEqual({ favorites: true });
		expect(planSidebarEvent(refresh('usr_me', 'gallery'), ctx)).toEqual({});
		expect(planSidebarEvent(refresh('usr_other', 'favorite'), ctx)).toEqual({});
		const presence = planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online' }) }), ctx);
		expect(presence.favorites).toBeUndefined();
		expect(presence.friends).toBeUndefined();
	});
});

describe('friend presence patch', () => {
	const at = '2026-11-18T12:00:00.000Z';

	test('online in an instance: state, location, platform and both timers as GET /friends would show them', () => {
		const next = patchFriendPresence([alice, bob], create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online', kind: 'online', location: 'wrld_a:2', platform: 'android' }), at);
		expect(next?.[0]).toBe(alice);
		expect(next?.[1]).toEqual({ ...bob, state: 'online', location: 'wrld_a:2', travelingToLocation: null, platform: 'android', locationSince: at, onlineFor: at, pendingOffline: false });
	});

	test('a move keeps the online timer, and travelling is carried', () => {
		const online = { ...bob, state: 'online' as const, location: 'wrld_a:2' };
		const next = patchFriendPresence([online], create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online', kind: 'location', location: 'wrld_b:3', travelingToLocation: 'wrld_c:4' }), at);
		expect(next?.[0]).toMatchObject({ location: 'wrld_b:3', travelingToLocation: 'wrld_c:4', locationSince: at, onlineFor: bob.onlineFor, platform: bob.platform });
	});

	test('offline or web as the platform leaves the last one, as computeUserPlatform', () => {
		const online = { ...bob, state: 'online' as const, location: 'wrld_a:2' };
		const next = patchFriendPresence([online], create(PresenceChangedSchema, { userId: 'usr_bob', state: 'offline', kind: 'offline', location: 'offline', platform: 'web' }), at);
		expect(next?.[0]).toMatchObject({ state: 'offline', location: 'offline', platform: 'standalonewindows', onlineFor: at });
	});

	test('the same place keeps its timer, and the name is left for the rename event', () => {
		const online = { ...bob, state: 'online' as const, location: 'wrld_a:2' };
		const next = patchFriendPresence([online], create(PresenceChangedSchema, { userId: 'usr_bob', displayName: 'Robert', state: 'online', kind: 'location', location: 'wrld_a:2' }), at);
		expect(next?.[0]).toMatchObject({ displayName: 'Bob', locationSince: bob.locationSince });
	});

	test('someone not in the list changes nothing', () => {
		expect(patchFriendPresence([alice], create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online' }), at)).toBeUndefined();
	});
});

describe('page refetch filters', () => {
	const isFriend = (id: string) => id === 'usr_bob';
	test("the feed grows from friends' presence and their status, avatar and bio", () => {
		expect(feedEventMatters(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob' }) }), isFriend)).toBe(true);
		expect(feedEventMatters(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_x' }) }), isFriend)).toBe(false);
		const user = (changed: string[]) => ev({ case: 'user', value: { $typeName: 'vrcx.live.UserChanged', userId: 'usr_bob', displayName: '', changed } });
		expect(feedEventMatters(user(['bio']), isFriend)).toBe(true);
		expect(feedEventMatters(user(['trust']), isFriend)).toBe(false);
	});

	test("the game log grows from game log visits and this account's batches", () => {
		const visit = (source: string) => ev({ case: 'visit', value: { $typeName: 'vrcx.live.InstanceVisitChanged', userId: 'usr_bob', displayName: '', instanceLocation: 'wrld_a:1', worldId: '', joined: true, source } });
		expect(gameLogEventMatters(visit('game_log'), 'usr_me')).toBe(true);
		expect(gameLogEventMatters(visit('pipeline'), 'usr_me')).toBe(false);
		const batch = (accountId: string) => ev({ case: 'gameLog', value: { $typeName: 'vrcx.live.GameLogStored', accountId, instanceLocation: '', kinds: [] } });
		expect(gameLogEventMatters(batch('usr_me'), 'usr_me')).toBe(true);
		expect(gameLogEventMatters(batch('usr_other'), 'usr_me')).toBe(false);
	});
});
