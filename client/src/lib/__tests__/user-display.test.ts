import { describe, expect, test } from 'vitest';
import { friendedInfo, languageClass, sortStatus, trustInfo, userOnlineFor } from '../people/user-display.js';
import { timeAgo } from '../helpers/format.js';

// The original's computeTrustLevel, languageClass and sortStatus.
describe('trust rank', () => {
	test('no rank tags is a Visitor', () => {
		expect(trustInfo([])).toMatchObject({ label: 'Visitor', className: 'x-tag-untrusted', colorKey: 'untrusted', sortNum: 1 });
	});

	test('a veteran tag is a Trusted User', () => {
		expect(trustInfo(['system_trust_veteran'])).toMatchObject({ label: 'Trusted User', className: 'x-tag-veteran', colorKey: 'veteran', sortNum: 5 });
	});

	test('a trusted tag is a Known User', () => {
		expect(trustInfo(['system_trust_trusted'])).toMatchObject({ label: 'Known User', sortNum: 4 });
	});

	test('a known tag is a User', () => {
		expect(trustInfo(['system_trust_known'])).toMatchObject({ label: 'User', sortNum: 3 });
	});

	test('a basic tag is a New User', () => {
		expect(trustInfo(['system_trust_basic'])).toMatchObject({ label: 'New User', sortNum: 2 });
	});

	test('a troll gets the troll colour and sorts a tenth after their rank', () => {
		const info = trustInfo(['system_troll', 'system_trust_known']);
		expect(info.colorKey).toBe('troll');
		expect(info.sortNum).toBeCloseTo(3.1);
	});

	test('a probable troll gets the troll colour too', () => {
		const info = trustInfo(['system_probable_troll', 'system_trust_basic']);
		expect(info.colorKey).toBe('troll');
		expect(info.sortNum).toBeCloseTo(2.1);
	});

	test('troll and probable troll together count once', () => {
		expect(trustInfo(['system_troll', 'system_probable_troll']).sortNum).toBeCloseTo(1.1);
	});

	test('a developer type makes a moderator, in the VIP colour', () => {
		const info = trustInfo([], 'internal');
		expect(info.colorKey).toBe('vip');
		expect(info.sortNum).toBeCloseTo(1.3);
	});

	test('the admin_moderator tag makes a moderator', () => {
		const info = trustInfo(['admin_moderator', 'system_trust_veteran']);
		expect(info.colorKey).toBe('vip');
		expect(info.sortNum).toBeCloseTo(5.3);
	});

	test('developer type none is not a moderator', () => {
		expect(trustInfo([], 'none')).toMatchObject({ colorKey: 'untrusted', sortNum: 1 });
	});
});

describe('language flags', () => {
	test('known language codes map to their flag', () => {
		expect(languageClass('eng')).toBe('us');
		expect(languageClass('jpn')).toBe('jp');
		expect(languageClass('kor')).toBe('kr');
	});

	test('anything else is the unknown flag', () => {
		expect(languageClass('xyz')).toBe('unknown');
		expect(languageClass('')).toBe('unknown');
	});
});

describe('status order', () => {
	const order = ['join me', 'active', 'ask me', 'busy', 'offline'];

	test('the same status is a tie', () => {
		for (const status of order) expect(sortStatus(status, status)).toBe(0);
	});

	test('join me, active, ask me, busy, offline: an earlier status sorts first', () => {
		for (let i = 0; i < order.length; i++) for (let j = i + 1; j < order.length; j++) expect(sortStatus(order[i], order[j])).toBe(-1);
	});

	test('a later status sorts after an earlier one', () => {
		expect(sortStatus('active', 'join me')).toBe(1);
		expect(sortStatus('busy', 'active')).toBe(1);
		expect(sortStatus('offline', 'join me')).toBe(1);
		expect(sortStatus('offline', 'busy')).toBe(1);
	});

	test('an unknown status is a tie', () => {
		expect(sortStatus('unknown', 'active')).toBe(0);
		expect(sortStatus('active', 'unknown')).toBe(0);
		expect(sortStatus(null as unknown as string, 'active')).toBe(0);
	});
});

// The original's userOnlineForTimestamp and the user pop-up's Online For / Offline For row. The
// server keeps one "since" for the current state, so online, active and offline all read it.
describe('online for and offline for', () => {
	const at = '2024-01-01T12:00:00.000Z';

	test('an online user with a time is Online For that time', () => {
		expect(userOnlineFor('online', at)).toEqual({ online: true, at });
	});

	test('an active user shows their time under Offline For', () => {
		expect(userOnlineFor('active', at)).toEqual({ online: false, at });
	});

	test('an offline user is Offline For their time', () => {
		expect(userOnlineFor('offline', at)).toEqual({ online: false, at });
	});

	test('no time is known: Offline For, with no time', () => {
		expect(userOnlineFor('offline', null)).toEqual({ online: false, at: null });
		expect(userOnlineFor('online', undefined)).toEqual({ online: false, at: null });
		expect(timeAgo(null)).toBe('-');
	});

	test('the row ticks from the clock it is given', () => {
		const start = Date.parse(at);
		expect(timeAgo(at, start + 90 * 60_000)).toBe('1 hour, 30 mins');
		expect(timeAgo(at, start + 2 * 86_400_000)).toBe('2 days');
		expect(timeAgo(at, start + 12_000)).toBe('10 secs');
	});
});

// The original's showUserDialog: dateFriendedInfo from the friend log, newest first.
describe('friended or unfriended', () => {
	const log = [
		{ type: 'Unfriend', createdAt: '2026-03-01T00:00:00Z' },
		{ type: 'Friend', createdAt: '2026-02-01T00:00:00Z' }
	];

	test('the newest row decides the label and the time', () => {
		expect(friendedInfo(log, false)).toEqual({ unfriended: true, at: '2026-03-01T00:00:00Z', entries: log });
	});

	test('hidden unfriends leave only the friend rows', () => {
		expect(friendedInfo(log, true)).toEqual({ unfriended: false, at: '2026-02-01T00:00:00Z', entries: [log[1]] });
	});

	test('no rows is Friended with no time', () => {
		expect(friendedInfo(undefined, false)).toEqual({ unfriended: false, at: null, entries: [] });
	});
});
