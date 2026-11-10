import { m } from '#lib/paraglide/messages.js';
import { parseLocation } from '#lib/instances/location.js';

// The original's notification ("noty") rules: shared/constants/feedFilters.js (the filter options
// and defaults), shared/utils/notificationMessage.js (the wording) and the conditions in the
// notification store's playNoty.

export type FilterValue = 'Off' | 'On' | 'VIP' | 'Friends' | 'Everyone';
export type FilterOption = { key: string; name: string; options: FilterValue[]; tooltip?: string; tooltipWarning?: boolean };
export type SharedFeedFilters = { noty: Record<string, FilterValue>; wrist: Record<string, FilterValue> };

export const SHARED_FEED_FILTERS_KEY = 'sharedFeedFilters';
export const DESKTOP_TOAST_KEY = 'VRCX_desktopToast';
export const AFK_DESKTOP_TOAST_KEY = 'VRCX_afkDesktopToast';
export const NOTIFICATION_TTS_KEY = 'VRCX_notificationTTS';
export const NOTIFICATION_TTS_NICKNAME_KEY = 'VRCX_notificationTTSNickName';
export const NOTIFICATION_TTS_VOICE_KEY = 'VRCX_notificationTTSVoice';

export const FILTER_LABEL: Record<FilterValue, string> = { Off: 'Off', On: 'On', VIP: 'Favorites', Friends: 'Friends', Everyone: 'Everyone' };

const VFE: FilterValue[] = ['Off', 'VIP', 'Friends', 'Everyone'];
const VF: FilterValue[] = ['Off', 'VIP', 'Friends'];
const ON: FilterValue[] = ['Off', 'On'];

export const NOTY_FILTER_OPTIONS: FilterOption[] = [
	{ key: 'OnPlayerJoining', name: 'OnPlayerJoining', options: VF },
	{ key: 'OnPlayerJoined', name: 'OnPlayerJoined', options: VFE },
	{ key: 'OnPlayerLeft', name: 'OnPlayerLeft', options: VFE },
	{ key: 'Online', get name() { return m.view_feed_filters_Online(); }, options: VF },
	{ key: 'Offline', get name() { return m.location_offline(); }, options: VF },
	{ key: 'GPS', get name() { return m.view_feed_filters_GPS(); }, options: VF },
	{ key: 'Status', get name() { return m.table_import_status(); }, options: VF },
	{ key: 'invite', get name() { return m.dialog_launch_invite(); }, options: VF },
	{ key: 'requestInvite', get name() { return m.dialog_user_actions_request_invite(); }, options: VF },
	{ key: 'inviteResponse', get name() { return m.view_notification_filters_inviteResponse(); }, options: VF },
	{ key: 'requestInviteResponse', get name() { return m.view_notification_filters_requestInviteResponse(); }, options: VF },
	{ key: 'boop', get name() { return m.dialog_boop_dialog_header(); }, options: VF },
	{ key: 'friendRequest', get name() { return m.view_notification_filters_friendRequest(); }, options: ON },
	{ key: 'Friend', name: 'New Friend', options: ON },
	{ key: 'Unfriend', get name() { return m.table_friendList_unfriend(); }, options: ON },
	{ key: 'DisplayName', name: 'Display Name Change', options: VF },
	{ key: 'TrustLevel', name: 'Trust Level Change', options: VF },
	{ key: 'groupChange', get name() { return m.view_notification_filters_groupChange(); }, options: ON, tooltip: "When you've left or been kicked from a group, group name changed, group owner changed, role added/removed" },
	{ key: 'group.announcement', get name() { return m.notifications_group_announcement_title(); }, options: ON },
	{ key: 'group.event.created', get name() { return m.notifications_group_event_created_title(); }, options: ON },
	{ key: 'group.event.starting', get name() { return m.notifications_group_event_starting_title(); }, options: ON },
	{ key: 'group.informative', name: 'Group Join', options: ON, tooltip: 'When your request to join a group has been approved' },
	{ key: 'group.invite', get name() { return m.notifications_group_invite_title(); }, options: ON, tooltip: 'When someone invites you to join a group' },
	{ key: 'group.joinRequest', get name() { return m.notifications_group_join_request_title(); }, options: ON, tooltip: "When someone requests to join a group you're a moderator for" },
	{ key: 'group.transfer', get name() { return m.notifications_group_transfer_request_title(); }, options: ON },
	{ key: 'group.queueReady', get name() { return m.notifications_group_queue_ready_title(); }, options: ON },
	{ key: 'instance.closed', get name() { return m.notifications_instance_closed_title(); }, options: ON, tooltip: "When the instance you're in has been closed preventing anyone from joining" },
	{ key: 'VideoPlay', name: 'Video Play', options: ON, tooltip: 'Requires VRCXD YouTube API option enabled', tooltipWarning: true },
	{
		key: 'Event',
		name: 'Miscellaneous Events',
		options: ON,
		tooltip:
			'Misc event from VRC game log: VRC crash auto rejoin, shader keyword limit, joining instance blocked by master, error loading video, audio device changed, error joining instance, kicked from instance, VRChat failing to start OSC server, etc...'
	},
	{ key: 'External', name: 'External App', options: ON },
	{ key: 'BlockedOnPlayerJoined', name: 'Blocked Player Joins', options: VFE },
	{ key: 'BlockedOnPlayerLeft', name: 'Blocked Player Leaves', options: VFE },
	{ key: 'MutedOnPlayerJoined', name: 'Muted Player Joins', options: VFE },
	{ key: 'MutedOnPlayerLeft', name: 'Muted Player Leaves', options: VFE },
	{ key: 'AvatarChange', name: 'Lobby Avatar Change', options: VFE }
];

