<script lang="ts">
	import { TAB_PANE } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import StoreListings, { type StoreListing } from '#lib/components/details/store-listings.svelte';
	import TabRefreshButton from '#lib/components/details/tab-refresh-button.svelte';
	import { api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupDialogStoreTab: the group store's listings, with price and duration.
	let { groupId, storeId, listings = $bindable([]) }: { groupId: string; storeId: string; listings?: Listing[] } = $props();

	type Listing = StoreListing;

	let loading = $state(false);

	const imageOf = (l: Listing) => l.imageUrl || (l.imageId ? `https://api.vrchat.cloud/api/1/file/${l.imageId}/1/` : '');
	const commaNumber = (n: number | undefined) => String(Number(n) || 0).replace(/\B(?=(\d{3})+(?!\d))/g, ',');

	/** getGroupListings. */
	export async function reload() {
		const forGroup = groupId;
		listings = [];
		if (!storeId) return;
		loading = true;
		try {
			const store = await api.get<{ listings?: Listing[] }>(ownPath(`/stores/${encodeURIComponent(storeId)}`));
			if (forGroup === groupId) listings = store.listings ?? [];
		} catch {
			// The original logs and shows nothing.
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		void storeId;
		reload();
	});
</script>

<div class={TAB_PANE}>
	<div class="flex items-center gap-1">
		<TabRefreshButton label={m.dialog_group_actions_refresh()} {loading} onclick={reload} />
	</div>
	<StoreListings {listings} {loading} {imageOf} price={commaNumber}>
		{#snippet image(src)}
			<button type="button" class="size-full" onclick={() => imageViewer.show(src)}>
				<img class="size-full cursor-pointer rounded-lg object-cover" src={imageSrc(src)} alt="" loading="lazy" />
			</button>
		{/snippet}
	</StoreListings>
</div>
