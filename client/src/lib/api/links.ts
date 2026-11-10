import { getServerToken } from './client';

/**
 * Where to load a VRChat image from: this server's disk copy. Anything else, like a data or blob
 * URL or a site's favicon, is left as it is.
 */
export function imageSrc<T extends string | null | undefined>(url: T): T | string {
	if (!url || !/^https:\/\/([a-z0-9-]+\.)*vrchat\.(cloud|com)\//i.test(url)) return url;
	return `/1/images?url=${encodeURIComponent(url)}&token=${encodeURIComponent(getServerToken())}`;
}

/**
 * The original's convertFileUrlToImageUrl: a VRChat file or image URL as the image endpoint at the
 * given size, so a small icon doesn't load the full picture (upstream issue 1178). Anything else as is.
 */
export function vrchatImageAt(url: string | null | undefined, resolution = 128): string {
	if (!url) return '';
	const match = /image\/file_([a-f0-9-]+)\/(\d+)\/(\d+)\/?$/.exec(url) ?? /file\/file_([a-f0-9-]+)\/(\d+)(\/file)?\/?$/.exec(url);
	return match ? `https://api.vrchat.cloud/api/1/image/file_${match[1]}/${match[2]}/${resolution}` : url;
}

/** {@link imageSrc} for a small icon: the 128px rendition, as the original's userImage(user, true). */
export function iconSrc(url: string | null | undefined): string | null | undefined {
	return url ? imageSrc(vrchatImageAt(url, 128)) : url;
}

// A normal click on one is caught by user-dialog-host.svelte and opens the pop-up instead.
export const userHref = (userId: string) => `/user?id=${encodeURIComponent(userId)}`;
/** Takes a world id or a full instance location. */
export const worldHref = (location: string) => `/world?id=${encodeURIComponent(location)}`;
export const avatarHref = (avatarId: string) => `/avatar?id=${encodeURIComponent(avatarId)}`;
export const groupHref = (groupId: string) => `/group?id=${encodeURIComponent(groupId)}`;

/** The page for a user, world, avatar or group on the VRChat website, for sharing. */
export const vrchatUrl = (kind: 'user' | 'world' | 'avatar' | 'group', id: string) => `https://vrchat.com/home/${kind}/${id}`;

/** The link for any VRChat id, going by its prefix. */
export function entityHref(id: string): string | undefined {
	if (id.startsWith('usr_')) return userHref(id);
	if (id.startsWith('wrld_')) return worldHref(id);
	if (id.startsWith('avtr_')) return avatarHref(id);
	if (id.startsWith('grp_')) return groupHref(id);
	return undefined;
}
