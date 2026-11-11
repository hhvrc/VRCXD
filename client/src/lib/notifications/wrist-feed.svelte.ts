import { accountPath, api, type PagedResult } from '#lib/api/client.js';
import { FEED_TYPES, type FeedEntry, type FeedType, type Notification } from '#lib/api/types.js';
import type { GameLogRow } from '#lib/activity/game-log-sessions.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
import { agentCan, desktop } from '#lib/instances/instance-launch.svelte.js';
import { m } from '#lib/paraglide/messages.js';
import { hour12Setting } from '#lib/activity/charts.js';
import { displayLocation, filterPasses, notificationMessage, notyMessage, notyName, type FilterValue, type Noty } from '#lib/notifications/noty.js';
import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
import { customTag } from '#lib/people/custom-tags.svelte.js';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

/** The original's sharedFeed maxEntries. */
export const WRIST_MAX_ROWS = 25;

export type WristRow = { time: string; name: string; text: string; friend: boolean; favorite: boolean; colour?: string; userId?: string };

/** The original's wristOverlay settings that only change what the browser sends. */
class WristFeedSettings {
	hidePrivateFromFeed = $state(readSetting('VRCX_hidePrivateFromFeed', false));
	vrBackgroundEnabled = $state(readSetting('VRCX_vrBackgroundEnabled', false));
	hideDevicesFromFeed = $state(readSetting('VRCX_hideDevicesFromFeed', false));
	/** The bottom bar without labels (VRCX_minimalFeed). */
	minimalFeed = $state(readSetting('VRCX_minimalFeed', true));
	vrOverlayCpuUsage = $state(readSetting('VRCX_vrOverlayCpuUsage', false));
	/** Hides how long the game has run (VRCX_hideUptimeFromFeed). */
	hideUptimeFromFeed = $state(readSetting('VRCX_hideUptimeFromFeed', false));
	pcUptimeOnFeed = $state(readSetting('VRCX_pcUptimeOnFeed', false));

	set(key: 'hidePrivateFromFeed' | 'vrBackgroundEnabled' | 'hideDevicesFromFeed' | 'minimalFeed' | 'vrOverlayCpuUsage' | 'hideUptimeFromFeed' | 'pcUptimeOnFeed', value: boolean) {
		this[key] = value;
		storeSetting(`VRCX_${key}`, value);
		void sendWristFeed();
	}
}

export const wristFeedSettings = new WristFeedSettings();

let rows: WristRow[] = [];
/** Friends on the way somewhere, pinned on top (the original's onPlayerJoining rows), by user id. */
const travellers = new Map<string, WristRow>();

/** The rows the wrist shows: the travellers first, newest first, then the feed. */
export function wristRows(): WristRow[] {
	const me = sidebarData.me?.id;
	// Tags read now, so a changed tag recolours rows already shown (the original's addTag).
	return [...[...travellers.values()].reverse(), ...rows]
		.slice(0, WRIST_MAX_ROWS)
		.map((r) => (r.userId && me ? { ...r, colour: customTag(me, r.userId)?.colour || undefined } : r));
}

/**
 * The original's rebuildOnPlayerJoining for one friend: on the way to the game's instance they read
 * "is joining", elsewhere "is traveling to" there; with the wrist's OnPlayerJoining filter off, or set
 * to VIP for someone not a favorite, nothing is pinned.
 */
export function wristTravelling(noty: Noty, toHere: boolean, destination: string, filter: FilterValue | undefined) {
	if (!filter || filter === 'Off' || (filter === 'VIP' && !noty.isFavorite) || !noty.userId) return;
	const row = wristRow(noty);
	if (!row) return;
	travellers.set(noty.userId, toHere ? row : { ...row, text: `is traveling to ${destination}` });
	void sendWristFeed();
}

/** A friend arrived somewhere or went offline: no longer travelling. */
export function wristArrived(userId: string | undefined) {
	if (userId && travellers.delete(userId)) void sendWristFeed();
}

/** The original's sharedFeed addEntry, for a row that already has isFriend and isFavorite. */
export function wristAccepts(noty: Noty, source: 'notification' | 'other', filter: Parameters<typeof filterPasses>[0], meId: string | undefined, hidePrivate: boolean): boolean {
	if (noty.userId && noty.userId === meId) return false;
	if (hidePrivate && noty.type === 'GPS' && noty.location === 'private') return false;
	if (noty.type === 'FriendRequest' || noty.type === 'Avatar') return false;
	return filterPasses(filter, noty, source);
}

