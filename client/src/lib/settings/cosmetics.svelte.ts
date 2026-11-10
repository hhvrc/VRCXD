import { SvelteMap } from 'svelte/reactivity';
import { accountPath, api } from '#lib/api/client.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original's getCosmetics: VRChat's icon frame, profile effect and nameplate effect templates,
// read once per account, so the ids on users can be drawn.
export type CosmeticKind = 'iconFrame' | 'profileEffect' | 'nameplateEffect';
export type CosmeticAsset = { type: string; url: string; totalDurationMs?: number };
export type Cosmetic = { id: string; name?: string; assets: CosmeticAsset[]; gradientStart?: string; gradientEnd?: string };

class Cosmetics {
	#templates = new SvelteMap<string, Cosmetic>();
	#loaded = new Set<string>();
	/** Whether the window has focus: the original only animates cosmetics then. */
	focused = $state(typeof document === 'undefined' ? true : document.hasFocus());
	/** The account's Reduced Profile Animations (VRChat's clientConfig accessReduceDecorAnim). */
	reduceAnimations = $state(false);
	#configFor = '';

	/** The original's isAnimated: focused, and the account has not asked for reduced animations. */
	get animated(): boolean {
		this.#loadConfig();
		return this.focused && !this.reduceAnimations;
	}

	#loadConfig() {
		const account = vrchatAuth.ownerUserId;
		if (!account || this.#configFor === account) return;
		this.#configFor = account;
		api.get<{ accessReduceDecorAnim?: boolean }>(accountPath(account, '/client-config')).then(
			(config) => (this.reduceAnimations = config.accessReduceDecorAnim === true),
			() => (this.#configFor = '')
		);
	}

	/** The original's toggleReducedProfileAnimations, after its confirm. */
	async setReduceAnimations(account: string, value: boolean) {
		const config = await api.put<{ accessReduceDecorAnim?: boolean }>(accountPath(account, '/client-config'), { accessReduceDecorAnim: value });
		this.reduceAnimations = config.accessReduceDecorAnim === true;
	}

	constructor() {
		if (typeof window === 'undefined') return;
		window.addEventListener('focus', () => (this.focused = true));
		window.addEventListener('blur', () => (this.focused = false));
	}

	/** The template for an id, loading that kind's index the first time it is asked for. */
	get(kind: CosmeticKind, id: string | null | undefined): Cosmetic | undefined {
		if (!id) return undefined;
		this.#load(kind);
		return this.#templates.get(id);
	}

	#load(kind: CosmeticKind) {
		const account = vrchatAuth.ownerUserId;
		const key = `${account}:${kind}`;
		if (!account || this.#loaded.has(key)) return;
		this.#loaded.add(key);
		api.get<Cosmetic[]>(accountPath(account, `/cosmetics/${kind}`)).then(
			(list) => list.forEach((item) => this.#templates.set(item.id, item)),
			() => this.#loaded.delete(key)
		);
	}
}

export const cosmetics = new Cosmetics();
