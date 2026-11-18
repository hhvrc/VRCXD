import { describe, expect, test } from 'vitest';
import { create } from '@bufbuild/protobuf';
import { PresenceChangedSchema, ServerEventSchema, type ServerEvent } from '../gen/vrcx_live_pb.js';
import { feedEventMatters, gameLogEventMatters, planSidebarEvent, type SidebarContext } from '../people/sidebar-events.js';

const ev = (event: ServerEvent['event']): ServerEvent => create(ServerEventSchema, { event });
const ctx: SidebarContext = { owner: 'usr_me', gameLocation: 'wrld_a:1' };

// What each live event means for the shared sidebar state that has no topic, with the stream
// carrying every account's events. Friends and notifications follow their topics, so their events
// plan nothing.
describe('sidebar event plan', () => {
	test("a friend's or a stranger's presence is nothing here, and your own reads your profile again", () => {
		expect(planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_bob', state: 'online', location: 'wrld_a:2' }) }), ctx)).toEqual({});
		expect(planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_stranger' }) }), ctx)).toEqual({});
		expect(planSidebarEvent(ev({ case: 'presence', value: create(PresenceChangedSchema, { userId: 'usr_me' }) }), ctx)).toEqual({ me: true });
	});

	test('a profile change reads your own profile again for the fields the sidebar shows, and nothing for anyone else', () => {
		const user = (userId: string, changed: string[]) => ev({ case: 'user', value: { $typeName: 'vrcx.live.UserChanged', userId, displayName: '', changed } });
		expect(planSidebarEvent(user('usr_bob', ['status']), ctx)).toEqual({});
		expect(planSidebarEvent(user('usr_stranger', ['status']), ctx)).toEqual({});
		expect(planSidebarEvent(user('usr_me', ['avatar']), ctx)).toEqual({ me: true });
		expect(planSidebarEvent(user('usr_me', ['bio']), ctx)).toEqual({});
	});

	test('friendships and notifications plan nothing; sessions count only for the signed-in account', () => {
		const friendship = (owner: string) => ev({ case: 'friendship', value: { $typeName: 'vrcx.live.FriendshipChanged', ownerUserId: owner, friendUserId: 'usr_new', displayName: 'New', isFriend: true, friendNumber: 9 } });
		expect(planSidebarEvent(friendship('usr_me'), ctx)).toEqual({});
		const note = (owner: string) => ev({ case: 'notification', value: { $typeName: 'vrcx.live.NotificationReceived', ownerUserId: owner, notificationId: 'n', version: 1, type: 'invite', senderUserId: '', senderDisplayName: '', title: '', message: '' } });
		expect(planSidebarEvent(note('usr_me'), ctx)).toEqual({});
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
