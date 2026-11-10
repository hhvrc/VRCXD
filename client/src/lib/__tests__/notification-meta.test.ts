import { describe, expect, test } from 'vitest';
import type { Notification } from '../api/types.js';
import { boopView, detailsOf, notificationCategory } from '../notifications/notification-meta.js';

const row = (fields: Partial<Notification>): Notification => ({ id: 'not_1', version: 1, type: 'message', message: '', seen: false, hidden: false, createdAt: '', ...fields });

// The original's getNotificationCategory.
describe('notification category', () => {
	test('no type is other', () => {
		expect(notificationCategory('')).toBe('other');
		expect(notificationCategory(undefined)).toBe('other');
	});

	test('friend requests, invites and boops are friend notifications', () => {
		for (const type of ['friendRequest', 'ignoredFriendRequest', 'invite', 'requestInvite', 'inviteResponse', 'requestInviteResponse', 'boop']) expect(notificationCategory(type)).toBe('friend');
	});

	test('group changes and event announcements are group notifications', () => {
		expect(notificationCategory('groupChange')).toBe('group');
		expect(notificationCategory('event.announcement')).toBe('group');
	});

	test('group. and moderation. types are group notifications', () => {
		for (const type of ['group.announcement', 'group.invite', 'group.transfer', 'moderation.warning']) expect(notificationCategory(type)).toBe('group');
	});

	test('anything else is other', () => {
		for (const type of ['OnPlayerJoined', 'GPS', 'VideoPlay']) expect(notificationCategory(type)).toBe('other');
	});
});

// The original's parseNotificationDetails.
describe('notification details', () => {
	test('object details are used as they are', () => {
		const details = { worldId: 'wrld_123' };
		expect(detailsOf(row({ details }))).toBe(details);
	});

	test('JSON string details are parsed', () => {
		expect(detailsOf(row({ details: '{"worldId":"wrld_123"}' }))).toEqual({ worldId: 'wrld_123' });
		expect(detailsOf(row({ details: '{}' }))).toEqual({});
	});

	test('details that are not JSON are empty', () => {
		expect(detailsOf(row({ details: 'not json' }))).toEqual({});
	});

	test('a JSON array is parsed like any other JSON', () => {
		expect(detailsOf(row({ details: '[1,2]' }))).toEqual([1, 2]);
	});

	test('no details are empty', () => {
		expect(detailsOf(row({ details: null }))).toEqual({});
		expect(detailsOf(row({ details: undefined }))).toEqual({});
	});
});

// The original's applyBoopLegacyHandling.
describe('boops', () => {
	test('other notifications are not boops', () => {
		expect(boopView(row({ type: 'friendRequest', title: 'Hello' }))).toBeUndefined();
	});

	test('a boop without a title keeps its message', () => {
		expect(boopView(row({ type: 'boop', title: '', message: 'existing' }))).toBeUndefined();
	});

	test('a default emoji boop is its title and the emoji name', () => {
		expect(boopView(row({ type: 'boop', title: 'Boop!', details: { emojiId: 'default_wave', emojiVersion: '1' } }))).toEqual({ text: 'Boop! wave' });
	});

	test('a custom emoji boop shows the emoji file', () => {
		expect(boopView(row({ type: 'boop', title: 'Boop!', details: { emojiId: 'emj_123', emojiVersion: '5' } }))).toEqual({ text: 'Boop!', imageUrl: 'https://api.vrchat.cloud/api/1/file/emj_123/5' });
	});
});
