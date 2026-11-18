import type { Friend } from '#lib/api/types.js';
import { subscribeTopic, type TopicHandle } from '#lib/api/topics.svelte.js';
import { FRIENDS_TOPIC, friendsCodec } from '#lib/live/topics/friends.js';

/** The account's friend list as the server keeps it: shared, so every holder reads one subscription. Release when done. */
export function friendsTopic(account: string): TopicHandle<Friend[]> {
	return subscribeTopic(FRIENDS_TOPIC, account, friendsCodec);
}