/** One wrist row: the time, the name and what happened, as the minimal feed reads. */
export function wristRow(noty: Noty, locale?: string, hour12 = hour12Setting()): WristRow | null {
	// Your own instance changes read as just the place (Vr.vue's Location row).
	const msg =
		noty.type === 'Location'
			? { title: '', body: displayLocation(noty.location, noty.worldName, noty.groupName) }
			: notificationMessage(noty, notyMessage(noty), notyName(noty));
	if (!msg) return null;
	const time = new Date(noty.createdAt).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', hour12 });
	const tag = noty.userId && sidebarData.me ? customTag(sidebarData.me.id, noty.userId) : undefined;
	return { time, name: msg.title, text: msg.body, friend: noty.isFriend, favorite: noty.isFavorite, colour: tag?.colour || undefined, userId: noty.userId };
}

/** Adds a row to the top of the feed and pushes it to the PC's wrist overlay. */
export function addWristEntry(noty: Noty, source: 'notification' | 'other', filter: Parameters<typeof filterPasses>[0]) {
	if (!wristAccepts(noty, source, filter, sidebarData.me?.id, wristFeedSettings.hidePrivateFromFeed)) return;
	// The traveller pin stands for the joining row.
	if (noty.type === 'OnPlayerJoining') return;
	const row = wristRow(noty);
	if (!row) return;
	rows = [row, ...rows].slice(0, WRIST_MAX_ROWS);
	void sendWristFeed();
}

/** The game log's kinds the wrist can show, as the original's lookupGameLogDatabase. */
const GAME_LOG_TYPES = ['Location', 'OnPlayerJoined', 'OnPlayerLeft', 'PortalSpawn', 'VideoPlay', 'Event', 'External', 'AvatarChange'];

export function feedNoty(r: FeedEntry): Omit<Noty, 'isFriend' | 'isFavorite'> {
	return {
		type: r.type,
		createdAt: r.createdAt,
		userId: r.userId,
		displayName: r.displayName,
		location: r.location ?? undefined,
		worldName: r.worldName ?? undefined,
		groupName: r.groupName ?? undefined,
		status: r.status ?? undefined,
		statusDescription: r.statusDescription ?? undefined
	};
}

export function gameLogNoty(r: GameLogRow): Omit<Noty, 'isFriend' | 'isFavorite'> {
	const base = { type: r.type, createdAt: r.createdAt, userId: r.userId ?? undefined, displayName: r.displayName ?? undefined };
	switch (r.type) {
		case 'Location':
			return { ...base, location: r.location ?? undefined, worldName: r.worldName ?? undefined, groupName: r.groupName ?? undefined };
		case 'PortalSpawn':
			return { ...base, location: r.instanceId ?? undefined, worldName: r.worldName ?? undefined };
		case 'VideoPlay':
			return { ...base, notyName: r.videoName ? (r.displayName ? `${r.videoName} (${r.displayName})` : r.videoName) : (r.videoUrl ?? '') };
		case 'Event':
		case 'AvatarChange':
			return { ...base, data: r.data ?? '' };
		case 'External':
			return { ...base, message: r.message ?? '' };
		default:
			return base;
	}
}

export function notificationNoty(n: Notification): Omit<Noty, 'isFriend' | 'isFavorite'> {
	return { type: n.type, createdAt: n.createdAt, userId: n.senderUserId, senderUsername: n.senderDisplayName, displayName: n.senderDisplayName, title: n.title, message: n.message };
}

/**
 * The original's loadSharedFeed: the newest rows the wrist filters let through, from the feed, the
 * game log and the notifications, once friends and favorites are known.
 */
export function pickWristHistory(
	candidates: { noty: Omit<Noty, 'isFriend' | 'isFavorite'>; source: 'notification' | 'other' }[],
	filters: Record<string, FilterValue | undefined>,
	who: { meId?: string; isFriend: (id?: string) => boolean; isFavorite: (id?: string) => boolean },
	hidePrivate: boolean
): Noty[] {
	return candidates
		.map(({ noty, source }) => ({ noty: { ...noty, isFriend: who.isFriend(noty.userId), isFavorite: who.isFavorite(noty.userId) }, source }))
		.filter(({ noty, source }) => wristAccepts(noty, source, filters[noty.type], who.meId, hidePrivate))
		.map(({ noty }) => noty)
		.sort((a, b) => b.createdAt.localeCompare(a.createdAt))
		.slice(0, WRIST_MAX_ROWS);
}