export const WRIST_FILTER_OPTIONS: FilterOption[] = [{ key: 'Location', name: 'Self Location', options: ON }, ...NOTY_FILTER_OPTIONS];

const COMMON = {
	'group.announcement': 'On',
	'group.event.created': 'On',
	'group.event.starting': 'On',
	'group.informative': 'On',
	'group.invite': 'On',
	'group.transfer': 'On',
	'group.queueReady': 'On',
	'instance.closed': 'On',
	PortalSpawn: 'Everyone',
	Event: 'On',
	External: 'On',
	BlockedOnPlayerJoined: 'Off',
	BlockedOnPlayerLeft: 'Off',
	MutedOnPlayerJoined: 'Off',
	MutedOnPlayerLeft: 'Off',
	ChatBoxMessage: 'Off',
	friendRequest: 'On',
	Friend: 'On',
	Unfriend: 'On',
	groupChange: 'On',
	invite: 'Friends',
	requestInvite: 'Friends',
	inviteResponse: 'Friends',
	requestInviteResponse: 'Friends',
	boop: 'Friends'
} as const satisfies Record<string, FilterValue>;

export function defaultSharedFeedFilters(): SharedFeedFilters {
	return {
		noty: {
			...COMMON,
			Location: 'Off',
			OnPlayerJoined: 'VIP',
			OnPlayerLeft: 'VIP',
			OnPlayerJoining: 'VIP',
			Online: 'VIP',
			Offline: 'VIP',
			GPS: 'Off',
			Status: 'Off',
			DisplayName: 'VIP',
			TrustLevel: 'VIP',
			'group.joinRequest': 'Off',
			VideoPlay: 'Off',
			AvatarChange: 'Off',
			Blocked: 'Off',
			Unblocked: 'Off',
			Muted: 'Off',
			Unmuted: 'Off'
		},
		wrist: {
			...COMMON,
			Location: 'On',
			OnPlayerJoined: 'Everyone',
			OnPlayerLeft: 'Everyone',
			OnPlayerJoining: 'Friends',
			Online: 'Friends',
			Offline: 'Friends',
			GPS: 'Friends',
			Status: 'Friends',
			DisplayName: 'Friends',
			TrustLevel: 'Friends',
			'group.joinRequest': 'On',
			VideoPlay: 'On',
			AvatarChange: 'Everyone',
			Blocked: 'On',
			Unblocked: 'On',
			Muted: 'On',
			Unmuted: 'On'
		}
	};
}

