import { browser } from '$app/env';
import { timestampDate } from '@bufbuild/protobuf/wkt';
import { accountPath, api, type PagedResult } from '#lib/api/client.js';
import type { Notification } from '#lib/api/types.js';
import type { GameLogRow } from '#lib/activity/game-log-sessions.js';
import type { ServerEvent } from '#lib/gen/vrcx_live_pb.js';
import { desktop } from '#lib/instances/instance-launch.svelte.js';
import { onLiveEvent } from '#lib/api/live.svelte.js';
import { parseLocation } from '#lib/instances/location.js';
import { AFK_DESKTOP_TOAST_KEY, afkMet, conditionMet, DESKTOP_TOAST_KEY, filterPasses, normalizeSharedFeedFilters, NOTIFICATION_TTS_KEY, NOTIFICATION_TTS_NICKNAME_KEY, NOTIFICATION_TTS_VOICE_KEY, notificationMessage, notificationText, notyMessage, notyName, SHARED_FEED_FILTERS_KEY, shouldPlay, type GameState, type Noty, type SharedFeedFilters } from '#lib/notifications/noty.js';
import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original's notification store playNoty: desktop notifications (the browser's Notification API
// in place of the Windows toast) and text-to-speech (speechSynthesis), for live events that pass the
// Notification Filters and the When-to-show conditions. One tab plays them: the first to take the
// "vrcx-noty" lock, so several open tabs don't all speak.

const readString = (key: string, fallback: string) => {
	const value = readSetting<unknown>(key, fallback);
	return typeof value === 'string' ? value : fallback;
};

class NotySettings {
	desktopToast = $state(readString(DESKTOP_TOAST_KEY, 'Never'));
	afkDesktopToast = $state(readSetting(AFK_DESKTOP_TOAST_KEY, false));
	notificationTTS = $state(readString(NOTIFICATION_TTS_KEY, 'Never'));
	notificationTTSNickName = $state(readSetting(NOTIFICATION_TTS_NICKNAME_KEY, false));
	/** The voice's index in speechSynthesis.getVoices(), stored as a string as the original does. */
	ttsVoice = $state(Number(readString(NOTIFICATION_TTS_VOICE_KEY, '0')) || 0);
	filters = $state<SharedFeedFilters>(normalizeSharedFeedFilters(readSharedFeedFilters()));
	voices = $state.raw<SpeechSynthesisVoice[]>([]);

	setDesktopToast(value: string) {
		this.desktopToast = value;
		storeSetting(DESKTOP_TOAST_KEY, value);
		// The browser asks once; without permission nothing shows.
		if (value !== 'Never' && typeof Notification !== 'undefined' && Notification.permission === 'default') void Notification.requestPermission();
	}

	setAfkDesktopToast(value: boolean) {
		this.afkDesktopToast = value;
		storeSetting(AFK_DESKTOP_TOAST_KEY, value);
		if (value && typeof Notification !== 'undefined' && Notification.permission === 'default') void Notification.requestPermission();
	}

	setNotificationTTS(value: string) {
		if (!browser) return;
		speechSynthesis.cancel();
		if (this.notificationTTS === 'Never' && value !== 'Never') this.speak('Notification text-to-speech enabled');
		this.notificationTTS = value;
		storeSetting(NOTIFICATION_TTS_KEY, value);
	}

	setNickName(value: boolean) {
		this.notificationTTSNickName = value;
		storeSetting(NOTIFICATION_TTS_NICKNAME_KEY, value);
	}

	/** The original's changeTTSVoice: saved, then the voice says its own name. */
	setVoice(index: number) {
		this.ttsVoice = index;
		storeSetting(NOTIFICATION_TTS_VOICE_KEY, String(index));
		const voice = this.voices[index];
		if (!voice) return;
		speechSynthesis.cancel();
		this.speak(voice.name);
	}

