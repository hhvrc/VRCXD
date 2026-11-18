import { accountPath, api, type PagedResult } from '#lib/api/client.js';
import type { Notification } from '#lib/api/types.js';
import type { TopicHandle } from '#lib/api/topics.svelte.js';
import { notificationsTopic } from '#lib/live/topics/notifications.svelte.js';
import { isNotificationExpired, notificationCategory } from '#lib/notifications/notification-meta.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original notification store's unseen and last-24-hours lists, split into Friend, Group and
// Other, behind the sidebar bell.
//
// The rows come from the live socket's notifications topic: a snapshot and then the server's
// deltas, so they are never read over REST. A server without that topic (older than it), or a
// socket that does not come up in time, gets them from GET notifications and keeps them current
// from the ServerEvents as before (load, and the reloads in sidebar-live.ts).
const DAY = 24 * 60 * 60 * 1000;
const ts = (n: Notification) => Date.parse(n.createdAt) || 0;

/** How long the bell waits for the topic's snapshot before reading the notifications over REST once. */
export const NOTIFICATIONS_TOPIC_WAIT_MS = 4000;

class NotificationCenter {
	open = $state(false);
	rows = $state.raw<Notification[]>([]);
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
	#viaTopic = false;
	#topicWait: ReturnType<typeof setTimeout> | undefined;

	/** The rows follow the topic: the events and reconnects need not read them. */
	get followsTopic() {
		return this.#viaTopic;
	}

	/** Follows the account's notifications once per account; the live socket keeps them current after. */
	start() {
		const account = vrchatAuth.ownerUserId;
		if (!account || account === this.#for) return;
		this.#for = account;
		this.rows = [];
		clearTimeout(this.#topicWait);
		this.#topic?.release();
		this.#viaTopic = false;
		this.#follow(account);
	}

	/**
	 * Follows the account's notifications topic. Its snapshot and deltas are the rows from then on;
	 * a server without the topic, or no snapshot within NOTIFICATIONS_TOPIC_WAIT_MS, reads them over
	 * REST instead (a snapshot coming later still takes over).
	 */
	#follow(account: string) {
		const topic = notificationsTopic(account);
		this.#topic = topic;
		let fellBack = false;
		const fallBack = () => {
			if (fellBack || this.#topic !== topic) return;
			fellBack = true;
			void this.load();
		};
		this.#topicWait = setTimeout(fallBack, NOTIFICATIONS_TOPIC_WAIT_MS);
		topic.listen((notifications, status) => {
			if (this.#topic !== topic) return;
			if (status === 'live' && notifications) {
				clearTimeout(this.#topicWait);
				this.#viaTopic = true;
				// The topic carries the hidden ones too, for the Notifications page; the bell does not show them.
				this.rows = notifications.filter((n) => !n.hidden);
				this.now = Date.now();
			} else if (status === 'unsupported' || status === 'closed') {
				clearTimeout(this.#topicWait);
				this.#viaTopic = false;
				fallBack();
			}
		});
	}

	/** Reads the rows over REST: the fallback, and what the events and reconnects ask for while there is no topic. */
	async load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const page = await api.get<PagedResult<Notification>>(accountPath(account, '/notifications'), { page: 1, pageSize: 500 }).catch(() => null);
		// The topic's copy is newer than any read that was under way when it came.
		if (page && account === vrchatAuth.ownerUserId && !this.#viaTopic) this.rows = page.items;
		this.now = Date.now();
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

	async markAllSeen() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		await api.post(accountPath(account, '/notifications/see-all'), {}).catch(() => {});
		// Following the topic, the rows that were seen come as a delta.
		if (!this.#viaTopic) await this.load();
	}
}

export const notificationCenter = new NotificationCenter();
