import { api } from '#lib/api/client.js';
import type { Favorites, Friend } from '#lib/api/types.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
import { isRealInstance } from '#lib/instances/location.js';

export type LocalFavorite = { groupName: string; targetId: string };
export type FriendGroup = { key: string; name: string; members: Set<string> };

/**
 * The original's favoriteFriendGroups and localFriendFavoriteGroups: VRChat groups keyed
 * friend:group_N, then local groups keyed local:Name, empty local groups included.
 */
export function buildFriendGroups(favorites: Favorites, local: LocalFavorite[], localGroupNames: string[] = []): FriendGroup[] {
	const remote = favorites.groups.map((group) => ({
		key: `friend:${group.name}`,
		name: group.displayName || group.name,
		members: new Set(favorites.favorites.filter((f) => f.groupName === group.name).map((f) => f.targetId))
	}));
	const localNames = [...new Set([...localGroupNames, ...local.map((f) => f.groupName)])];
	return [...remote, ...localNames.map((name) => ({ key: `local:${name}`, name, members: new Set(local.filter((f) => f.groupName === name).map((f) => f.targetId)) }))];
}

/** Two or more friends in one real instance, biggest first: the Friends Locations instance blocks. */
export function sameInstanceGroups(online: Friend[], locationOf: (friend: Friend) => string): { instanceId: string; members: Friend[] }[] {
	const byLocation = new Map<string, Friend[]>();
	for (const friend of online) {
		const location = locationOf(friend);
		if (!isRealInstance(location)) continue;
		const list = byLocation.get(location);
		if (list) list.push(friend);
		else byLocation.set(location, [friend]);
	}
	return [...byLocation.entries()]
		.filter(([, members]) => members.length > 1)
		.map(([instanceId, members]) => ({ instanceId, members }))
		.sort((a, b) => b.members.length - a.members.length);
}

/** The Friends Locations search: name, status and world name, each friend once. */
export function searchFriends(lists: Friend[][], term: string, worldName: (friend: Friend) => string): Friend[] {
	const unique = new Map<string, Friend>();
	for (const friend of lists.flat()) unique.set(friend.userId, friend);
	const needle = term.trim().toLowerCase();
	return [...unique.values()].filter((friend) => `${friend.displayName} ${friend.statusDescription ?? ''} ${worldName(friend)}`.toLowerCase().includes(needle));
}

/** The friends list with the favorite groups that sort it, as the sidebar and Friends Locations show it. */
export async function loadFriendsWithGroups(): Promise<{ friends: Friend[]; groups: FriendGroup[] }> {
	const [friends, favorites, local, localGroups] = await Promise.all([
		api.get<Friend[]>(ownPath('/friends')),
		api.get<Favorites>(ownPath('/favorites'), { type: 'friend' }),
		api.get<LocalFavorite[]>(ownPath('/favorites/local'), { type: 'friend' }),
		api.get<{ name: string }[]>(ownPath('/favorites/local/groups'), { type: 'friend' }).catch(() => [])
	]);
	return { friends, groups: buildFriendGroups(favorites, local, localGroups.map((g) => g.name)) };
}
