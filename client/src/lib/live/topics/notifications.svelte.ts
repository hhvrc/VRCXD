import type { Notification } from '#lib/api/types.js';
import { subscribeTopic, type TopicHandle } from '#lib/api/topics.svelte.js';
import { NOTIFICATIONS_TOPIC, notificationsCodec } from '#lib/live/topics/notifications.js';

/** The account's newest notifications as the server keeps them, hidden ones included: shared, so every holder reads one subscription. Release when done. */
export function notificationsTopic(account: string): TopicHandle<Notification[]> {
	return subscribeTopic(NOTIFICATIONS_TOPIC, account, notificationsCodec);
}
