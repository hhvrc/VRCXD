import { accountPath, api } from '#lib/api/client.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

// The friends feature's REST requests (server/API/Controller/Friends), in one place so pages and
// components do not build its paths themselves (architecture.test.ts). The friend list itself is
// not here: it comes from the friends topic through sidebarData.

/** The Friend Log's path under the account, for the log table that reads and pages it. */
export const FRIEND_LOG_PATH = '/friend-log';

const user = (userId: string) => encodeURIComponent(userId);

/** Every current friend with what the Friend List page shows and searches. */
export function loadFriendDetails<T>(account: string): Promise<T> {
	return api.get<T>(accountPath(account, '/friends/details'));
}

/** Fetch a friend's full user object and profile from VRChat now: the Friend List's load missing entries. */
export function loadFriendProfile(userId: string): Promise<unknown> {
	return api.post(ownPath(`/friends/${user(userId)}/load`));
}

export function unfriend(userId: string): Promise<unknown> {
	return api.delete(ownPath(`/friends/${user(userId)}`));
}

export function sendFriendRequest(userId: string): Promise<unknown> {
	return api.post(ownPath(`/friend-requests/${user(userId)}`));
}

export function cancelFriendRequest(userId: string): Promise<unknown> {
	return api.delete(ownPath(`/friend-requests/${user(userId)}`));
}

export function acceptFriendRequest(userId: string): Promise<unknown> {
	return api.post(ownPath(`/friend-requests/${user(userId)}/accept`));
}

export function declineFriendRequest(userId: string): Promise<unknown> {
	return api.post(ownPath(`/friend-requests/${user(userId)}/decline`));
}

/** Remove a row from the Friend Log view; the history behind it stays on the server. */
export function hideFriendLogEntry(key: string): Promise<unknown> {
	return api.delete(ownPath(`${FRIEND_LOG_PATH}/${encodeURIComponent(key)}`));
}
