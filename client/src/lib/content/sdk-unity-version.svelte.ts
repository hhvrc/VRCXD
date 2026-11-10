import { accountPath, api } from '#lib/api/client.js';
import { SvelteMap } from 'svelte/reactivity';

// The original's cachedConfig.sdkUnityVersion, read once per account. Undefined until it is known,
// so the Platform line does not filter before then; null when VRChat's config has none.
const versions = new SvelteMap<string, string | null>();
const asked = new Set<string>();

export function sdkUnityVersion(account: string | null | undefined): string | null | undefined {
	if (!account) return undefined;
	if (!asked.has(account)) {
		asked.add(account);
		api.get<{ sdkUnityVersion?: string | null }>(accountPath(account, '/sdk-unity-version')).then(
			(found) => versions.set(account, found?.sdkUnityVersion ?? null),
			// Asked again the next time a pop-up opens.
			() => asked.delete(account)
		);
	}
	return versions.get(account);
}
