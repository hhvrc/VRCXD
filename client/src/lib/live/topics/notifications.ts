import { timestampDate, type Timestamp } from '@bufbuild/protobuf/wkt';
import type { Notification } from '#lib/api/types.js';
import type { NotificationEntry, TopicDelta, TopicSnapshot } from '#lib/gen/vrcx_live_pb.js';
import type { TopicCodec } from '#lib/api/topics.svelte.js';

// The "notifications" topic's payloads as the notifications the app already uses (GET
// notifications' shape with includeHidden), so the bell reads the same rows whichever way they
// came. The topic carries the account's newest notifications, hidden ones included; older ones are
// only on REST. Pure: notifications.svelte.ts is the store around it.

export const NOTIFICATIONS_TOPIC = 'notifications';

const text = (value: string) => (value === '' ? undefined : value);
const time = (value: Timestamp | undefined) => (value ? timestampDate(value).toISOString() : undefined);

/** JSON the server sends as text: what it left out (empty) or could not have been JSON is left out. */
function json<T>(value: string): T | undefined {
	if (value === '') return undefined;
	try {
		return JSON.parse(value) as T;
	} catch {
		return undefined;
	}
}

/** One row as GET notifications returns it: what the server leaves out (null) is left out here too. */
export function notificationFromEntry(e: NotificationEntry): Notification {
	return {
		id: e.id,
		version: e.version,
		type: e.type,
		senderUserId: text(e.senderUserId),
		senderDisplayName: text(e.senderDisplayName),
		title: text(e.title),
		message: e.message,
		imageUrl: text(e.imageUrl),
		link: text(e.link),
		linkText: text(e.linkText),
		details: json<Notification['details']>(e.detailsJson),
		responses: json<Notification['responses']>(e.responsesJson),
		seen: e.seen,
		hidden: e.hidden,
		createdAt: time(e.createdAt) ?? '',
		expiresAt: time(e.expiresAt)
	};
}

/** Newest first, then by id: the order GET notifications lists them in. */
function newestFirst(a: Notification, b: Notification): number {
	const byTime = (Date.parse(b.createdAt) || 0) - (Date.parse(a.createdAt) || 0);
	return byTime !== 0 ? byTime : a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/** The snapshot as the whole list, newest first as the server sends it. */
export function applyNotificationsSnapshot(payload: TopicSnapshot['payload']): Notification[] | undefined {
	if (payload.case !== 'notifications') return undefined;
	return payload.value.notifications.map(notificationFromEntry);
}

/**
 * The list after a delta: upserts replace a notification's row or add it, removals drop it, and
 * the list stays newest first. A new array whenever anything changed, the same one otherwise.
 */
export function applyNotificationsDelta(notifications: Notification[], payload: TopicDelta['payload']): Notification[] | undefined {
	if (payload.case !== 'notifications') return undefined;
	const { upserts, removedIds } = payload.value;
	if (!upserts.length && !removedIds.length) return notifications;
	const byId = new Map(notifications.map((n) => [n.id, n]));
	for (const id of removedIds) byId.delete(id);
	for (const entry of upserts) byId.set(entry.id, notificationFromEntry(entry));
	return [...byId.values()].sort(newestFirst);
}

export const notificationsCodec: TopicCodec<Notification[]> = { snapshot: applyNotificationsSnapshot, delta: applyNotificationsDelta };
