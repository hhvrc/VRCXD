import { describe, expect, test, vi } from 'vitest';
import type { Notification } from '../api/types.js';

const post = vi.hoisted(() => vi.fn(async () => ({})));
const get = vi.hoisted(() => vi.fn(async () => ({ items: [] })));
vi.mock('#lib/api/client.js', () => ({ api: { get, post }, accountPath: (account: string, rest = '') => `/1/accounts/${account}${rest}` }));
vi.mock('#lib/api/vrchat-auth.svelte.js', () => {
	const vrchatAuth = { ownerUserId: 'usr_me' };
	return { vrchatAuth, ownPath: (rest = '') => `/1/accounts/${encodeURIComponent(vrchatAuth.ownerUserId)}${rest}` };
});

const { notificationCenter } = await import('../notifications/notification-center.svelte.js');

const HOUR = 60 * 60 * 1000;
const now = Date.parse('2026-03-23T12:00:00Z');
const row = (id: string, type: string, seen: boolean, hoursAgo: number) =>
	({ id, type, seen, createdAt: new Date(now - hoursAgo * HOUR).toISOString() }) as Notification;

// The original's NotificationList and NotificationCenterSheet lists behind the sidebar bell.
describe('notification center lists', () => {
	test('unseen ones come newest first, then the seen ones of the last day', () => {
		notificationCenter.rows = [row('old', 'friendRequest', false, 5), row('new', 'invite', false, 1), row('recent1', 'boop', true, 2), row('stale', 'invite', true, 30)];
		notificationCenter.now = now;
		expect(notificationCenter.unseenIn('friend').map((n) => n.id)).toEqual(['new', 'old']);
		expect(notificationCenter.recentIn('friend').map((n) => n.id)).toEqual(['recent1']);
	});

	test('nothing new gives empty lists', () => {
		notificationCenter.rows = [];
		expect(notificationCenter.unseenIn('friend')).toEqual([]);
		expect(notificationCenter.recentIn('friend')).toEqual([]);
		expect(notificationCenter.hasUnseen).toBe(false);
	});

	test('group notifications sit under the group tab only', () => {
		notificationCenter.rows = [row('g', 'group.announcement', false, 1)];
		notificationCenter.now = now;
		expect(notificationCenter.unseenIn('group').map((n) => n.id)).toEqual(['g']);
		expect(notificationCenter.unseenIn('friend')).toEqual([]);
		expect(notificationCenter.unseenIn('other')).toEqual([]);
	});

	test('Mark all as read asks the server and reloads', async () => {
		await notificationCenter.markAllSeen();
		expect(post).toHaveBeenCalledWith('/1/accounts/usr_me/notifications/see-all', {});
		expect(get).toHaveBeenCalledWith('/1/accounts/usr_me/notifications', { page: 1, pageSize: 500 });
	});
});
