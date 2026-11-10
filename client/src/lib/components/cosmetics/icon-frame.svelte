<script lang="ts">
	import CosmeticImages from './cosmetic-images.svelte';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { cosmetics } from '#lib/settings/cosmetics.svelte.js';

	// The original's IconFrame: drawn over a round profile picture, 15% bigger on each side. Shown when
	// the VRChat Profile Cosmetics setting is on, or when `enabled` says so (the sidebar's own switch).
	let { iconFrame, enabled, class: className = '' }: { iconFrame?: string | null; enabled?: boolean; class?: string } = $props();

	const shown = $derived(enabled ?? appearance.displayVRCProfileCosmetics.value);
	const frame = $derived(shown ? cosmetics.get('iconFrame', iconFrame) : undefined);
</script>

{#if frame}
	<CosmeticImages cosmetic={frame} class={['pointer-events-none absolute top-[-15%] left-[-15%] h-[130%] w-[130%] max-w-none', className].join(' ')} />
{/if}
