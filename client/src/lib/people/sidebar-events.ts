import type { ServerEvent } from '#lib/gen/vrcx_live_pb.js';

// What one live event means for the shared sidebar state (sidebarData) that has no topic yet: the
// account's own profile, the game's instance and its players, the favorite groups and the group
// instances. The friend list and the notifications follow their topics and are not planned here.
// The live stream carries every account's events, so anything about "you" is filtered to the
// signed-in account here.

export type SidebarPlan = {
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
};

export type SidebarContext = {
	owner: string;
	/** Where the game log puts the account, if anywhere. */
	gameLocation: string | null;
};

/** The profile fields the sidebar shows of the account itself; a change to only others (bio) is not read again. */
const LISTED_FIELDS = ['display_name', 'status', 'trust', 'avatar'];

export function planSidebarEvent(event: ServerEvent, ctx: SidebarContext): SidebarPlan {
	const e = event.event;
	switch (e.case) {
		case 'presence':
			return e.value.userId === ctx.owner ? { me: true } : {};
		case 'user':
			return e.value.userId === ctx.owner && e.value.changed.some((c) => LISTED_FIELDS.includes(c)) ? { me: true } : {};
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
		case 'content':
			return e.value.kind === 'group' ? { groups: true } : {};
		case 'contentRefresh':
			// VRChat's content-refresh for the account's favorites, should it send one.
			return e.value.accountId === ctx.owner && e.value.contentType.toLowerCase().includes('favorite') ? { favorites: true } : {};
		default:
			return {};
	}
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
