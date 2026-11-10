import { beforeEach, describe, expect, test } from 'vitest';
import { navNotify } from '../nav/nav-notify.svelte';

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
