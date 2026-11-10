<script lang="ts" module>
	/** A listing's picture: its own URL, or the file its imageId names. */
	export function listingImageUrl(listing: { imageUrl?: string | null; imageId?: string | null }): string {
		if (listing.imageUrl) return listing.imageUrl;
		if (listing.imageId) return `https://api.vrchat.cloud/api/1/file/${listing.imageId}/1/`;
		return '';
	}

	export const commaNumber = (value: number | undefined | null) => (value ?? 0).toLocaleString('en-US');
</script>

<script lang="ts">
	import { TAB_PANE } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { api } from '#lib/api/client.js';
	import StoreListings, { type StoreListing } from '#lib/components/details/store-listings.svelte';
	import TabRefreshButton from '#lib/components/details/tab-refresh-button.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';

	// The original's WorldDialogStoreTab: the listings in the world's store.
	let { path, storeId }: { path: string; storeId: string } = $props();

	type Listing = StoreListing;

	let listings = $state<Listing[]>([]);
	let loading = $state(false);

	export async function getWorldListings() {
		const forPath = path;
		listings = [];
		if (!storeId) return;
		loading = true;
		try {
			const next = await api.get<Listing[]>(`${forPath}/store`, { storeId });
			if (forPath === path) listings = next ?? [];
		} catch (err) {
			console.error(err);
		} finally {
			loading = false;
		}
	}

	// The original fetches whenever the tab is opened; the tab's content mounts each time it is.
	$effect(() => {
		void path;
		void storeId;
		untrack(getWorldListings);
	});
</script>

<div class={TAB_PANE}>
	<div class="flex items-center gap-1">
		<TabRefreshButton label={m.dialog_world_actions_refresh()} {loading} onclick={getWorldListings} />
	</div>
	<StoreListings {listings} {loading} imageOf={listingImageUrl} price={commaNumber}>
		{#snippet image(src)}
			<ViewableImage {src} class="size-full" imageClass="size-full rounded-lg object-cover" />
		{/snippet}
	</StoreListings>
</div>
