import { SvelteMap } from 'svelte/reactivity';
import { api } from '#lib/api/client.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// World, group and user names for anything that only has an id, like the original's getWorldName and
// getGroupName. Each id is asked for once, and a name the server has never seen is fetched through it.
class NameCache {
	#names = new SvelteMap<string, string>();
	#asked = new Map<string, Promise<void>>();

	constructor(
		private readonly path: string,
		private readonly field = 'name'
	) {}

	get(id: string | null | undefined): string | undefined {
		if (!id) return undefined;
		const known = this.#names.get(id);
		if (known === undefined) void this.#ask(id);
		return known;
	}

	/** The name once it is known, for code that needs it now (a notification's text): asked for once, like get. */
	async load(id: string | null | undefined): Promise<string | undefined> {
		if (!id) return undefined;
		const known = this.#names.get(id);
		if (known !== undefined) return known;
		await this.#ask(id);
		return this.#names.get(id);
	}

	#ask(id: string): Promise<void> {
		let asked = this.#asked.get(id);
		if (!asked) {
			asked = this.#fetch(id);
			this.#asked.set(id, asked);
		}
		return asked;
	}

	async #fetch(id: string) {
		const url = `/1/${this.path}/${encodeURIComponent(id)}`;
		try {
			const found = await api.get<Record<string, string>>(url).catch(() => api.get<Record<string, string>>(url, { refresh: true, account: vrchatAuth.ownerUserId }));
			if (found[this.field]) this.#names.set(id, found[this.field]);
		} catch {
			// Unknown to VRChat too. Whoever asked keeps showing the id.
		}
	}
}

export const worldNames = new NameCache('worlds');
export const groupNames = new NameCache('groups');
export const userNames = new NameCache('users', 'displayName');
