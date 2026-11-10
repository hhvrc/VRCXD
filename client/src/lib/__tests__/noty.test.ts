import { describe, expect, it } from 'vitest';
import { conditionMet, afkMet, defaultSharedFeedFilters, displayLocation, filterPasses, normalizeSharedFeedFilters, notificationMessage, notificationText, notyMessage, shouldPlay, type Noty } from '../notifications/noty.js';

const base = (over: Partial<Noty>): Noty => ({ type: 'Online', createdAt: new Date().toJSON(), isFriend: true, isFavorite: false, ...over });

describe('noty filters', () => {
	it('fills missing keys and moves an On boop to Friends', () => {
		const f = normalizeSharedFeedFilters({ noty: { boop: 'On', GPS: 'Friends' } });
		expect(f.noty.boop).toBe('Friends');
		expect(f.noty.GPS).toBe('Friends');
		expect(f.noty.Online).toBe('VIP');
		expect(f.wrist.Location).toBe('On');
		expect(defaultSharedFeedFilters().noty['group.joinRequest']).toBe('Off');
	});

	it('passes values the way the original queues do', () => {
		const stranger = { isFriend: false, isFavorite: false };
		expect(filterPasses('Friends', stranger, 'notification')).toBe(true);
		expect(filterPasses('Friends', stranger, 'other')).toBe(false);
		expect(filterPasses('VIP', { isFriend: true, isFavorite: true }, 'other')).toBe(true);
		expect(filterPasses('Everyone', stranger, 'other')).toBe(true);
		expect(filterPasses('Off', stranger, 'notification')).toBe(false);
	});
});

describe('noty conditions', () => {
	const game = { gameRunning: true, steamVrRunning: false, noVr: true, hmdAfk: false };
	it('reads each condition', () => {
		expect(conditionMet('Desktop Mode', game)).toBe(true);
		expect(conditionMet('Inside VR', game)).toBe(false);
		expect(conditionMet('Game Closed', game)).toBe(false);
		expect(conditionMet('Never', game)).toBe(false);
		expect(afkMet(true, { ...game, noVr: false, hmdAfk: true })).toBe(true);
	});
});

describe('noty wording', () => {
	it('words presence and invites as the original', () => {
		expect(notificationMessage(base({ displayName: 'Ann' }), '')).toEqual({ title: 'Ann', body: 'has logged in' });
		expect(displayLocation('wrld_1:123~friends(usr_1)', 'Home')).toBe('Home friends');
		const invite = base({ type: 'invite', senderUsername: 'Bo', details: { worldId: 'wrld_1:5', worldName: 'Hub', inviteMessage: 'come' } });
		const msg = notyMessage(invite);
		expect(msg).toBe(', come');
		expect(notificationMessage(invite, msg)).toEqual({ title: 'Bo', body: 'has invited you to Hub public , come' });
	});

	it('words portal spawns and avatar changes from the game log', () => {
		expect(notificationMessage(base({ type: 'PortalSpawn' }), '')).toEqual({ title: '', body: 'User has spawned a portal' });
		expect(notificationMessage(base({ type: 'PortalSpawn', displayName: 'Ann', location: 'wrld_1:123~friends(usr_1)', worldName: 'Home' }), '')).toEqual({ title: 'Ann', body: 'has spawned a portal to Home friends' });
		expect(notificationMessage(base({ type: 'AvatarChange', displayName: 'Ann', data: 'Fox' }), '')).toEqual({ title: 'Ann', body: 'changed into avatar Fox' });
	});

	it('joins title and body per type', () => {
		expect(notificationText('Event', 'Test notification.', 'Event')).toBe('Test notification.');
		expect(notificationText('Now playing', 'Song', 'VideoPlay')).toBe('Now playing: Song');
		expect(notificationText('Ann', 'has joined', 'OnPlayerJoined')).toBe('Ann has joined');
		expect(notificationText('Ann', '', 'MutedOnPlayerLeft')).toBe('Muted user Ann has left');
	});
});

describe('shouldPlay', () => {
	it('plays a fresh one once and skips old ones', () => {
		const seen = new Map<string, string>();
		const noty = base({ displayName: 'Ann' });
		expect(shouldPlay(noty, seen)).toBe(true);
		expect(shouldPlay(noty, seen)).toBe(false);
		expect(shouldPlay(base({ displayName: 'Cy', createdAt: new Date(Date.now() - 120_000).toJSON() }), seen)).toBe(false);
	});
});