	saveFilters() {
		storeSetting(SHARED_FEED_FILTERS_KEY, JSON.stringify(this.filters));
	}

	updateVoices() {
		if (browser && 'speechSynthesis' in window) this.voices = speechSynthesis.getVoices();
	}

	speak(text: string) {
		if (!browser || !('speechSynthesis' in window)) return;
		const voices = speechSynthesis.getVoices();
		if (voices.length === 0) return;
		const tts = new SpeechSynthesisUtterance(text);
		tts.voice = voices[this.ttsVoice < voices.length ? this.ttsVoice : 0];
		speechSynthesis.speak(tts);
	}
}

/** The original keeps the filters as a JSON string under sharedFeedFilters; an object is read too. */
function readSharedFeedFilters(): unknown {
	const raw = readSetting<unknown>(SHARED_FEED_FILTERS_KEY, null);
	if (typeof raw !== 'string') return raw;
	try {
		return JSON.parse(raw);
	} catch {
		return null;
	}
}

export const notySettings = new NotySettings();

/** What the desktop tells about the game: running, and desktop mode from its log. It does not report SteamVR or the headset yet, so those read as off. */
const gameState = (): GameState => ({ gameRunning: desktop.gameRunning, steamVrRunning: false, noVr: desktop.gameNoVr, hmdAfk: false });

const seen = new Map<string, string>();

async function nickname(userId: string | undefined): Promise<string> {
	if (!userId) return '';
	const memo = await api.get<{ memo: string }>(`/1/users/${encodeURIComponent(userId)}/memo`).catch(() => null);
	return memo?.memo ? memo.memo.split('\n')[0] : '';
}

async function userImage(userId: string | undefined): Promise<string> {
	if (!userId || userId.startsWith('grp_')) return '';
	const friend = sidebarData.friends.find((f) => f.userId === userId);
	if (friend?.thumbnailImageUrl) return friend.thumbnailImageUrl;
	const user = await api.get<{ thumbnailImageUrl?: string | null }>(`/1/users/${encodeURIComponent(userId)}`).catch(() => null);
	return user?.thumbnailImageUrl ?? '';
}

/** playNoty: busy and stale checks, then the desktop notification and the speech its conditions allow. */
export async function playNoty(noty: Noty, options: { force?: boolean } = {}) {
	if (!options.force) {
		if (sidebarData.me?.status === 'busy' || !sidebarData.friends.length) return;
		if (!shouldPlay(noty, seen)) return;
	}
	const game = gameState();
	const speak = conditionMet(notySettings.notificationTTS, game);
	const toast = conditionMet(notySettings.desktopToast, game) || afkMet(notySettings.afkDesktopToast, game);
	const message = notyMessage(noty);
	if (speak) {
		const nick = notySettings.notificationTTSNickName ? await nickname(noty.userId) : '';
		const msg = notificationMessage(noty, message, nick || notyName(noty));
		if (msg) notySettings.speak(notificationText(msg.title, msg.body, noty.type));
	}
	if (toast && typeof Notification !== 'undefined' && Notification.permission === 'granted') {
		const msg = notificationMessage(noty, message);
		if (!msg) return;
		const icon = noty.imageUrl || (await userImage(noty.userId));
		new Notification(msg.title || 'VRCXD', { body: msg.body, icon: icon || undefined, tag: `${noty.type},${notyName(noty)}` });
	}
}

/** The original's Send Test Notification. */
export function testNotification() {
	void playNoty({ type: 'Event', createdAt: new Date().toJSON(), data: 'Test notification.', isFriend: false, isFavorite: false }, { force: true });
}

const isFriend = (userId: string | undefined) => !!userId && sidebarData.friends.some((f) => f.userId === userId);
const isFavorite = (userId: string | undefined) => !!userId && sidebarData.vipIds.has(userId);

