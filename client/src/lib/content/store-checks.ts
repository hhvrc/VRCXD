// The original's checkInventoryBundles and checkFreeStoreItems (stores/notification), as pure
// functions: which bundles and free store listings are new enough to show.

export const INVENTORY_BUNDLE_NOTIFICATIONS_KEY = 'VRCX_inventoryBundleNotifications';
export const FREE_STORE_ITEM_NOTIFICATIONS_KEY = 'VRCX_freeStoreItemNotifications';

/** Listings a claim failed for (buggy items VRChat won't hand out), per user, never shown again. */
export function failedFreeListingsKey(userId: string) {
	return `VRCX_failedFreeListingIds_${userId}`;
}

/** The original re-checks every 3 hours (updateLoop's nextBundleCheck). */
export const STORE_CHECK_INTERVAL_MS = 3 * 60 * 60 * 1000;

/** "Thanks for this buggy unredeemable bundle...": never shown. */
export const IGNORED_BUNDLE_TEMPLATE_IDS = new Set(['invt_08e50f24-8807-4ec2-a0f9-9e008371280b']);

export type Bundle = { id: string; templateId?: string | null; itemType?: string | null; name?: string | null; description?: string | null; imageUrl?: string | null };

export type Listing = {
	id: string;
	active?: boolean;
	displayName?: string;
	subtitle?: string;
	imageUrl?: string;
	imageId?: string;
	priceTokens?: number | null;
	vrcPlusDiscountPrice?: number | null;
	productIds?: string[];
};

export type Purchase = { listingId?: string; buyerId?: string; purchaseActive?: boolean; products?: { id: string }[] };

/** Bundles not shown yet in this session, newest first as the inventory sends them. */
export function newBundles(items: Bundle[], shown: Set<string>): Bundle[] {
	return items.filter((item) => item.itemType === 'bundle' && !shown.has(item.id) && !IGNORED_BUNDLE_TEMPLATE_IDS.has(item.templateId ?? ''));
}

/** getEffectiveListingPrice. */
export function effectivePrice(listing: Listing, supporter: boolean): number {
	if (supporter && listing.vrcPlusDiscountPrice != null) return listing.vrcPlusDiscountPrice;
	return listing.priceTokens ?? 0;
}

/** isListingOwned, from the account's active purchases. */
export function ownership(purchases: Purchase[], buyerId: string) {
	const active = purchases.filter((p) => p.purchaseActive && p.buyerId === buyerId);
	const listings = new Set(active.map((p) => p.listingId).filter((id): id is string => !!id));
	const products = new Set(active.flatMap((p) => (p.products ?? []).map((product) => product.id)));
	return (listing: Listing) => {
		if (listings.has(listing.id)) return true;
		const ids = listing.productIds ?? [];
		return ids.length > 0 && ids.every((id) => products.has(id));
	};
}

/** isListingInBundle: another free listing carries all of this one's products and more. */
function inBundle(listing: Listing, listings: Map<string, Listing>): boolean {
	const ids = listing.productIds ?? [];
	if (!ids.length) return false;
	for (const other of listings.values()) {
		const otherIds = other.productIds ?? [];
		if (other.id !== listing.id && otherIds.length > ids.length && ids.every((id) => otherIds.includes(id))) return true;
	}
	return false;
}

/**
 * Active, free, unowned listings not shown yet in this session, without the ones a free bundle
 * already includes. Every free listing found is marked shown, bundled or not, as the original does.
 */
export function newFreeListings(
	shelves: { listings?: Listing[] | null }[],
	owned: (listing: Listing) => boolean,
	supporter: boolean,
	shown: Set<string>,
	failed: Set<string> = new Set()
): Listing[] {
	const free = new Map<string, Listing>();
	for (const shelf of shelves) {
		for (const listing of shelf.listings ?? []) {
			if (listing.active && !effectivePrice(listing, supporter) && !owned(listing) && !shown.has(listing.id) && !failed.has(listing.id)) free.set(listing.id, listing);
		}
	}
	for (const id of free.keys()) shown.add(id);
	return [...free.values()].filter((listing) => !inBundle(listing, free));
}
