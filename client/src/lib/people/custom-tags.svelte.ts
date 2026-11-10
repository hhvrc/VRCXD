import { SvelteMap } from 'svelte/reactivity';
import { accountPath, api } from '#lib/api/client.js';
import { onLiveEvent } from '#lib/api/live.svelte.js';
import { applyTagChange, tagsFromRows, type CustomTag, type CustomTagRow } from '#lib/people/custom-tags.js';

// The account's custom user tags: read once per account, then kept current by CustomTagChanged.
const byAccount = new SvelteMap<string, SvelteMap<string, CustomTag>>();
const loading = new Set<string>();

async function load(accountId: string) {
	if (loading.has(accountId)) return;
	loading.add(accountId);
	try {
		const rows = await api.get<CustomTagRow[]>(accountPath(accountId, '/custom-tags'));
		byAccount.set(accountId, new SvelteMap(tagsFromRows(rows)));
	} catch (err) {
		loading.delete(accountId);
		console.error('Failed to read custom tags:', err);
	}
}

/** The user's tag for this account, if a program gave one; loads the account's tags on first use. */
export function customTag(accountId: string, userId: string): CustomTag | undefined {
	const tags = byAccount.get(accountId);
	if (!tags) {
		void load(accountId);
		return undefined;
	}
	return tags.get(userId);
}

onLiveEvent((event) => {
	if (event.event.case !== 'customTag') return;
	const change = event.event.value;
	const tags = byAccount.get(change.accountId);
	// Not read yet: the first read will have it.
	if (tags) applyTagChange(tags, change);
});

/** The original's SendIpc ShowUserDialog: programs on the account's PC pipe learn which user is shown. */
export function announceUserDialog(accountId: string, userId: string) {
	void api.post(accountPath(accountId, '/desktop/ipc/user-dialog'), { userId }).catch(() => {});
}
