// The world and avatar dialogs' cache row (the original's updateVRChatWorldCache and
// updateVRChatAvatarCache): whether VRChat on the PC has the bundle, its size, and deleting it.
import { formatFileSize } from '#lib/helpers/format.js';
import { toastError } from '#lib/helpers/feedback.js';
import { agentCan } from '#lib/instances/instance-launch.svelte.js';
import { openFolder } from '#lib/agent/agent.js';
import { checkCache, deleteCache, type CacheRef } from '#lib/agent/cache.js';

export class BundleCache {
	inCache = $state(false);
	size = $state('');
	locked = $state(false);
	path = $state('');
	#asked = 0;

	reset() {
		this.#asked++;
		this.inCache = false;
		this.size = '';
		this.locked = false;
		this.path = '';
	}

	async refresh(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined) {
		this.reset();
		const asked = this.#asked;
		if (!ref || !agentCan('cache')) return;
		try {
			const found = await checkCache(ref, sdkUnityVersion);
			if (asked !== this.#asked) return;
			if (found.size > 0) {
				this.inCache = true;
				this.size = formatFileSize(found.size);
				this.path = found.path;
			}
			this.locked = found.locked;
		} catch {
			// No answer from the PC: shown as not cached.
		}
	}

	/** runDeleteVRChatCacheFlow, then the row again. */
	async delete(ref: CacheRef | undefined, sdkUnityVersion: string | null | undefined) {
		try {
			await deleteCache(ref, sdkUnityVersion);
		} catch (err) {
			toastError(err);
		}
		await this.refresh(ref, sdkUnityVersion);
	}

	/** openFolderGeneric on the bundle's folder: "<asset id>/<version>" inside the cache folder. */
	open() {
		const parts = this.path.split(/[\/]/).filter(Boolean);
		openFolder('cache', parts.slice(-2).join('/')).catch(toastError);
	}
}
