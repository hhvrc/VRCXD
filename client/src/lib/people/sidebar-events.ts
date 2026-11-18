import type { Friend, UserState } from '#lib/api/types.js';
import type { PresenceChanged, ServerEvent } from '#lib/gen/vrcx_live_pb.js';

// What one live event means for the shared friend state (sidebarData): most are applied as they
// come, like the original's friend store applies each pipeline event to its friend refs, and only
// what the event cannot carry is read again. The live stream carries every account's events, so
// anything about "you" is filtered to the signed-in account here.

export type SidebarPlan = {
	/** A friend's new presence, applied to the list as it is. */
	presence?: PresenceChanged;
	/** The friend list itself changed in a way the event doesn't carry (a new friend, a new status). */
	friends?: boolean;
	/** The friend favorite groups changed. */
	favorites?: boolean;
	/** Your own profile or where you are changed (GET /me, which asks VRChat). */
	me?: boolean;
	/** The game's own instance changed (GET /game/instance). */
	game?: boolean;
	/** Someone joined or left the game's instance, by its log: applied to the player list as it is. */
	player?: { userId: string; joined: boolean };
	/** The open instances in your groups. */
	groups?: boolean;
	/** Your notifications. */
	notifications?: boolean;
};

export type SidebarContext = {
	owner: string;
	isFriend: (userId: string) => boolean;
	/** Where the game log puts the account, if anywhere. */
	gameLocation: string | null;
};

/** The user profile fields the friend list shows; a change to only others (bio) is not read again. */
const LISTED_FIELDS = ['display_name', 'status', 'trust', 'avatar'];

export function planSidebarEvent(event: ServerEvent, ctx: SidebarContext): SidebarPlan {
	const e = event.event;
	switch (e.case) {
		case 'presence': {
			if (e.value.userId === ctx.owner) return { me: true };
			return ctx.isFriend(e.value.userId) ? { presence: e.value } : {};
		}
		case 'user': {
			const listed = e.value.changed.some((c) => LISTED_FIELDS.includes(c));
			if (e.value.userId === ctx.owner) return listed ? { me: true } : {};
			// The event names what changed, not the new values, so the list is read again.
			return listed && ctx.isFriend(e.value.userId) ? { friends: true } : {};
		}
		case 'friendship':
			return e.value.ownerUserId === ctx.owner ? { friends: true } : {};
		case 'visit': {
			const v = e.value;
			// Your own move changes where the game is and your current location.
			if (v.userId === ctx.owner) return { me: true, game: true };
			// Others only matter in the game's instance, and only by its log, as GET /game/instance counts them.
			if (v.source === 'game_log' && ctx.gameLocation && v.instanceLocation === ctx.gameLocation) return { player: { userId: v.userId, joined: v.joined } };
			return {};
		}
		case 'desktop':
			// The PC's agent or the game started or stopped: the game's instance (and your current
			// location, which follows it while the game runs) may have gone.
			return { me: true, game: true };
		case 'account':
			return e.value.accountId === ctx.owner ? { me: true } : {};
		case 'notification':
			return e.value.ownerUserId === ctx.owner ? { notifications: true } : {};
		case 'content':
			return e.value.kind === 'group' ? { groups: true } : {};
		case 'contentRefresh':
			// VRChat's content-refresh for the account's favorites, should it send one.
			return e.value.accountId === ctx.owner && e.value.contentType.toLowerCase().includes('favorite') ? { favorites: true } : {};
		default:
			return {};
	}
}

const STATES: readonly UserState[] = ['online', 'active', 'offline'];

/**
 * A friend's PresenceChanged applied to the list, the way GET /friends would show it afterwards:
 * state, location and travelling as they are now, the platform unless VRChat says offline or web
 * (the original's computeUserPlatform), and the location and online timers restarted when those
 * changed. Undefined when the user isn't in the list.
 */
export function patchFriendPresence(friends: Friend[], presence: PresenceChanged, at: string): Friend[] | undefined {
	const index = friends.findIndex((f) => f.userId === presence.userId);
	if (index === -1) return undefined;
	const before = friends[index];
	const state = STATES.includes(presence.state as UserState) ? (presence.state as UserState) : before.state;
	const location = presence.location || (state === 'online' ? before.location : 'offline');
	const platform = presence.platform && presence.platform !== 'offline' && presence.platform !== 'web' ? presence.platform : before.platform;
	// The name is left alone: a rename comes as its own UserChanged, and the notifications tell it
	// from the name the list still has.
	const after: Friend = {
		...before,
		state,
		location,
		travelingToLocation: presence.travelingToLocation || null,
		platform,
		locationSince: location !== before.location ? at : before.locationSince,
		onlineFor: state !== before.state ? at : before.onlineFor,
		// The server applies a held offline when it publishes it, so nothing is waiting any more.
		pendingOffline: false
	};
	const next = friends.slice();
	next[index] = after;
	return next;
}

/** The user fields the feed has rows for (its Status, Avatar and Bio types). */
const FEED_FIELDS = ['status', 'avatar', 'bio'];

/**
 * Whether a live event can have added to the feed: a friend's presence (GPS, Online, Offline), or a
 * friend's status, avatar or bio change. The feed only holds friends, and the stream carries every
 * user the server hears about.
 */
export function feedEventMatters(event: ServerEvent, isFriend: (userId: string) => boolean): boolean {
	const e = event.event;
	if (e.case === 'presence') return isFriend(e.value.userId);
	if (e.case === 'user') return e.value.changed.some((c) => FEED_FIELDS.includes(c)) && isFriend(e.value.userId);
	return false;
}

/**
 * Whether a live event can have added to the account's game log: a visit read from a game log (not
 * friends moving, which the pipeline reports for every friend), or a game log batch of this account.
 */
export function gameLogEventMatters(event: ServerEvent, owner: string | undefined): boolean {
	const e = event.event;
	if (e.case === 'visit') return e.value.source === 'game_log';
	if (e.case === 'gameLog') return !!owner && e.value.accountId === owner;
	return false;
}