function queue(noty: Omit<Noty, 'isFriend' | 'isFavorite'>, source: 'notification' | 'other') {
	const full: Noty = { ...noty, isFriend: isFriend(noty.userId), isFavorite: isFavorite(noty.userId) };
	if (!filterPasses(notySettings.filters.noty[full.type], full, source)) return;
	void playNoty(full);
}

async function worldName(location: string | undefined): Promise<string> {
	const worldId = parseLocation(location ?? '').worldId;
	if (!worldId) return '';
	const world = await api.get<{ name?: string }>(`/1/worlds/${encodeURIComponent(worldId)}`).catch(() => null);
	return world?.name ?? '';
}

async function notificationDetails(account: string, id: string): Promise<Noty['details'] & { imageUrl?: string }> {
	const page = await api.get<PagedResult<Notification>>(accountPath(account, '/notifications'), { page: 1, pageSize: 25 }).catch(() => null);
	const row = page?.items.find((n) => n.id === id);
	let details = row?.details;
	if (typeof details === 'string') {
		try {
			details = JSON.parse(details) as Record<string, unknown>;
		} catch {
			details = null;
		}
	}
	return { ...((details as Noty['details']) ?? {}), imageUrl: row?.imageUrl };
}

// When the account itself moves, the joins and leaves of that switch are not news (the original's
// 30 seconds after arriving and 5 seconds after leaving).
let ownJoinedAt = 0;
let ownLeftAt = 0;
const lastGameLog = new Map<string, string>();

