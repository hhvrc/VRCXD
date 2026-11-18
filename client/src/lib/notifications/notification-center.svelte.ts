import { accountPath, api } from '#lib/api/client.js';
import type { Notification } from '#lib/api/types.js';
import type { TopicHandle } from '#lib/api/topics.svelte.js';
import { notificationsTopic } from '#lib/live/topics/notifications.svelte.js';
import { isNotificationExpired, notificationCategory } from '#lib/notifications/notification-meta.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original notification store's unseen and last-24-hours lists, split into Friend, Group and
// Other, behind the sidebar bell.
//
// The rows come from the live socket's notifications topic: a snapshot and then the server's
// deltas. Nothing here reads them over REST; until the socket has delivered the snapshot the bell
// is empty.
const DAY = 24 * 60 * 60 * 1000;
const ts = (n: Notification) => Date.parse(n.createdAt) || 0;

class NotificationCenter {
	open = $state(false);
	/** What the bell shows: the topic's rows that are not hidden. */
	rows = $state.raw<Notification[]>([]);
	/** The topic's rows, hidden ones included, newest first: what the Notifications page lists. */
	allRows = $state.raw<Notification[]>([]);
	now = $state(Date.now());
	#pendingSee = new Set<string>();

	unseen = $derived(this.rows.filter((n) => !n.seen));
	hasUnseen = $derived(this.unseen.length > 0);

	unseenIn(category: 'friend' | 'group' | 'other') {
		return this.unseen.filter((n) => notificationCategory(n.type) === category).sort((a, b) => ts(b) - ts(a));
	}

	recentIn(category: 'friend' | 'group' | 'other') {
		const cutoff = this.now - DAY;
		return this.rows.filter((n) => n.seen && notificationCategory(n.type) === category && ts(n) > cutoff).sort((a, b) => ts(b) - ts(a));
	}

	#for = '';
	/** The notifications topic for #for, while held. */
	#topic: TopicHandle<Notification[]> | undefined;

	/** Follows the account's notifications topic, once per account; its snapshot and deltas are the rows from then on. */
	start() {
		const account = vrchatAuth.ownerUserId;
		if (!account || account === this.#for) return;
		this.#for = account;
		this.rows = [];
		this.allRows = [];
		this.#topic?.release();
		const topic = notificationsTopic(account);
		this.#topic = topic;
		topic.listen((notifications, status) => {
			if (this.#topic !== topic || status !== 'live' || !notifications) return;
			this.allRows = notifications;
			// The topic carries the hidden ones too, for the Notifications page; the bell does not show them.
			this.rows = notifications.filter((n) => !n.hidden);
			this.now = Date.now();
		});
	}

	/** Shown unseen items are marked seen when they leave the screen, as the original's items do on unmount. */
	shown(list: Notification[]) {
		for (const n of list) if (!n.seen && !isNotificationExpired(n, this.now)) this.#pendingSee.add(n.id);
	}

	async flushSeen() {
		const account = vrchatAuth.ownerUserId;
		const ids = [...this.#pendingSee];
		this.#pendingSee.clear();
		if (!account || !ids.length) return;
		// One at a time, as the original queues them to stay under VRChat's rate limit.
		for (const id of ids) await api.post(accountPath(account, `/notifications/${encodeURIComponent(id)}/see`), {}).catch(() => {});
		this.rows = this.rows.map((n) => (ids.includes(n.id) ? { ...n, seen: true } : n));
	}

	/** Asks the server to mark them all seen; the rows that were come back as a delta. */
	async markAllSeen() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		await api.post(accountPath(account, '/notifications/see-all'), {}).catch(() => {});
	}
}

export const notificationCenter = new NotificationCenter();
