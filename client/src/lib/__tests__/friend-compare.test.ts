import { afterEach, describe, expect, test } from 'vitest';
import type { Friend } from '../api/types.js';
import { appearance, sortInstanceUsers } from '../settings/appearance.svelte.js';
import { friendComparator } from '../people/friend-sort.js';

// The original's compare.js friend and instance user orders: friend-sort.ts for the sidebar's sort
// methods, sortInstanceUsers for Sort Users in Instance by.

const friend = (overrides: Partial<Friend>): Friend => ({
	friendNumber: 0,
	friendsSince: '2020-01-01T00:00:00Z',
	userId: 'usr_x',
	displayName: 'Same',
	trustLevel: 'Known User',
	state: 'online',
	status: 'active',
	...overrides
});

const by = (method: string) => friendComparator([method]);

describe('friend sort methods', () => {
	test('private to bottom puts friends in a private instance after everyone else', () => {
		const sort = by('Sort Private to Bottom');
		const open = friend({ location: 'wrld_a:1' });
		const hidden = friend({ location: 'private' });
		expect(sort(open, hidden)).toBe(-1);
		expect(sort(hidden, open)).toBe(1);
	});

	test('private to bottom leaves two private or two visible friends in place', () => {
		const sort = by('Sort Private to Bottom');
		expect(sort(friend({ location: 'private' }), friend({ location: 'private' }))).toBe(0);
		expect(sort(friend({ location: 'wrld_a:1' }), friend({ location: 'wrld_b:2~friends(usr_y)' }))).toBe(0);
	});

	test('status puts an offline friend last and leaves equal statuses in place', () => {
		const sort = by('Sort by Status');
		expect(sort(friend({ state: 'offline', status: 'active' }), friend({ status: 'busy' }))).toBe(1);
		expect(sort(friend({ status: 'active' }), friend({ status: 'active' }))).toBe(0);
		expect(sort(friend({ status: 'join me' }), friend({ status: 'busy' }))).toBeLessThan(0);
	});

	test('last active: online friends by how long they have been online, newest first', () => {
		const sort = by('Sort by Last Active');
		const earlier = friend({ onlineFor: '2023-01-01T00:00:00Z' });
		const later = friend({ onlineFor: '2023-01-02T00:00:00Z' });
		expect(sort(earlier, later)).toBe(1);
		expect(sort(later, earlier)).toBe(-1);
	});

	test('last active: online at the same moment falls back to the last login', () => {
		const sort = by('Sort by Last Active');
		const a = friend({ onlineFor: '2023-01-01T00:00:00Z', lastLogin: '2023-01-01T00:00:00Z' });
		const b = friend({ onlineFor: '2023-01-01T00:00:00Z', lastLogin: '2023-01-02T00:00:00Z' });
		expect(sort(a, b)).toBe(1);
		expect(sort(b, a)).toBe(-1);
	});

	test('last active: friends not both online compare by last activity, newest first', () => {
		const sort = by('Sort by Last Active');
		const a = friend({ state: 'offline', lastActivity: '2023-01-01T00:00:00Z' });
		const b = friend({ state: 'offline', lastActivity: '2023-01-02T00:00:00Z' });
		expect(sort(a, b)).toBe(1);
		const online = friend({ state: 'online', lastActivity: '2023-06-01T00:00:00Z' });
		expect(sort(online, a)).toBe(-1);
	});

	test('last seen compares the last activity, newest first, with the original empty value rule', () => {
		const sort = by('Sort by Last Seen');
		expect(sort(friend({ lastActivity: '2023-01-01' }), friend({ lastActivity: '2023-01-02' }))).toBe(1);
		// As the original: an empty value against a real one compares as 1 either way round.
		expect(sort(friend({ lastActivity: '' }), friend({ lastActivity: '2023-01-01' }))).toBe(1);
		expect(sort(friend({ lastActivity: '2023-01-01' }), friend({ lastActivity: '' }))).toBe(1);
	});

	test('location sorts online friends by location and leaves anyone offline in place', () => {
		const sort = by('Sort by Location');
		expect(sort(friend({ location: 'wrld_b:1' }), friend({ location: 'wrld_a:1' }))).toBeGreaterThan(0);
		expect(sort(friend({ state: 'offline', location: 'wrld_b:1' }), friend({ state: 'offline', location: 'wrld_a:1' }))).toBe(0);
		expect(sort(friend({ location: 'wrld_b:1' }), friend({ state: 'offline', location: 'wrld_a:1' }))).toBe(0);
		expect(sort(friend({ location: 'ä' }), friend({ location: 'z' }))).toBeLessThan(0);
	});

	test('every method ends with the name', () => {
		const sort = by('Sort Private to Bottom');
		expect(sort(friend({ displayName: 'Alice' }), friend({ displayName: 'Bob' }))).toBeLessThan(0);
		expect(sort(friend({ displayName: 'Bob' }), friend({ displayName: 'Alice' }))).toBeGreaterThan(0);
		expect(sort(friend({ displayName: 'Alice' }), friend({ displayName: 'Alice' }))).toBe(0);
	});
});

describe('instance user order', () => {
	afterEach(() => {
		appearance.instanceUsersSortAlphabetical.value = false;
	});

	test('by time in the instance: longest there first, anyone traveling last', () => {
		appearance.instanceUsersSortAlphabetical.value = false;
		const users = [
			{ displayName: 'Traveler', location: 'traveling', locationSince: '2023-01-01T00:00:00Z' },
			{ displayName: 'Newer', location: 'wrld_a:1', locationSince: '2023-01-02T00:00:00Z' },
			{ displayName: 'Older', location: 'wrld_a:1~friends(usr_x)', locationSince: '2023-01-01T00:00:00Z' }
		];
		expect(sortInstanceUsers(users).map((u) => u.displayName)).toEqual(['Older', 'Newer', 'Traveler']);
	});

	test('two travelers and two arrivals at the same moment keep their order', () => {
		appearance.instanceUsersSortAlphabetical.value = false;
		const travelers = [
			{ displayName: 'B', location: 'traveling', locationSince: '2023-01-02T00:00:00Z' },
			{ displayName: 'A', location: 'traveling', locationSince: '2023-01-01T00:00:00Z' }
		];
		expect(sortInstanceUsers(travelers).map((u) => u.displayName)).toEqual(['B', 'A']);
		const same = [
			{ displayName: 'B', location: 'wrld_a:1', locationSince: '2023-01-01T00:00:00Z' },
			{ displayName: 'A', location: 'wrld_b:1', locationSince: '2023-01-01T00:00:00Z' }
		];
		expect(sortInstanceUsers(same).map((u) => u.displayName)).toEqual(['B', 'A']);
	});

	test('alphabetical sorts by display name and leaves users without one in place', () => {
		appearance.instanceUsersSortAlphabetical.value = true;
		expect(sortInstanceUsers([{ displayName: 'Bob Display' }, { displayName: 'Alice Display' }]).map((u) => u.displayName)).toEqual(['Alice Display', 'Bob Display']);
		expect(sortInstanceUsers([{ displayName: 'Same' }, { displayName: 'Same' }]).length).toBe(2);
		const missing = [{ displayName: 'Bob' }, {}, { displayName: 'Alice' }];
		const sorted = sortInstanceUsers(missing);
		expect(sorted.length).toBe(3);
		expect(sorted).toContain(missing[1]);
	});
});
