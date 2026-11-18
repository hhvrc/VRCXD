import { loadFriendGroups } from '#lib/people/friend-groups.js';
import { isRealInstance } from '#lib/instances/location.js';
import { accountPath, api, ApiError } from '#lib/api/client.js';
import type { Friend } from '#lib/api/types.js';
import type { ServerEvent } from '#lib/gen/vrcx_live_pb.js';
import { timestampDate } from '@bufbuild/protobuf/wkt';
import type { FriendGroup } from '#lib/components/sidebar/sidebar-utils.js';
import { debounce } from '#lib/helpers/debounce.js';
import { patchFriendPresence, planSidebarEvent, type SidebarPlan } from '#lib/people/sidebar-events.js';
import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
import { friendsTopic } from '#lib/live/topics/friends.svelte.js';
import type { TopicHandle } from '#lib/api/topics.svelte.js';

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

type GameInstance = { location: string | null; playerIds: Set<string>; joinedAt?: string | null };

// One copy for the whole app: the sidebar, Friends Locations, the feed's menus, the user dialog,
// the notifications and the wrist overlay all read these. start() loads them once per account and
// keeps them current from the live socket (sidebar-live.ts), so a page or the sidebar mounting
// again reads them as they are instead of asking the server again.
//
// The friend list comes from the live socket's friends topic: a snapshot and then the server's
// deltas, so it is never read over REST. A server without that topic (older than topics), or a
// socket that does not come up in time, gets the list from GET friends and keeps it current from
// the ServerEvents as before (the fallback below: loadFriends, patchFriendPresence and the reloads).

/** How long the friend list waits for the topic's snapshot before reading it over REST once. */
export const FRIENDS_TOPIC_WAIT_MS = 4000;

class SidebarData {
	friends = $state.raw<Friend[]>([]);
	groups = $state.raw<FriendGroup[]>([]);
	me = $state.raw<Me | null>(null);
	groupInstances = $state.raw<GroupInstance[]>([]);
	currentLocation = $state<string | null>(null);
	/** The game's instance by the desktop app's game log and who is in it: the original's lastLocation.friendList. */
	gameInstance = $state.raw<GameInstance>({ location: null, playerIds: new Set() });
	/** The friend list has been read for this account (Friends Locations shows its spinner until then). */
	friendsLoaded = $state(false);
	refreshing = $state(false);
	error = $state('');

	/** The account the data above belongs to. */
	#owner = '';
	#running = new Map<string, Promise<void>>();
	#ids: { of: Friend[]; ids: Set<string> } = { of: [], ids: new Set() };
	/** The friends topic for #owner, while held. */
	#topic: TopicHandle<Friend[]> | undefined;
	/** The list follows the topic: the events and reconnects need not read it. */
	#viaTopic = false;
	#topicWait: ReturnType<typeof setTimeout> | undefined;
	/** Settles once the friend list is first there for #owner, by the topic or by REST. */
	#friendsReady: { promise: Promise<void>; resolve: () => void } = SidebarData.#deferred();

