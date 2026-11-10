import { describe, expect, test } from 'vitest';
import type { Friend } from '../api/types.js';
import { friendComparator } from '../people/friend-sort.js';

// The original's getFriendsSortFunction and its compareBy* sorts, over the server's friend rows.
const friend = (fields: Partial<Friend>): Friend => ({ friendNumber: 0, friendsSince: '', userId: 'usr_x', displayName: '', trustLevel: '', state: 'online', status: 'active', ...fields }) as Friend;

describe('friend sort', () => {
	test('alphabetical sorts by display name', () => {
		const compare = friendComparator(['Sort Alphabetically']);
		const alice = friend({ displayName: 'Alice' });
		const bob = friend({ displayName: 'Bob' });
		expect(compare(alice, bob)).toBeLessThan(0);
		expect(compare(bob, alice)).toBeGreaterThan(0);
	});

	test('private to bottom puts private locations last', () => {
		const compare = friendComparator(['Sort Private to Bottom']);
		const open = friend({ location: 'wrld_1:1', displayName: 'Same' });
		const hidden = friend({ location: 'private', displayName: 'Same' });
		expect(compare(hidden, open)).toBe(1);
		expect(compare(open, hidden)).toBe(-1);
	});

	test('by status sorts join me before busy', () => {
		const compare = friendComparator(['Sort by Status']);
		expect(compare(friend({ status: 'join me' }), friend({ status: 'busy' }))).toBeLessThan(0);
	});

	test('by last active puts the most recently active first', () => {
		const compare = friendComparator(['Sort by Last Active']);
		const older = friend({ state: 'offline', lastActivity: '2023-01-01' });
		const newer = friend({ state: 'offline', lastActivity: '2023-06-01' });
		expect(compare(older, newer)).toBe(1);
	});

	test('by last seen puts the most recently seen first', () => {
		const compare = friendComparator(['Sort by Last Seen']);
		expect(compare(friend({ lastActivity: '2023-01-01' }), friend({ lastActivity: '2023-06-01' }))).toBe(1);
	});

	test('by time in instance puts friends pending offline at the bottom', () => {
		const compare = friendComparator(['Sort by Time in Instance']);
		const pending = friend({ pendingOffline: true, locationSince: '2023-06-01T00:00:00Z' });
		const here = friend({ pendingOffline: false, locationSince: '2023-01-01T00:00:00Z' });
		expect(compare(pending, here)).toBe(1);
		expect(compare(here, pending)).toBe(-1);
	});

	test('by time in instance puts the newest arrival first', () => {
		const compare = friendComparator(['Sort by Time in Instance']);
		const early = friend({ locationSince: '2023-01-01T00:00:00Z' });
		const late = friend({ locationSince: '2023-06-01T00:00:00Z' });
		expect(compare(late, early)).toBeLessThan(0);
	});

	test('by location sorts online friends by their location', () => {
		const compare = friendComparator(['Sort by Location']);
		expect(compare(friend({ location: 'aaa' }), friend({ location: 'zzz' }))).toBeLessThan(0);
	});

	test('a tie falls through to the next sort, and names break the last tie', () => {
		const compare = friendComparator(['Sort by Status', 'Sort Alphabetically']);
		expect(compare(friend({ displayName: 'Alice' }), friend({ displayName: 'Bob' }))).toBeLessThan(0);
		expect(friendComparator(['None'])(friend({ displayName: 'Alice' }), friend({ displayName: 'Bob' }))).toBeLessThan(0);
	});

	test('the first sort decides when it is not a tie', () => {
		const compare = friendComparator(['Sort by Status', 'Sort Alphabetically']);
		expect(compare(friend({ displayName: 'Zack', status: 'join me' }), friend({ displayName: 'Alice', status: 'busy' }))).toBeLessThan(0);
	});
});
