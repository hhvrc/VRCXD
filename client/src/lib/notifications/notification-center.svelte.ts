import { accountPath, api, type PagedResult } from '#lib/api/client.js';
import type { Notification } from '#lib/api/types.js';
import { isNotificationExpired, notificationCategory } from '#lib/notifications/notification-meta.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original notification store's unseen and last-24-hours lists, split into Friend, Group and
// Other, behind the sidebar bell.
const DAY = 24 * 60 * 60 * 1000;
const ts = (n: Notification) => Date.parse(n.createdAt) || 0;

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

	async load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const page = await api.get<PagedResult<Notification>>(accountPath(account, '/notifications'), { page: 1, pageSize: 500 }).catch(() => null);
		if (page) this.rows = page.items;
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
		await this.load();
	}
}

export const notificationCenter = new NotificationCenter();
