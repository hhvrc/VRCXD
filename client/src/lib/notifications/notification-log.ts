import type { Notification } from '#lib/api/types.js';

// The Notifications page's rows from its two sources: the table as read over REST when the page
// opened, and the notifications topic's rows, which the server keeps current.

const ts = (n: Notification) => Date.parse(n.createdAt) || 0;

/**
 * The table with the topic's rows laid over what REST returned, newest first, at most `max`.
 *
 * The topic carries the account's newest notifications and everything since, so for the time it
 * covers it is the truth: its row replaces REST's, and a REST row from that time that the topic
 * does not have was taken off the page or answered since it was read, and is dropped. What is at
 * or before the topic's oldest row is older than the topic carries, and stays as REST read it.
 */
export function mergeNotificationLog(live: Notification[], rest: Notification[], max: number): Notification[] {
	if (live.length === 0) return rest.slice(0, max);
	const ids = new Set(live.map((n) => n.id));
	const floor = Math.min(...live.map(ts));
	// At the floor itself a row may be one the topic's cap cut off beside its oldest, so it is kept.
	const older = rest.filter((n) => !ids.has(n.id) && ts(n) <= floor);
	return [...live, ...older].sort((a, b) => ts(b) - ts(a)).slice(0, max);
}
