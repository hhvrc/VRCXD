import { timestampDate, type Timestamp } from '@bufbuild/protobuf/wkt';
import type { Friend, UserState } from '#lib/api/types.js';
import type { FriendEntry, TopicDelta, TopicSnapshot } from '#lib/gen/vrcx_live_pb.js';
import type { TopicCodec } from '#lib/api/topics.svelte.js';

// The "friends" topic's payloads as the friend list the app already uses (GET friends' shape), so
// the sidebar, Friends Locations and the rest read the same Friend rows whichever way they came.
// Pure: friends.svelte.ts is the store around it.

export const FRIENDS_TOPIC = 'friends';

const STATES: readonly UserState[] = ['online', 'active', 'offline'];

const text = (value: string) => (value === '' ? undefined : value);
const time = (value: Timestamp | undefined) => (value ? timestampDate(value).toISOString() : undefined);

/** One row as GET friends returns it: what the server leaves out (null) is left out here too. */
export function friendFromEntry(e: FriendEntry): Friend {
	return {
		friendNumber: e.friendNumber,
		friendsSince: time(e.friendsSince) ?? '',
		userId: e.userId,
		displayName: e.displayName,
		trustLevel: e.trustLevel,
		state: STATES.includes(e.state as UserState) ? (e.state as UserState) : 'offline',
		status: e.status,
		statusDescription: text(e.statusDescription),
		location: text(e.location),
		travelingToLocation: text(e.travelingToLocation),
		locationSince: time(e.locationSince),
		onlineFor: time(e.onlineFor),
		lastActivity: time(e.lastActivity),
		lastLogin: time(e.lastLogin),
		thumbnailImageUrl: text(e.thumbnailImageUrl),
		tags: [...e.tags],
		developerType: e.developerType,
		platform: text(e.platform),
		nickname: text(e.nickname),
		pendingOffline: e.pendingOffline,
		nameHue: e.nameHue,
		iconFrame: text(e.iconFrame),
		profileEffect: text(e.profileEffect)
	};
}

/** The snapshot as the whole list, in friend-number order as the server sends it. */
export function applyFriendsSnapshot(payload: TopicSnapshot['payload']): Friend[] | undefined {
	if (payload.case !== 'friends') return undefined;
	return payload.value.friends.map(friendFromEntry);
}

/**
 * The list after a delta: upserts replace a friend's row or add it, removals drop it, and the list
 * stays in friend-number order. A new array whenever anything changed, the same one otherwise.
 */
export function applyFriendsDelta(friends: Friend[], payload: TopicDelta['payload']): Friend[] | undefined {
	if (payload.case !== 'friends') return undefined;
	const { upserts, removedUserIds } = payload.value;
	if (!upserts.length && !removedUserIds.length) return friends;
	const byId = new Map(friends.map((f) => [f.userId, f]));
	for (const id of removedUserIds) byId.delete(id);
	for (const entry of upserts) byId.set(entry.userId, friendFromEntry(entry));
	return [...byId.values()].sort((a, b) => a.friendNumber - b.friendNumber);
}

export const friendsCodec: TopicCodec<Friend[]> = { snapshot: applyFriendsSnapshot, delta: applyFriendsDelta };
