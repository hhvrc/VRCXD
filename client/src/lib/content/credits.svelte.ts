import { SvelteMap } from 'svelte/reactivity';
import { accountPath, api } from '#lib/api/client.js';
import { onLiveEvent } from '#lib/api/live.svelte.js';

// The original's currentUserCredits: the account's VRC Credits balance, read on demand
// (getVRChatCredits) and kept current by VRChat's economy-update.
export const credits = new SvelteMap<string, number | null>();

/** Asks VRChat for the account's balance again. */
export async function refreshCredits(accountId: string) {
	try {
		const result = await api.get<{ balance?: number | null }>(accountPath(accountId, '/economy/balance'));
		credits.set(accountId, result.balance ?? null);
	} catch (err) {
		console.error('Failed to read VRChat credits:', err);
	}
}

onLiveEvent((event) => {
	if (event.event.case !== 'credits') return;
	const { accountId, balance } = event.event.value;
	credits.set(accountId, balance === undefined ? null : Number(balance));
});
