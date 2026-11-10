<script lang="ts" module>
	export type StoreListing = { id: string; displayName?: string; description?: string; imageUrl?: string; imageId?: string; priceTokens?: number; duration?: number; durationType?: string };
</script>

<script lang="ts">
	import type { Snippet } from 'svelte';
	import { PERSON_NAME, PRICE, LISTING_IMAGE, SUBTEXT_MUTED, LISTING_ROW } from '#lib/details.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import { replaceBioSymbols } from '#lib/helpers/format.js';

	// A store's listings with price and duration, as the original's world and group store tabs
	// show them. Each tab keeps how its picture opens and how it writes the price.
	let {
		listings,
		loading,
		imageOf,
		price,
		image
	}: {
		listings: StoreListing[];
		loading: boolean;
		imageOf: (listing: StoreListing) => string;
		price: (tokens: number | undefined) => string;
		image: Snippet<[string]>;
	} = $props();
</script>

{#if listings.length}
	<div class="min-h-0 flex-1 overflow-y-auto">
		{#each listings as listing (listing.id)}
			{@const src = imageOf(listing)}
			<div class={LISTING_ROW}>
				<div class={LISTING_IMAGE}>
					{#if src}{@render image(src)}{/if}
				</div>
				<div class="flex-1 overflow-hidden">
					<span class={PERSON_NAME}>{replaceBioSymbols(listing.displayName)}</span>
					<span class={PRICE}>${price(listing.priceTokens)}V</span>
					{#if listing.duration}<span class={SUBTEXT_MUTED}>{`${listing.duration} ${listing.durationType ?? ''}`}</span>{/if}
					<span class="block text-xs" style="text-wrap: auto">{replaceBioSymbols(listing.description)}</span>
				</div>
			</div>
		{/each}
	</div>
{:else if !loading}
	<DataTableEmpty />
{/if}