async function onEvent(event: ServerEvent) {
	const account = vrchatAuth.ownerUserId;
	if (!account) return;
	const createdAt = (event.at ? timestampDate(event.at) : new Date()).toJSON();
	const e = event.event;
	switch (e.case) {
		case 'presence': {
			const p = e.value;
			if (!isFriend(p.userId)) return;
			if (p.kind === 'online') {
				const name = p.location ? await worldName(p.location) : '';
				queue({ type: 'Online', createdAt, userId: p.userId, displayName: p.displayName, location: p.location, worldName: name || undefined }, 'other');
			} else if (p.kind === 'offline') queue({ type: 'Offline', createdAt, userId: p.userId, displayName: p.displayName }, 'other');
			else if (p.kind === 'location' && p.location === 'traveling') {
				// The original's onPlayerTraveling: a friend on the way to the instance the game is in.
				const here = sidebarData.gameInstance;
				if (desktop.gameRunning && p.travelingToLocation && here.location === p.travelingToLocation && p.userId !== account && !here.playerIds.has(p.userId))
					queue({ type: 'OnPlayerJoining', createdAt, userId: p.userId, displayName: p.displayName }, 'other');
			} else if (p.kind === 'location' && p.location && p.location !== 'traveling') {
				queue({ type: 'GPS', createdAt, userId: p.userId, displayName: p.displayName, location: p.location, worldName: await worldName(p.location) }, 'other');
			}
			return;
		}
		case 'user': {
			const u = e.value;
			if (!isFriend(u.userId)) return;
			const before = sidebarData.friends.find((f) => f.userId === u.userId);
			if (u.changed.includes('display_name') && before && before.displayName !== u.displayName) {
				queue({ type: 'DisplayName', createdAt, userId: u.userId, displayName: u.displayName, previousDisplayName: before.displayName }, 'other');
			}
			if (!u.changed.includes('status') && !u.changed.includes('trust')) return;
			const user = await api.get<{ status: string; statusDescription?: string | null; trustLevel: string }>(`/1/users/${encodeURIComponent(u.userId)}`).catch(() => null);
			if (!user) return;
			if (u.changed.includes('status')) queue({ type: 'Status', createdAt, userId: u.userId, displayName: u.displayName, status: user.status, statusDescription: user.statusDescription ?? '' }, 'other');
			if (u.changed.includes('trust')) queue({ type: 'TrustLevel', createdAt, userId: u.userId, displayName: u.displayName, trustLevel: user.trustLevel }, 'other');
			return;
		}
		case 'friendship': {
			const f = e.value;
			if (f.ownerUserId !== account) return;
			queue({ type: f.isFriend ? 'Friend' : 'Unfriend', createdAt, userId: f.friendUserId, displayName: f.displayName }, 'notification');
			return;
		}
		case 'notification': {
			const n = e.value;
			if (n.ownerUserId !== account) return;
			const needsDetails = ['invite', 'requestInvite', 'inviteResponse', 'requestInviteResponse'].includes(n.type);
			const details = needsDetails ? await notificationDetails(account, n.notificationId) : undefined;
			queue(
				{
					type: n.type,
					createdAt,
					userId: n.senderUserId || undefined,
					senderUsername: n.senderDisplayName,
					displayName: n.senderDisplayName,
					title: n.title || undefined,
					message: n.message,
					details,
					imageUrl: details?.imageUrl
				},
				'notification'
			);
			return;
		}
		case 'visit': {
			const v = e.value;
			if (v.source !== 'game_log') return;
			const now = Date.parse(createdAt);
			if (v.userId === account) {
				if (v.joined) ownJoinedAt = now;
				else ownLeftAt = now;
				return;
			}
			if (v.joined && now <= ownJoinedAt + 30_000) return;
			if (!v.joined && now <= ownLeftAt + 5_000) return;
			queue({ type: v.joined ? 'OnPlayerJoined' : 'OnPlayerLeft', createdAt, userId: v.userId || undefined, displayName: v.displayName }, 'other');
			return;
		}
		case 'gameLog': {
			const g = e.value;
			if (g.accountId !== account) return;
			const types = [
				g.kinds.includes('event') && 'Event',
				g.kinds.includes('video') && 'VideoPlay',
				g.kinds.includes('portal') && 'PortalSpawn',
				g.kinds.includes('avatar') && 'AvatarChange',
				// A program on the PC pipe; external_quiet ones asked for no notification.
				g.kinds.includes('external') && 'External'
			].filter(Boolean) as string[];
			for (const type of types) {
				const [row] = await api.get<GameLogRow[]>(accountPath(account, '/game-log'), { types: type, limit: 1 }).catch(() => [] as GameLogRow[]);
				if (!row || lastGameLog.get(type) === row.key) continue;
				lastGameLog.set(type, row.key);
				if (type === 'Event') queue({ type, createdAt: row.createdAt, data: row.data ?? '' }, 'other');
				else if (type === 'External') queue({ type, createdAt: row.createdAt, userId: row.userId ?? undefined, displayName: row.displayName ?? undefined, message: row.message ?? '' }, 'other');
				else if (type === 'PortalSpawn') queue({ type, createdAt: row.createdAt, userId: row.userId ?? undefined, displayName: row.displayName ?? undefined, location: row.instanceId ?? undefined, worldName: row.worldName ?? undefined }, 'other');
				else if (type === 'AvatarChange') queue({ type, createdAt: row.createdAt, userId: row.userId ?? undefined, displayName: row.displayName ?? undefined, data: row.data ?? '' }, 'other');
				else if (row.videoName) {
					queue({ type, createdAt: row.createdAt, notyName: row.displayName ? `${row.videoName} (${row.displayName})` : row.videoName, displayName: row.displayName ?? undefined }, 'other');
				}
			}
			return;
		}
	}
}

let started = false;

/** Starts playing notifications in this tab once it holds the lock; called once from the layout. */
export function startNoty() {
	if (!browser || started) return;
	started = true;
	notySettings.updateVoices();
	if ('speechSynthesis' in window) speechSynthesis.addEventListener('voiceschanged', () => notySettings.updateVoices());
	const run = () => {
		onLiveEvent((event) => void onEvent(event));
		return new Promise<void>(() => {});
	};
	if (navigator.locks) void navigator.locks.request('vrcx-noty', run);
	else void run();
}
