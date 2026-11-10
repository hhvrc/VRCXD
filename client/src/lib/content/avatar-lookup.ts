import { api } from '#lib/api/client.js';
import { userDialog } from '#lib/people/user-dialog.svelte.js';
import { ownPath, vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

export type AvatarLookup = { avatarId?: string | null; ownerId?: string | null; avatarName?: string | null };

export const lookupAvatar = (imageUrl: string, userId?: string) => api.get<AvatarLookup>(ownPath('/avatars/lookup'), { imageUrl, userId });

/**
 * What a lookup leads to: the avatar, else its owner unless that is the wearer, else a warning.
 * One of the account's own uploads the server could not find is the original's "Own avatar not found".
 */
export function avatarLookupOutcome(lookup: AvatarLookup, wearerId: string | undefined, ownId: string | undefined): { avatarId: string } | { ownerId: string } | { warning: string } {
	if (lookup.avatarId) return { avatarId: lookup.avatarId };
	if (lookup.ownerId && ownId && lookup.ownerId === ownId && wearerId !== ownId) return { warning: 'Own avatar not found' };
	if (lookup.ownerId && lookup.ownerId !== wearerId) return { ownerId: lookup.ownerId };
	return { warning: 'Avatar is private or not found' };
}

/**
 * The original's showAvatarAuthorDialog: opens the avatar a picture shows, or failing that its owner.
 * Returns what to tell the user when neither can be opened.
 */
export async function openAvatarFromImage(imageUrl: string, userId?: string): Promise<string | undefined> {
	const outcome = avatarLookupOutcome(await lookupAvatar(imageUrl, userId), userId, vrchatAuth.ownerUserId);
	if ('avatarId' in outcome) userDialog.openAvatar(outcome.avatarId);
	else if ('ownerId' in outcome) userDialog.open(outcome.ownerId);
	else return outcome.warning;
	return undefined;
}
