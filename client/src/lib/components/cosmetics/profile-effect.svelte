<script lang="ts">
	import CosmeticImages from './cosmetic-images.svelte';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { cosmetics } from '#lib/settings/cosmetics.svelte.js';

	// The original's ProfileEffect: an animation over the whole profile header. Shown when the VRChat
	// Profile Cosmetics setting is on, or when `enabled` says so (a view's own cosmetics switch).
	let { profileEffect, enabled, class: className = '' }: { profileEffect?: string | null; enabled?: boolean; class?: string } = $props();

	const shown = $derived(enabled ?? appearance.displayVRCProfileCosmetics.value);
	const effect = $derived(shown ? cosmetics.get('profileEffect', profileEffect) : undefined);
</script>

{#if effect}
	<CosmeticImages cosmetic={effect} class={['pointer-events-none absolute inset-0 block h-full w-full object-top', className].join(' ')} />
{/if}
