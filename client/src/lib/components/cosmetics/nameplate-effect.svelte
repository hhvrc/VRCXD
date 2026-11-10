<script lang="ts">
	import CosmeticImages from './cosmetic-images.svelte';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { cosmetics } from '#lib/settings/cosmetics.svelte.js';

	// The original's NameplateEffect: a gradient strip under the profile header with the effect's
	// animation at its right end.
	let { nameplateEffect, class: className = '' }: { nameplateEffect?: string | null; class?: string } = $props();

	const effect = $derived(appearance.displayVRCProfileCosmetics.value ? cosmetics.get('nameplateEffect', nameplateEffect) : undefined);
	const gradient = $derived(effect?.gradientStart && effect.gradientEnd ? `linear-gradient(90deg, #${effect.gradientStart}, #${effect.gradientEnd})` : undefined);
</script>

{#if effect}
	<div class={['absolute top-[105px] right-0 h-[50px] w-full', className]}>
		<div class="absolute inset-0 rounded-b-lg" style:background-image={gradient}></div>
		<CosmeticImages cosmetic={effect} class="absolute top-0 right-0 h-full w-auto rounded-b-lg object-contain object-right" />
	</div>
{/if}
