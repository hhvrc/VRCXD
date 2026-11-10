import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

// The original's useRecentActions: when invites, invite requests and friend requests were last sent to
// someone, so the user menu can mark them with a clock for a while (Settings > Social > Recent Action
// Icon). Same storage names as the original.

export type RecentAction =
	| 'Send Friend Request'
	| 'Invite'
	| 'Invite Photo'
	| 'Invite Message'
	| 'Request Invite'
	| 'Request Invite Photo'
	| 'Request Invite Message';

class RecentActions {
	#actions = $state<Record<string, number>>(readSetting('VRCX_recentActions', {}));
	#enabled = $state(readSetting('VRCX_recentActionCooldownEnabled', false));
	#minutes = $state(readSetting('VRCX_recentActionCooldownMinutes', 60));
	/** Re-read once a minute so markers expire on screen. */
	#tick = $state(0);

	constructor() {
		if (typeof window !== 'undefined') setInterval(() => (this.#tick += 1), 60_000);
	}

	get enabled() {
		return this.#enabled;
	}
	set enabled(value: boolean) {
		this.#enabled = value;
		storeSetting('VRCX_recentActionCooldownEnabled', value);
	}

	get minutes() {
		return this.#minutes;
	}
	set minutes(value: number) {
		this.#minutes = Math.max(1, Math.min(1440, Math.round(value) || 1));
		storeSetting('VRCX_recentActionCooldownMinutes', this.#minutes);
	}

	record(userId: string, action: RecentAction) {
		this.#actions = { ...this.#actions, [`${userId}:${action}`]: Date.now() };
		storeSetting('VRCX_recentActions', this.#actions);
	}

	/** The original's isActionRecent. */
	isRecent(userId: string, ...actions: RecentAction[]): boolean {
		void this.#tick;
		if (!this.#enabled) return false;
		const cooldown = this.#minutes * 60_000;
		return actions.some((action) => {
			const at = this.#actions[`${userId}:${action}`];
			return !!at && Date.now() - at < cooldown;
		});
	}

	/**
	 * Records what a successful request to the server sent, so every place that invites or sends a
	 * request is covered: POST …/invites/{id}[/photo], …/invite-requests/{id}[/photo], …/friend-requests/{id}.
	 */
	noteRequest(method: string, path: string, body?: unknown) {
		if (method !== 'POST') return;
		const match = path.match(/\/(invites|invite-requests|friend-requests)\/([^/?]+)(\/photo)?(?:\?|$)/);
		if (!match) return;
		const userId = decodeURIComponent(match[2]);
		const photo = !!match[3];
		const message = !!body && typeof body === 'object' && (body as { messageSlot?: unknown }).messageSlot != null;
		if (match[1] === 'friend-requests') this.record(userId, 'Send Friend Request');
		else if (match[1] === 'invites') this.record(userId, photo ? 'Invite Photo' : message ? 'Invite Message' : 'Invite');
		else this.record(userId, photo ? 'Request Invite Photo' : message ? 'Request Invite Message' : 'Request Invite');
	}
}

export const recentActions = new RecentActions();