export async function loadWristHistory(filters: Record<string, FilterValue | undefined>) {
	const account = sidebarData.me?.id;
	if (!account) return;
	const wanted = (type: string) => !!filters[type] && filters[type] !== 'Off';
	const feedTypes = FEED_TYPES.filter((t: FeedType) => wanted(t));
	const gameTypes = GAME_LOG_TYPES.filter(wanted);
	const [feed, game, notes] = await Promise.all([
		feedTypes.length ? api.get<FeedEntry[]>(accountPath(account, '/feed'), { types: feedTypes.join(','), limit: WRIST_MAX_ROWS * 4 }).catch(() => []) : [],
		gameTypes.length ? api.get<GameLogRow[]>(accountPath(account, '/game-log'), { types: gameTypes.join(','), limit: WRIST_MAX_ROWS * 4 }).catch(() => []) : [],
		api.get<PagedResult<Notification>>(accountPath(account, '/notifications'), { page: 1, pageSize: WRIST_MAX_ROWS * 2 }).then((p) => p.items).catch(() => [])
	]);
	const friendIds = new Set(sidebarData.friends.map((f) => f.userId));
	const history = pickWristHistory(
		[
			...feed.map((r) => ({ noty: feedNoty(r), source: 'other' as const })),
			...game.map((r) => ({ noty: gameLogNoty(r), source: 'other' as const })),
			...notes.filter((n) => wanted(n.type)).map((n) => ({ noty: notificationNoty(n), source: 'notification' as const }))
		],
		filters,
		{ meId: account, isFriend: (id) => !!id && friendIds.has(id), isFavorite: (id) => !!id && sidebarData.vipIds.has(id) },
		wristFeedSettings.hidePrivateFromFeed
	);
	// Live rows that came in while this loaded stay on top.
	const loaded = history.map((n) => wristRow(n)).filter((r): r is WristRow => !!r);
	rows = [...rows, ...loaded].slice(0, WRIST_MAX_ROWS);
	void sendWristFeed();
}

/** The bottom bar's player count with friends in brackets, as Vr.vue showed it. */
function status(): string {
	const players = sidebarData.gameInstance.playerIds;
	if (!sidebarData.gameInstance.location || !players.size) return '';
	const friends = sidebarData.friends.filter((f) => players.has(f.userId)).length;
	return friends ? `${players.size} (${friends})` : `${players.size}`;
}

const ms = (at: string | null | undefined) => (at ? Date.parse(at) || 0 : 0);

/** What the wrist's bottom bar needs from the page (the original's updateVRLastLocation and config). */
export function wristStatus() {
	return {
		status: status(),
		locationSinceMs: sidebarData.gameInstance.location ? ms(sidebarData.gameInstance.joinedAt) : 0,
		onlineSinceMs: wristFeedSettings.hideUptimeFromFeed ? 0 : ms(desktop.gameStartedAt),
		pcUptime: wristFeedSettings.pcUptimeOnFeed,
		cpuUsage: wristFeedSettings.vrOverlayCpuUsage,
		minimal: wristFeedSettings.minimalFeed,
		onlineFriends: sidebarData.onlineCount,
		timerLabel: m.vr_status_timer(),
		playersLabel: m.vr_status_players(),
		cpuLabel: m.vr_status_cpu(),
		onlineLabel: m.vr_status_online(),
		hour12: hour12Setting()
	};
}

/** The original's sendSharedFeed. */
export async function sendWristFeed() {
	if (!agentCan('openvr')) return;
	await api
		.post<void>(ownPath('/agent/wrist-feed'), {
			rows: wristRows(),
			background: wristFeedSettings.vrBackgroundEnabled,
			hideDevices: wristFeedSettings.hideDevicesFromFeed,
			...wristStatus()
		})
		.catch((err) => console.error('Failed to update the wrist overlay:', err));
}

let watching = false;

/** Sends the feed again when the bottom bar's numbers change: the instance, its players, the game and friends online. */
export function watchWristStatus(filters: () => Record<string, FilterValue | undefined>) {
	if (watching) return;
	watching = true;
	let timer: ReturnType<typeof setTimeout> | undefined;
	let last = '';
	let loaded = false;
	$effect.root(() => {
		// The history once friends are in, as the original waited for its friend and favorite lists.
		$effect(() => {
			if (loaded || !sidebarData.friends.length || !sidebarData.me) return;
			loaded = true;
			void loadWristHistory(filters());
		});
		$effect(() => {
			const key = JSON.stringify([wristStatus(), wristRows().map((r) => r.colour ?? '')]);
			if (key === last) return;
			last = key;
			clearTimeout(timer);
			timer = setTimeout(() => void sendWristFeed(), 500);
		});
	});
}
