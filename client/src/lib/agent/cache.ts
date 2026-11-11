// The original's cacheCoordinator (checkVRChatCache, deleteVRChatCache) and AssetBundleManager
// calls: which of a world's or avatar's bundles VRChat would have cached, asked of the PC's agent.
import { api } from '#lib/api/client.js';
import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
import { compareUnityVersion } from '#lib/details.js';

export type CachePackage = { platform?: string; unitySortNumber?: number | string; variant?: string; assetUrl?: string };
export type CacheRef = { unityPackages?: CachePackage[]; assetUrl?: string };
export type CacheEntry = { id: string; version: number; variant: string; variantVersion: number };
export type CacheState = { size: number; locked: boolean; path: string };

/** extractFileId. */
export const fileIdOf = (s: string | undefined): string => /file_[0-9A-Za-z-]+/.exec(String(s))?.[0] ?? '';

/** extractFileVersion. */
export const fileVersionOf = (s: string | undefined): string => /(?:\/file_[0-9A-Za-z-]+\/)([0-9]+)/i.exec(String(s))?.[1] ?? '';

/** extractVariantVersion: the ?v= of the asset URL, else '0'. */
export function variantVersionOf(url: string | undefined): string {
	if (!url) return '0';
	try {
		return new URL(url).searchParams.get('v') || '0';
	} catch {
		return '0';
	}
}

const variantOf = (p: CachePackage) => (!p.variant || p.variant === 'standard' ? 'security' : p.variant);

function entry(assetUrl: string, variant: string): CacheEntry | null {
	const id = fileIdOf(assetUrl);
	const version = parseInt(fileVersionOf(assetUrl), 10);
	if (!id || !version) return null;
	return { id, version, variant, variantVersion: parseInt(variantVersionOf(assetUrl), 10) || 0 };
}

/**
 * checkVRChatCache's pick: the newest Windows package the SDK's Unity opens, standard or security
 * only, falling back to the ref's own assetUrl; null when there is nothing to look for.
 */
export function entryToCheck(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined): CacheEntry | null {
	if (!ref?.unityPackages) return null;
	let assetUrl = '';
	let variant = '';
	for (let i = ref.unityPackages.length - 1; i > -1; i--) {
		const p = ref.unityPackages[i];
		if (p.variant && p.variant !== 'security') continue;
		if (p.platform === 'standalonewindows' && compareUnityVersion(p.unitySortNumber, sdkUnityVersion)) {
			assetUrl = p.assetUrl ?? '';
			variant = variantOf(p);
			break;
		}
	}
	return entry(assetUrl || ref.assetUrl || '', variant);
}

/** deleteVRChatCache's pick: as above but standard packages count too, and no fallback. */
export function entryToDelete(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined): CacheEntry | null {
	for (let i = (ref?.unityPackages?.length ?? 0) - 1; i > -1; i--) {
		const p = ref!.unityPackages![i];
		if (p.variant && p.variant !== 'standard' && p.variant !== 'security') continue;
		if (p.platform === 'standalonewindows' && compareUnityVersion(p.unitySortNumber, sdkUnityVersion)) return entry(p.assetUrl ?? '', variantOf(p));
	}
	return null;
}

/** CheckVRChatCache: the bundle's size (-1 when not cached), its lock and folder. */
export async function checkCache(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined): Promise<CacheState> {
	const found = entryToCheck(ref, sdkUnityVersion);
	if (!found) return { size: -1, locked: false, path: '' };
	return api.post<CacheState>(ownPath('/agent/cache/check'), found);
}

export async function deleteCache(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined): Promise<void> {
	const found = entryToDelete(ref, sdkUnityVersion);
	if (found) await api.post<void>(ownPath('/agent/cache/delete'), found);
}

/** GetCacheSize, in bytes. */
export const cacheInfo = (): Promise<{ size: number; location: string }> => api.get(ownPath('/agent/cache'));
export const clearCache = (): Promise<void> => api.post<void>(ownPath('/agent/cache/clear'), { confirm: true });
export const sweepCache = (): Promise<string[]> => api.post<string[]>(ownPath('/agent/cache/sweep'));
