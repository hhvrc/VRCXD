import { describe, expect, test } from 'vitest';
import { ownStatusClass, userStatusClass } from '../components/sidebar/sidebar-utils.js';
import { statusClass } from '../helpers/format.js';

// The original's statusClass and userStatusClass: the status dot's classes.
describe('status dot for a status', () => {
	test('active is the online dot', () => {
		expect(statusClass('active')).toBe('online');
	});

	test('join me, ask me and busy have their own dots', () => {
		expect(statusClass('join me')).toBe('joinme');
		expect(statusClass('ask me')).toBe('askme');
		expect(statusClass('busy')).toBe('busy');
	});

	test('no status and unknown statuses have no dot', () => {
		expect(statusClass(undefined)).toBe('');
		expect(statusClass('offline')).toBe('');
		expect(statusClass('unknown')).toBe('');
	});
});

describe('status dot for a friend', () => {
	test('a friend pending offline shows offline', () => {
		expect(userStatusClass({ state: 'online', status: 'active', location: 'wrld_1:1' }, true)).toBe('status-icon offline');
	});

	test('each status of an online friend has its dot', () => {
		const cases = { active: 'online', 'join me': 'joinme', 'ask me': 'askme', busy: 'busy' };
		for (const [status, dot] of Object.entries(cases)) expect(userStatusClass({ state: 'online', status, location: 'wrld_1:1' }).split(' ')).toContain(dot);
	});

	test('a friend whose location is offline shows offline', () => {
		expect(userStatusClass({ state: '', status: 'active', location: 'offline' })).toBe('status-icon offline');
	});

	test('a private friend without an online state shows offline', () => {
		expect(userStatusClass({ state: '', status: 'busy', location: 'private' })).toBe('status-icon offline');
	});

	test('an active (website) friend gets the active dots', () => {
		expect(userStatusClass({ state: 'active', status: 'join me' })).toBe('status-icon active-joinme');
		expect(userStatusClass({ state: 'active', status: 'active' })).toBe('status-icon active');
	});

	test('an online friend on a phone or headset is marked mobile, a PC one is not', () => {
		expect(userStatusClass({ state: 'online', status: 'active', location: 'wrld_1:1', platform: 'android' })).toBe('status-icon online mobile');
		expect(userStatusClass({ state: 'online', status: 'active', location: 'wrld_1:1', platform: 'standalonewindows' })).toBe('status-icon online');
	});

	test('an unknown status has no dot', () => {
		expect(userStatusClass({ state: 'online', status: 'weird', location: 'wrld_1:1' })).toBe('');
	});
});

describe('status dot for the current user', () => {
	test('is the status alone', () => {
		expect(ownStatusClass('busy')).toBe('status-icon busy');
		expect(ownStatusClass('active', 'standalonewindows')).toBe('status-icon online');
	});

	test('is marked mobile on a platform other than PC', () => {
		expect(ownStatusClass('active', 'android')).toBe('status-icon online mobile');
	});
});