/** Saved filters with the original's upgrades: missing keys take their default, and a boop of On becomes Friends. */
export function normalizeSharedFeedFilters(saved: unknown): SharedFeedFilters {
	const defaults = defaultSharedFeedFilters();
	const value = (saved && typeof saved === 'object' ? saved : {}) as Partial<SharedFeedFilters>;
	const noty = { ...defaults.noty, ...(value.noty ?? {}) };
	const wrist = { ...defaults.wrist, ...(value.wrist ?? {}) };
	if (noty.boop === 'On') noty.boop = 'Friends';
	if (wrist.boop === 'On') wrist.boop = 'Friends';
	return { noty, wrist };
}

/**
 * Whether a filter value lets a notification through. Off never does; On and Everyone always do;
 * VIP needs a favorite friend. Friends needs a friend, except in the original's notification and
 * friend log queues, which let Friends through for anyone.
 */
export function filterPasses(value: FilterValue | undefined, noty: { isFriend: boolean; isFavorite: boolean }, source: 'notification' | 'other'): boolean {
	if (!value || value === 'Off') return false;
	if (value === 'On' || value === 'Everyone') return true;
	if (value === 'Friends') return source === 'notification' || noty.isFriend;
	return noty.isFavorite;
}

export type Condition = 'Never' | 'Desktop Mode' | 'Inside VR' | 'Outside VR' | 'Game Running' | 'Game Closed' | 'Always';
export const DESKTOP_CONDITIONS: { value: Condition; label: string }[] = [
	{ value: 'Never', get label() { return m.dialog_group_event_edit_ends_values_never(); } },
	{ value: 'Desktop Mode', get label() { return m.view_settings_notifications_notifications_conditions_desktop(); } },
	{ value: 'Inside VR', get label() { return m.view_settings_notifications_notifications_conditions_inside_vr(); } },
	{ value: 'Outside VR', get label() { return m.view_settings_notifications_notifications_conditions_outside_vr(); } },
	{ value: 'Game Running', get label() { return m.view_settings_notifications_notifications_conditions_inside_vrchat(); } },
	{ value: 'Game Closed', get label() { return m.view_settings_notifications_notifications_conditions_outside_vrchat(); } },
	{ value: 'Always', get label() { return m.view_settings_notifications_notifications_conditions_always(); } }
];
export const TTS_CONDITIONS = DESKTOP_CONDITIONS.filter((c) => c.value !== 'Desktop Mode' && c.value !== 'Outside VR');

export type GameState = { gameRunning: boolean; steamVrRunning: boolean; noVr: boolean; hmdAfk: boolean };

export function conditionMet(condition: string, game: GameState): boolean {
	switch (condition) {
		case 'Always':
			return true;
		case 'Inside VR':
			return game.steamVrRunning;
		case 'Outside VR':
			return !game.steamVrRunning;
		case 'Game Closed':
			return !game.gameRunning;
		case 'Game Running':
			return game.gameRunning;
		case 'Desktop Mode':
			return game.noVr && game.gameRunning;
		default:
			return false;
	}
}

/** The original's AFK rule: a desktop notification anyway while the headset is off in VR. */
export const afkMet = (enabled: boolean, game: GameState) => enabled && game.hmdAfk && game.gameRunning && !game.noVr;

