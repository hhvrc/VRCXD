import { describe, expect, test } from 'vitest';
import type { Favorites, Friend } from '../api/types.js';
import { buildFriendGroups, sameInstanceGroups, searchFriends } from '../people/friend-groups.js';

const friend = (userId: string, extra: Partial<Friend> = {}) => ({ userId, displayName: userId, state: 'online', ...extra }) as Friend;

describe('buildFriendGroups', () => {
	const favorites = {
		groups: [
			{ name: 'group_0', displayName: 'Closies' },
			{ name: 'group_1', displayName: '' }
		],
		favorites: [
			{ groupName: 'group_0', targetId: 'usr_a' },
			{ groupName: 'group_1', targetId: 'usr_b' }
		]
	} as unknown as Favorites;

	test('VRChat groups use the original keys, then local groups, empty ones included', () => {
		const groups = buildFriendGroups(favorites, [{ groupName: 'Mine', targetId: 'usr_c' }], ['Empty', 'Mine']);
		expect(groups.map((g) => g.key)).toEqual(['friend:group_0', 'friend:group_1', 'local:Empty', 'local:Mine']);
		expect(groups[0].name).toBe('Closies');
		expect(groups[1].name).toBe('group_1');
		expect([...groups[0].members]).toEqual(['usr_a']);
		expect(groups[2].members.size).toBe(0);
		expect([...groups[3].members]).toEqual(['usr_c']);
	});
});

describe('Friends Locations', () => {
	test('instance blocks need two friends in one real instance, biggest first', () => {
		const online = [
			friend('a', { location: 'wrld_1:1' }),
			friend('b', { location: 'wrld_1:1' }),
			friend('c', { location: 'wrld_2:9' }),
			friend('d', { location: 'wrld_2:9' }),
			friend('e', { location: 'wrld_2:9' }),
			friend('f', { location: 'private' }),
			friend('g', { location: 'private' }),
			friend('h', { location: 'wrld_3:3' })
		];
		const blocks = sameInstanceGroups(online, (f) => f.location ?? '');
		expect(blocks.map((b) => b.instanceId)).toEqual(['wrld_2:9', 'wrld_1:1']);
		expect(blocks[0].members.map((f) => f.userId)).toEqual(['c', 'd', 'e']);
	});

	test('search matches name, status or world and lists each friend once', () => {
		const alice = friend('alice', { displayName: 'Alice', statusDescription: 'Chilling' });
		const bob = friend('bob', { displayName: 'Bob', location: 'wrld_cozy:1' });
		const carol = friend('carol', { displayName: 'Carol', state: 'offline' });
		const world = (f: Friend) => (f.location === 'wrld_cozy:1' ? 'Cozy Cabin' : '');
		expect(searchFriends([[alice], [alice, bob, carol]], 'ali', world).map((f) => f.userId)).toEqual(['alice']);
		expect(searchFriends([[alice, bob, carol]], 'chill', world).map((f) => f.userId)).toEqual(['alice']);
		expect(searchFriends([[alice, bob, carol]], ' COZY ', world).map((f) => f.userId)).toEqual(['bob']);
		expect(searchFriends([[alice, bob, carol]], 'nobody', world)).toEqual([]);
	});
});
