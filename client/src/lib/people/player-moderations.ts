import { accountPath, api } from '#lib/api/client.js';
import type { PlayerModeration } from '#lib/api/types.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// Your blocks and mutes, shared by the notifications (the original's cachedPlayerModerations, read
// again after a minute) and the user dialog, so opening a few profiles or a busy instance's joins
// don't each read the whole list. Changing one here forgets the copy, so the next read is fresh.
const MAX_AGE_MS = 60_000;

let cache: { owner: string; at: number; list: PlayerModeration[] } | undefined;
let loading: { owner: string; promise: Promise<PlayerModeration[]> } | undefined;

export async function playerModerations(maxAgeMs = MAX_AGE_MS): Promise<PlayerModeration[]> {
	const owner = vrchatAuth.ownerUserId;
	if (!owner) return [];
	if (cache?.owner === owner && Date.now() - cache.at < maxAgeMs) return cache.list;
	if (loading?.owner === owner) return loading.promise;
	const promise = api
		.get<PlayerModeration[]>(accountPath(owner, '/moderations'))
		.then((list) => {
			cache = { owner, at: Date.now(), list };
			return list;
		})
		.finally(() => {
			if (loading?.promise === promise) loading = undefined;
		});
	loading = { owner, promise };
	return promise;
}

/** A moderation was added or removed here: the next read asks the server. */
export function forgetPlayerModerations() {
	cache = undefined;
	loading = undefined;
}