export type Noty = {
	type: string;
	createdAt: string;
	displayName?: string;
	previousDisplayName?: string;
	senderUsername?: string;
	userId?: string;
	location?: string;
	worldName?: string;
	groupName?: string;
	status?: string;
	statusDescription?: string;
	trustLevel?: string;
	title?: string;
	message?: string;
	details?: { worldId?: string; worldName?: string; inviteMessage?: string; requestMessage?: string; responseMessage?: string };
	data?: string;
	notyName?: string;
	imageUrl?: string;
	isFriend: boolean;
	isFavorite: boolean;
};

/** The original's displayLocation. */
export function displayLocation(location: string | undefined, worldName: string | undefined, groupName = ''): string {
	const L = parseLocation(location ?? '');
	if (L.isOffline) return 'Offline';
	if (L.isPrivate) return 'Private';
	if (L.isTraveling) return 'Traveling';
	const name = worldName ?? '';
	if (L.worldId) {
		if (groupName) return `${name} ${L.accessTypeName}(${groupName})`;
		if (L.instanceId) return `${name} ${L.accessTypeName}`;
	}
	return name;
}

/** The name a notification is about, as playNoty picks it. */
export function notyName(noty: Noty): string {
	if (noty.type === 'DisplayName' && noty.previousDisplayName) return noty.previousDisplayName;
	return noty.displayName || noty.senderUsername || '';
}

/** The invite, request or response text playNoty adds (", <text>"), or the title and message. */
export function notyMessage(noty: Noty): string {
	let message = noty.title ? `${noty.title}, ${noty.message ?? ''}` : (noty.message ?? '');
	for (const key of ['inviteMessage', 'requestMessage', 'responseMessage'] as const) {
		if (noty.details?.[key] !== undefined) message = `, ${noty.details[key]}`;
	}
	return message;
}

const fill = (text: string, values: Record<string, string | undefined>) => text.replace(/\{(\w+)\}/g, (_, k: string) => values[k] ?? '');

