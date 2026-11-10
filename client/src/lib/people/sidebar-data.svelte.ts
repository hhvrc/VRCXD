import { loadFriendsWithGroups } from '#lib/people/friend-groups.js';
import { isRealInstance } from '#lib/instances/location.js';
import { accountPath, api, ApiError } from '#lib/api/client.js';
import type { Friend } from '#lib/api/types.js';
import type { FriendGroup } from '#lib/components/sidebar/sidebar-utils.js';
import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// What the right-hand sidebar shows: friends with their favorite groups, the account itself, and
// open instances in the account's groups (the original refreshes these every five minutes).
export type Me = {
	id: string;
	displayName: string;
	iconFrame?: string;
	status: string;
	statusDescription: string;
	statusHistory: string[];
	isBoopingEnabled: boolean;
	imageUrl?: string | null;
	location?: string | null;
	travelingToLocation?: string | null;
	platform?: string | null;
	currentLocation?: string | null;
	isVrcPlus?: boolean;
	currentAvatar?: string | null;
	hasSharedConnectionsOptOut?: boolean;
	hasDiscordFriendsOptOut?: boolean;
	allowAvatarCopying?: boolean;
	homeLocation?: string | null;
	fallbackAvatar?: string | null;
	accountStanding?: string;
};
export type GroupInstance = { location: string; groupId: string; groupName: string; groupIconUrl?: string | null; userCount: number; capacity: number; ageGate: boolean };

class SidebarData {
	friends = $state.raw<Friend[]>([]);
	groups = $state.raw<FriendGroup[]>([]);
	me = $state.raw<Me | null>(null);
	groupInstances = $state.raw<GroupInstance[]>([]);
	currentLocation = $state<string | null>(null);
	/** The game's instance by the desktop app's game log and who is in it: the original's lastLocation.friendList. */
	gameInstance = $state.raw<{ location: string | null; playerIds: Set<string> }>({ location: null, playerIds: new Set() });
	refreshing = $state(false);
	error = $state('');

	/** The favorite groups the sidebar shows, all of them when none are picked: the original's VIP groups. */
	get vipGroups() {
		const picked = sidebarSettings.favoriteGroups;
		return this.groups.filter((g) => !picked.length || picked.includes(g.key));
	}

	/** Friends in those groups: the original's isVIP. */
	get vipIds() {
		return new Set(this.vipGroups.flatMap((g) => [...g.members]));
	}

	get onlineCount() {
		return this.friends.filter((f) => f.state === 'online').length;
	}

	async loadFriends() {
		if (!vrchatAuth.ownerUserId) return;
		try {
			({ friends: this.friends, groups: this.groups } = await loadFriendsWithGroups());
			this.error = '';
		} catch (err) {
			this.error = ApiError.from(err).message;
		}
	}

	async loadMe() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const me = await api.get<Me>(accountPath(account, '/me')).catch(() => null);
		if (!me) return;
		this.me = me;
		this.currentLocation = me.currentLocation ?? null;
	}

	async loadGameInstance() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const data = await api.get<{ location: string | null; playerIds: string[] }>(accountPath(account, '/game/instance')).catch(() => null);
		if (data) this.gameInstance = { location: data.location, playerIds: new Set(data.playerIds) };
	}

	/**
	 * The original's friendsInSameInstance location: a friend whose location VRChat hides but who is in
	 * the game's instance by the game log counts as being there (upstream PR 1138).
	 */
	sameInstanceLocation(friend: { userId: string; location?: string | null }): string {
		const location = friend.location ?? '';
		if (!isRealInstance(location) && this.gameInstance.location && this.gameInstance.playerIds.has(friend.userId)) return this.gameInstance.location;
		return location;
	}

	async loadGroupInstances() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		this.groupInstances = await api.get<GroupInstance[]>(accountPath(account, '/group-instances')).catch(() => this.groupInstances);
	}

	/** The refresh button: read the friend list from VRChat again, then reload. */
	async refresh() {
		const account = vrchatAuth.ownerUserId;
		if (!account || this.refreshing) return;
		this.refreshing = true;
		try {
			await api.post(accountPath(account, '/friends/refresh'), {}).catch(() => {});
			await Promise.all([this.loadFriends(), this.loadMe()]);
		} finally {
			this.refreshing = false;
		}
	}

	async saveMe(changes: { status?: string; statusDescription?: string }) {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		this.me = await api.put<Me>(accountPath(account, '/me'), changes);
	}

	/** The original's own-profile toggles: Avatar Cloning, Booping, Show Mutual Friends, Show Discord Connections. */
	async saveOptions(changes: { allowAvatarCopying?: boolean; isBoopingEnabled?: boolean; hasSharedConnectionsOptOut?: boolean; hasDiscordFriendsOptOut?: boolean }) {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const me = await api.put<Me>(accountPath(account, '/me/options'), changes);
		this.me = { ...me, currentLocation: me.currentLocation ?? this.me?.currentLocation };
	}
}

export const sidebarData = new SidebarData();
