<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { commaNumber, listingImageUrl } from '#lib/components/details/world-store-tab.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { SECTION, PERSON_NAME, PERSON_SUBTEXT, CARD, PRICE, LISTING_IMAGE, LISTING_ROW } from '#lib/details.js';

	// The original's avatar Listings card (AvatarDialog.vue): the avatar's published listings, with
	// Buy, or Owned when one of your active purchases covers the listing or the avatar's product.
	type Listing = { listingId: string; displayName?: string; description?: string; priceTokens?: number; imageId?: string | null };
	type Purchase = { listingId?: string; buyerId?: string; purchaseActive?: boolean; products?: { id: string }[] };

	let { accountPath, owner, listings, productId }: { accountPath: string; owner: string; listings: Listing[]; productId?: string | null } = $props();

	let purchases = $state.raw<Purchase[]>([]);
	let credits = $state<number | null>(null);
	let confirming = $state<Confirming | undefined>();

	const active = $derived(purchases.filter((p) => p.purchaseActive && p.buyerId === owner));
	const ownedListingIds = $derived(new Set(active.map((p) => p.listingId)));
	const ownedProductIds = $derived(new Set(active.flatMap((p) => (p.products ?? []).map((product) => product.id))));

	const isListingOwned = (listing: Listing) => ownedListingIds.has(listing.listingId) || (!!productId && ownedProductIds.has(productId));
	const canPurchaseListing = (listing: Listing) => credits !== null && credits >= (listing.priceTokens ?? 0);

	async function getAllProductPurchases() {
		try {
			const owned = await api.get<{ purchases: Purchase[]; balance: number | null }>(`${accountPath}/purchases/owned`);
			purchases = owned.purchases ?? [];
			credits = owned.balance ?? null;
		} catch (err) {
			console.error('Failed to load purchases', err);
		}
	}

	$effect(() => {
		void accountPath;
		untrack(getAllProductPurchases);
	});

	function purchaseListing(listing: Listing) {
		confirming = {
			get title() { return m.confirm_title(); },
			description: `Purchase ${listing.displayName} for ${commaNumber(listing.priceTokens)} VRC Credits? VRC Credits are purchased with real money and this purchase cannot be reversed.`,
			run: async () => {
				confirming = undefined;
				try {
					await api.post(`${accountPath}/search/store/purchase`, { listingId: listing.listingId, totalPrice: listing.priceTokens ?? 0, quantity: 1 });
					toast.success(m.dialog_avatar_info_purchase_success());
				} catch (err) {
					console.error('Failed to purchase listing', err);
				} finally {
					getAllProductPurchases();
				}
			}
		};
	}
</script>

<div class={CARD}>
	<div class="{SECTION} mb-2 border-b border-border pb-2">{m.dialog_avatar_info_listings()}</div>
	{#each listings as listing (listing.listingId)}
		<div class={LISTING_ROW}>
			<div class={LISTING_IMAGE}>
				<ViewableImage src={listingImageUrl({ imageId: listing.imageId })} class="size-full" imageClass="size-full rounded-lg object-cover" />
			</div>
			<div class="flex-1 overflow-hidden">
				<span class={PERSON_NAME}>{listing.displayName}</span>
				<span class={PRICE}>${commaNumber(listing.priceTokens)}V</span>
				<span class={PERSON_SUBTEXT} style="text-overflow: ellipsis; text-wrap: auto">{listing.description}</span>
			</div>
			<Button size="sm" variant="outline" class="ml-2 flex-none" disabled={isListingOwned(listing) || !canPurchaseListing(listing)} onclick={() => purchaseListing(listing)}>
				{isListingOwned(listing) ? m.view_tools_marketplace_owned() : m.view_tools_marketplace_buy()}
			</Button>
		</div>
	{/each}
</div>

<ActionDialogs bind:confirming />