/** The original's getNotificationMessage. */
export function notificationMessage(noty: Noty, message: string, nameOverride?: string): { title: string; body: string } | null {
	const name = nameOverride || noty.displayName || '';
	const sender = nameOverride || noty.senderUsername || '';
	const at = () => displayLocation(noty.location, noty.worldName, noty.groupName);
	switch (noty.type) {
		case 'OnPlayerJoined':
			return { title: name, body: 'has joined' };
		case 'OnPlayerLeft':
			return { title: name, body: 'has left' };
		case 'OnPlayerJoining':
			return { title: name, body: 'is joining' };
		case 'GPS':
			return { title: name, body: fill('is in {location}', { location: at() }) };
		case 'Online':
			return noty.worldName ? { title: name, body: fill('has logged in to {location}', { location: at() }) } : { title: name, body: 'has logged in' };
		case 'Offline':
			return { title: name, body: 'has logged out' };
		case 'Status':
			return { title: name, body: fill('status is now {status} {description}', { status: noty.status, description: noty.statusDescription }) };
		case 'invite':
			return { title: sender, body: fill('has invited you to {location} {message}', { location: displayLocation(noty.details?.worldId, noty.details?.worldName), message }) };
		case 'requestInvite':
			return { title: sender, body: fill('has requested an invite {message}', { message }) };
		case 'inviteResponse':
			return { title: sender, body: fill('has responded to your invite {message}', { message }) };
		case 'requestInviteResponse':
			return { title: sender, body: fill('has responded to your invite request {message}', { message }) };
		case 'friendRequest':
			return { title: sender, body: 'has sent you a friend request' };
		case 'Friend':
			return { title: name, body: 'is now your friend' };
		case 'Unfriend':
			return { title: name, body: 'is no longer your friend' };
		case 'TrustLevel':
			return { title: name, body: fill('Trust level is now {trustLevel}', { trustLevel: noty.trustLevel }) };
		case 'DisplayName':
			return { title: nameOverride || noty.previousDisplayName || '', body: fill('changed their name to {displayName}', { displayName: noty.displayName }) };
		case 'boop':
		case 'groupChange':
			return { title: sender, body: noty.message ?? '' };
		case 'group.announcement':
			return { title: noty.title || 'Group Announcement', body: noty.message ?? '' };
		case 'group.event.created':
			return { title: noty.title || 'Group Event Created', body: noty.message ?? '' };
		case 'group.event.starting':
			return { title: noty.title || 'Group Event Starting', body: noty.message ?? '' };
		case 'group.informative':
			return { title: noty.title || 'Group Informative', body: noty.message ?? '' };
		case 'group.invite':
			return { title: noty.title || 'Group Invite', body: noty.message ?? '' };
		case 'group.joinRequest':
			return { title: noty.title || 'Group Join Request', body: noty.message ?? '' };
		case 'group.transfer':
			return { title: noty.title || 'Group Transfer Request', body: noty.message ?? '' };
		case 'group.queueReady':
			return { title: noty.title || 'Instance Queue Ready', body: noty.message ?? '' };
		case 'instance.closed':
			return { title: noty.title || 'Instance Closed', body: noty.message ?? '' };
		case 'Event':
			return { get title() { return m.view_game_log_filters_Event(); }, body: noty.data ?? '' };
		case 'External':
			return { get title() { return m.view_game_log_filters_External(); }, body: noty.message ?? '' };
		case 'VideoPlay':
			return { title: 'Now playing', body: noty.notyName ?? '' };
		case 'PortalSpawn':
			return name ? { title: name, body: `has spawned a portal to ${displayLocation(noty.location, noty.worldName, noty.groupName)}` } : { title: '', body: 'User has spawned a portal' };
		case 'AvatarChange':
			return { title: name, body: `changed into avatar ${noty.data ?? ''}` };
		case 'BlockedOnPlayerJoined':
			return { title: name, body: 'Blocked user has joined' };
		case 'BlockedOnPlayerLeft':
			return { title: name, body: 'Blocked user has left' };
		case 'MutedOnPlayerJoined':
			return { title: name, body: 'Muted user has joined' };
		case 'MutedOnPlayerLeft':
			return { title: name, body: 'Muted user has left' };
		default:
			return null;
	}
}

const BODY_ONLY = new Set(['boop', 'group.announcement', 'group.informative', 'group.invite', 'group.joinRequest', 'group.transfer', 'group.queueReady', 'instance.closed', 'Event', 'External']);
const COLON = new Set(['groupChange', 'VideoPlay']);
const CUSTOM: Record<string, (title: string) => string> = {
	BlockedOnPlayerJoined: (t) => `Blocked user ${t} has joined`,
	BlockedOnPlayerLeft: (t) => `Blocked user ${t} has left`,
	MutedOnPlayerJoined: (t) => `Muted user ${t} has joined`,
	MutedOnPlayerLeft: (t) => `Muted user ${t} has left`
};

/** The original's toNotificationText: one line for text-to-speech. */
export function notificationText(title: string, body: string, type: string): string {
	if (BODY_ONLY.has(type)) return body;
	if (COLON.has(type)) return title ? `${title}: ${body}` : body;
	const custom = CUSTOM[type];
	if (custom) return custom(title);
	return title ? `${title} ${body}` : body;
}

/**
 * playNoty's once-only and freshness rules: the same type and name plays once per creation time,
 * and nothing older than a minute plays. Returns whether to play, updating the seen map.
 */
export function shouldPlay(noty: Noty, seen: Map<string, string>, now = Date.now()): boolean {
	const name = notyName(noty);
	if (name) {
		const id = `${noty.type},${name}`;
		const last = seen.get(id);
		if (last && last >= noty.createdAt) return false;
		seen.set(id, noty.createdAt);
	}
	const bias = new Date(now - 60_000).toJSON();
	for (const [id, createdAt] of seen) if (createdAt < bias) seen.delete(id);
	return noty.createdAt >= bias;
}