	static #deferred() {
		let resolve = () => {};
		const promise = new Promise<void>((r) => (resolve = r));
		return { promise, resolve };
	}

	// Reads the events can't stand in for, held while events keep coming but run at least every
	// 10 seconds (the original's friend store has no such reads: its pipeline events carry the users).
	#reloadFriends = debounce(() => this.loadFriends(true), { wait: 1500, maxWait: 10_000 });
	#reloadFavorites = debounce(() => this.loadFavorites(true), { wait: 1500, maxWait: 10_000 });
	#reloadMe = debounce(() => this.loadMe(true), { wait: 1500, maxWait: 10_000 });
	#reloadGame = debounce(() => this.loadGameInstance(true), { wait: 1500, maxWait: 10_000 });
	#reloadGroups = debounce(() => this.loadGroupInstances(true), { wait: 1500, maxWait: 10_000 });

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

	isFriend(userId: string | undefined): boolean {
		if (!userId) return false;
		if (this.#ids.of !== this.friends) this.#ids = { of: this.friends, ids: new Set(this.friends.map((f) => f.userId)) };
		return this.#ids.ids.has(userId);
	}

	/**
	 * Loads everything for the signed-in account the first time, and again only when the account
	 * changes. Safe to call from every page and component that reads this; the live socket keeps it
	 * current afterwards.
	 */
	start(): void {
		const owner = vrchatAuth.ownerUserId;
		if (!owner || owner === this.#owner) return;
		this.#reset(owner);
		this.#followFriends(owner);
		void this.loadFavorites();
		void this.loadMe();
		void this.loadGameInstance();
		void this.loadGroupInstances();
	}

	/** The friend list, read once: what a page that only looks friends up waits for. */
	async ensureFriends(): Promise<Friend[]> {
		this.start();
		if (!this.friendsLoaded) await this.#friendsReady.promise;
		return this.friends;
	}

	/** What a live event changes: applied here when the event carries it, read again (debounced) otherwise. */
	apply(event: ServerEvent): SidebarPlan {
		const owner = vrchatAuth.ownerUserId;
		if (!owner || owner !== this.#owner) return {};
		const plan = planSidebarEvent(event, { owner, isFriend: (id) => this.isFriend(id), gameLocation: this.gameInstance.location });
		// Following the topic, the server sends the friend rows themselves: nothing to patch or read.
		if (plan.presence && !this.#viaTopic) {
			const at = (event.at ? timestampDate(event.at) : new Date()).toJSON();
			const next = patchFriendPresence(this.friends, plan.presence, at);
			if (next) this.friends = next;
		}
		if (plan.player) {
			const playerIds = new Set(this.gameInstance.playerIds);
			if (plan.player.joined) playerIds.add(plan.player.userId);
			else playerIds.delete(plan.player.userId);
			this.gameInstance = { ...this.gameInstance, playerIds };
		}
		if (plan.friends && !this.#viaTopic) this.#reloadFriends();
		if (plan.favorites) this.#reloadFavorites();
		if (plan.me) this.#reloadMe();
		if (plan.game) this.#reloadGame();
		if (plan.groups) this.#reloadGroups();
		return plan;
	}

	/** The live socket came back: whatever changed while it was down was never seen, so read it all once. */
	resync(): void {
		if (!vrchatAuth.ownerUserId || vrchatAuth.ownerUserId !== this.#owner) return;
		for (const held of [this.#reloadFriends, this.#reloadFavorites, this.#reloadMe, this.#reloadGame, this.#reloadGroups]) held.cancel();
		// The topic subscribes again on the new connection and its snapshot replaces the list.
		if (!this.#viaTopic) void this.loadFriends(true);
		void this.loadFavorites(true);
		void this.loadMe(true);
		void this.loadGameInstance(true);
		void this.loadGroupInstances(true);
	}

	/** Friend favorites were changed from this app (the favorites pages, a user's favorite button). */
	favoritesChanged(): void {
		this.#reloadFavorites();
	}

	#reset(owner: string) {
		this.#owner = owner;
		clearTimeout(this.#topicWait);
		this.#topic?.release();
		this.#topic = undefined;
		this.#viaTopic = false;
		this.#friendsReady.resolve();
		this.#friendsReady = SidebarData.#deferred();
		this.#running.clear();
		for (const held of [this.#reloadFriends, this.#reloadFavorites, this.#reloadMe, this.#reloadGame, this.#reloadGroups]) held.cancel();
		this.friends = [];
		this.groups = [];
		this.me = null;
		this.groupInstances = [];
		this.currentLocation = null;
		this.gameInstance = { location: null, playerIds: new Set() };
		this.friendsLoaded = false;
		this.error = '';
	}

	/**
	 * One read of a kind at a time: a second ask while one runs shares it, unless it must see
	 * something newer than that read (`fresh`: an event said it changed), which runs once more after
	 * it. What comes back for an account that is no longer signed in is dropped.
	 */
	#once(key: string, fresh: boolean, work: (owner: string, current: () => boolean) => Promise<void>): Promise<void> {
		const owner = vrchatAuth.ownerUserId;
		if (!owner) return Promise.resolve();
		// A read asked for before anything started (a dialog opened first) starts it all.
		this.start();
		const running = this.#running.get(key);
		if (running && !fresh) return running;
		const promise = (running ? running.then(() => undefined) : Promise.resolve())
			.then(() => work(owner, () => vrchatAuth.ownerUserId === owner && this.#owner === owner))
			.finally(() => {
				if (this.#running.get(key) === promise) this.#running.delete(key);
			});
		this.#running.set(key, promise);
		return promise;
	}

	/**
	 * Follows the account's friends topic. Its snapshot and deltas are the list from then on; a
	 * server without the topic, or no snapshot within FRIENDS_TOPIC_WAIT_MS, reads it over REST
	 * instead (a snapshot coming later still takes over).
	 */
	#followFriends(owner: string) {
		const topic = friendsTopic(owner);
		this.#topic = topic;
		let fellBack = false;
		const fallBack = () => {
			if (fellBack || this.#topic !== topic) return;
			fellBack = true;
			void this.loadFriends();
		};
		this.#topicWait = setTimeout(fallBack, FRIENDS_TOPIC_WAIT_MS);
		topic.listen((friends, status) => {
			if (this.#topic !== topic) return;
			if (status === 'live' && friends) {
				clearTimeout(this.#topicWait);
				this.#reloadFriends.cancel();
				this.#viaTopic = true;
				this.friends = friends;
				this.friendsLoaded = true;
				this.error = '';
				this.#friendsReady.resolve();
			} else if (status === 'unsupported' || status === 'closed') {
				clearTimeout(this.#topicWait);
				this.#viaTopic = false;
				fallBack();
			}
		});
	}

	loadFriends(fresh = false): Promise<void> {
		return this.#once('friends', fresh, async (owner, current) => {
			try {
				const friends = await api.get<Friend[]>(accountPath(owner, '/friends'));
				// The topic's copy is newer than any read that was under way when it came.
				if (!current() || this.#viaTopic) return;
				this.friends = friends;
				this.friendsLoaded = true;
				this.error = '';
			} catch (err) {
				if (current() && !this.#viaTopic) this.error = ApiError.from(err).message;
			} finally {
				if (current()) this.#friendsReady.resolve();
			}
		});
	}

	loadFavorites(fresh = false): Promise<void> {
		return this.#once('favorites', fresh, async (_owner, current) => {
			const groups = await loadFriendGroups().catch(() => undefined);
			if (groups && current()) this.groups = groups;
		});
	}

	loadMe(fresh = false): Promise<void> {
		return this.#once('me', fresh, async (owner, current) => {
			const me = await api.get<Me>(accountPath(owner, '/me')).catch(() => null);
			if (!me || !current()) return;
			this.me = me;
			this.currentLocation = me.currentLocation ?? null;
		});
	}

	loadGameInstance(fresh = false): Promise<void> {
		return this.#once('game', fresh, async (owner, current) => {
			const data = await api.get<{ location: string | null; playerIds: string[]; joinedAt?: string | null }>(accountPath(owner, '/game/instance')).catch(() => null);
			if (data && current()) this.gameInstance = { location: data.location, playerIds: new Set(data.playerIds), joinedAt: data.joinedAt ?? null };
		});
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

	loadGroupInstances(fresh = false): Promise<void> {
		return this.#once('groups', fresh, async (owner, current) => {
			const list = await api.get<GroupInstance[]>(accountPath(owner, '/group-instances')).catch(() => undefined);
			if (list && current()) this.groupInstances = list;
		});
	}

	/** The refresh button: read the friend list from VRChat again, then reload. */
	async refresh() {
		const account = vrchatAuth.ownerUserId;
		if (!account || this.refreshing) return;
		this.refreshing = true;
		try {
			await api.post(accountPath(account, '/friends/refresh'), {}).catch(() => {});
			// Following the topic, what the refresh changed comes as deltas.
			await Promise.all([this.#viaTopic ? Promise.resolve() : this.loadFriends(true), this.loadFavorites(true), this.loadMe(true)]);
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
