import { browser } from '$app/env';

// Per-browser settings in localStorage, JSON-encoded under the original's config key names.

export function readSetting<T>(key: string, fallback: T): T {
	if (!browser) return fallback;
	try {
		const raw = localStorage.getItem(key);
		return raw === null ? fallback : (JSON.parse(raw) as T);
	} catch {
		return fallback;
	}
}

export function storeSetting(key: string, value: unknown) {
	if (!browser) return;
	try {
		localStorage.setItem(key, JSON.stringify(value));
	} catch {
		// Private window or blocked storage: the choice just lasts until reload.
	}
}

/** A setting kept as a plain string, as the original's string config keys are. */
export function readText(key: string, fallback = ''): string {
	if (!browser) return fallback;
	try {
		return localStorage.getItem(key) ?? fallback;
	} catch {
		return fallback;
	}
}

/** Stores a plain string; an empty one removes the key. */
export function storeText(key: string, value: string) {
	if (!browser) return;
	try {
		if (value) localStorage.setItem(key, value);
		else localStorage.removeItem(key);
	} catch {
		// Private window or blocked storage: the value just lasts until reload.
	}
}
