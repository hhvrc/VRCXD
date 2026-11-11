import { describe, expect, test } from 'vitest';
import type { Noty } from '../notifications/noty.js';
import { feedNoty, gameLogNoty, pickWristHistory, wristAccepts, wristArrived, wristRow, wristRows, wristTravelling } from '../notifications/wrist-feed.svelte.js';

const base = (over: Partial<Noty>): Noty => ({ type: 'Online', createdAt: '2026-11-11T12:34:00Z', displayName: 'Bee', userId: 'usr_bee', isFriend: true, isFavorite: false, ...over });

describe('wrist feed', () => {
	test('takes the rows the wrist filter passes, never your own or friend requests', () => {
		expect(wristAccepts(base({}), 'other', 'Friends', 'usr_me', false)).toBe(true);
		expect(wristAccepts(base({ isFriend: false }), 'other', 'Friends', 'usr_me', false)).toBe(false);
		expect(wristAccepts(base({ userId: 'usr_me' }), 'other', 'On', 'usr_me', false)).toBe(false);
		expect(wristAccepts(base({ type: 'FriendRequest' }), 'notification', 'On', 'usr_me', false)).toBe(false);
		expect(wristAccepts(base({}), 'other', 'Off', 'usr_me', false)).toBe(false);
	});

	test('hides private worlds when asked', () => {
		const gps = base({ type: 'GPS', location: 'private' });
		expect(wristAccepts(gps, 'other', 'On', 'usr_me', true)).toBe(false);
		expect(wristAccepts(gps, 'other', 'On', 'usr_me', false)).toBe(true);
	});

	test('a row reads as the minimal feed: time, name and what happened', () => {
		const row = wristRow(base({ isFavorite: true }), 'en-GB');
		expect(row).toMatchObject({ name: 'Bee', text: 'has logged in', friend: true, favorite: true });
		expect(row?.time).toMatch(/^\d\d:\d\d$/);
	});

	test('the history on start takes the newest rows the wrist filters pass, from every source', () => {
		const at = (m: number) => `2026-11-11T12:${String(m).padStart(2, '0')}:00Z`;
		const picked = pickWristHistory(
			[
				{ noty: feedNoty({ type: 'GPS', rowId: 1, createdAt: at(1), userId: 'usr_ann', displayName: 'Ann', location: 'wrld_a:1', worldName: 'Club' }), source: 'other' },
				{ noty: feedNoty({ type: 'Online', rowId: 2, createdAt: at(5), userId: 'usr_stranger', displayName: 'Stranger' }), source: 'other' },
				{ noty: gameLogNoty({ key: 'v', id: 3, type: 'VideoPlay', createdAt: at(3), videoName: 'Song', displayName: 'Bee', isFriend: false, isFavorite: false }), source: 'other' },
				{ noty: gameLogNoty({ key: 'j', id: 4, type: 'OnPlayerJoined', createdAt: at(4), userId: 'usr_me', displayName: 'Me', isFriend: false, isFavorite: false }), source: 'other' }
			],
			{ GPS: 'Friends', Online: 'Friends', VideoPlay: 'On', OnPlayerJoined: 'Everyone' },
			{ meId: 'usr_me', isFriend: (id) => id === 'usr_ann', isFavorite: () => false },
			false
		);
		expect(picked.map((n) => n.type)).toEqual(['VideoPlay', 'GPS']);
		expect(picked[0].notyName).toBe('Song (Bee)');
		expect(picked[1]).toMatchObject({ isFriend: true, worldName: 'Club' });
	});

	test('travelling friends stay pinned on top until they arrive', () => {
		const joining = (userId: string, name: string, isFavorite = false) => base({ type: 'OnPlayerJoining', userId, displayName: name, isFavorite });
		wristTravelling(joining('usr_bee', 'Bee'), true, '', 'Friends');
		wristTravelling(joining('usr_cat', 'Cat'), false, 'Club #1', 'Friends');
		wristTravelling(joining('usr_dan', 'Dan'), false, 'Club #1', 'VIP');
		expect(wristRows().map((r) => [r.name, r.text])).toEqual([
			['Cat', 'is traveling to Club #1'],
			['Bee', 'is joining']
		]);
		wristArrived('usr_cat');
		wristArrived('usr_bee');
		expect(wristRows()).toEqual([]);
	});

	test('your own instance changes read as just the place, in 12-hour time when asked', () => {
		const row = wristRow(base({ type: 'Location', createdAt: '2026-11-11T13:05:00', location: 'wrld_a:1~private(usr_x)', worldName: 'Club' }), 'en-US', true);
		expect(row?.name).toBe('');
		expect(row?.text).toContain('Club');
		expect(row?.time).toMatch(/PM/);
	});
});
