import { goto } from '$app/navigation';
import { api } from '#lib/api/client.js';
import { userDialog } from '#lib/people/user-dialog.svelte.js';
import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

/**
 * The original's lookupUser, for rows that only have a display name: opens the user with exactly that
 * name (a friend, a stored user, or VRChat's exact-name search), or else Search with the name filled in
 * (upstream issues 110 and 519).
 */
export async function lookupUser(ref: { userId?: string | null; displayName?: string | null }): Promise<void> {
	if (ref.userId) {
		userDialog.open(ref.userId);
		return;
	}
	const name = ref.displayName ?? '';
	if (!name || name.startsWith('ID:') || !vrchatAuth.ownerUserId) return;
	const found = await api.get<{ userId?: string | null }>(ownPath('/users/lookup'), { displayName: name }).catch(() => null);
	if (found?.userId) userDialog.open(found.userId);
	else await goto(`/search?user=${encodeURIComponent(name)}`);
}
