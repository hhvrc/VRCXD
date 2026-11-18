import { beforeEach, describe, expect, test } from 'vitest';
import { navNotify, notificationLightsNav } from '../nav/nav-notify.svelte';

// The original's notifyMenu, removeNotify and clearAllNotifications.
describe('nav notification dots', () => {
	beforeEach(() => navNotify.clear());

	test('a page gets its dot once, and several pages each get one', () => {
		navNotify.notify('notification', '/feed');
		navNotify.notify('notification', '/feed');
		navNotify.notify('friend-log', '/feed');
		expect(navNotify.menus).toEqual(['notification', 'friend-log']);
	});

	test('the page that is open gets no dot', () => {
		navNotify.notify('notification', '/notification');
		expect(navNotify.has('notification')).toBe(false);
	});

	test('removing takes one dot away and ignores pages without one', () => {
		navNotify.notify('notification', '/feed');
		navNotify.notify('friend-log', '/feed');
		navNotify.remove('notification');
		navNotify.remove('tools');
		expect(navNotify.menus).toEqual(['friend-log']);
	});

	test('mark all read clears every dot, and dots come back afterwards', () => {
		navNotify.clear();
		expect(navNotify.menus).toEqual([]);
		navNotify.notify('notification', '/feed');
		navNotify.clear();
		expect(navNotify.menus).toEqual([]);
		navNotify.notify('friend-log', '/feed');
		expect(navNotify.has('friend-log')).toBe(true);
	});
});

// Upstream e55577c1 and 588042ea: which new notifications light the Notifications dot.
describe('what lights the Notifications dot', () => {
	const v1 = (type: string, senderUserId = 'usr_other') => ({ version: 1, type, senderUserId });

	test('an invite does, with no type filter', () => {
		expect(notificationLightsNav(v1('invite'), [], 'usr_me')).toBe(true);
	});

	test('a friend request, an ignored one or a dotted v1 type does not', () => {
		expect(notificationLightsNav(v1('friendRequest'), [], 'usr_me')).toBe(false);
		expect(notificationLightsNav(v1('ignoredFriendRequest'), [], 'usr_me')).toBe(false);
		expect(notificationLightsNav(v1('group.announcement'), [], 'usr_me')).toBe(false);
	});

	test("the account's own v1 notification does not", () => {
		expect(notificationLightsNav(v1('invite', 'usr_me'), [], 'usr_me')).toBe(false);
	});

	test("only types the page's filter shows do", () => {
		expect(notificationLightsNav(v1('invite'), ['boop'], 'usr_me')).toBe(false);
		expect(notificationLightsNav(v1('boop'), ['boop'], 'usr_me')).toBe(true);
		expect(notificationLightsNav({ version: 2, type: 'group.announcement', senderUserId: '' }, ['boop'], 'usr_me')).toBe(false);
	});

	test('a v2 notification does whatever its type', () => {
		expect(notificationLightsNav({ version: 2, type: 'group.announcement', senderUserId: '' }, [], 'usr_me')).toBe(true);
	});
});
