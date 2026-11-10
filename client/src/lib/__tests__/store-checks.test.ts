import { describe, expect, it } from 'vitest';
import { newBundles, newFreeListings, ownership } from '../content/store-checks.js';

describe('newBundles', () => {
	it('keeps unseen bundles and skips the ignored template', () => {
		const shown = new Set(['b1']);
		const items = [
			{ id: 'b1', itemType: 'bundle' },
			{ id: 'b2', itemType: 'bundle' },
			{ id: 'b3', itemType: 'bundle', templateId: 'invt_08e50f24-8807-4ec2-a0f9-9e008371280b' },
			{ id: 'e1', itemType: 'emoji' }
		];
		expect(newBundles(items, shown).map((b) => b.id)).toEqual(['b2']);
	});
});

describe('newFreeListings', () => {
	const owned = ownership(
		[
			{ listingId: 'owned', buyerId: 'usr_me', purchaseActive: true, products: [{ id: 'p9' }] },
			{ listingId: 'lapsed', buyerId: 'usr_me', purchaseActive: false }
		],
		'usr_me'
	);

	it('skips listings a claim failed for before', () => {
		const shelves = [{ listings: [{ id: 'buggy', active: true, priceTokens: 0 }, { id: 'fine', active: true, priceTokens: 0 }] }];
		expect(newFreeListings(shelves, owned, false, new Set(), new Set(['buggy'])).map((l) => l.id)).toEqual(['fine']);
	});

	it('keeps free unowned listings once and drops ones a free bundle covers', () => {
		const shelves = [
			{
				listings: [
					{ id: 'owned', active: true, priceTokens: 0 },
					{ id: 'lapsed', active: true, priceTokens: 0 },
					{ id: 'paid', active: true, priceTokens: 100 },
					{ id: 'inactive', active: false, priceTokens: 0 },
					{ id: 'byproduct', active: true, priceTokens: 0, productIds: ['p9'] },
					{ id: 'single', active: true, priceTokens: 0, productIds: ['p1'] },
					{ id: 'bundle', active: true, priceTokens: 0, productIds: ['p1', 'p2'] }
				]
			}
		];
		const shown = new Set<string>();
		expect(newFreeListings(shelves, owned, false, shown).map((l) => l.id)).toEqual(['lapsed', 'bundle']);
		expect([...shown].sort()).toEqual(['bundle', 'lapsed', 'single']);
		expect(newFreeListings(shelves, owned, false, shown)).toEqual([]);
	});

	it('counts the VRC+ price for supporters', () => {
		const shelves = [{ listings: [{ id: 'plus', active: true, priceTokens: 50, vrcPlusDiscountPrice: 0 }] }];
		expect(newFreeListings(shelves, owned, false, new Set())).toEqual([]);
		expect(newFreeListings(shelves, owned, true, new Set()).map((l) => l.id)).toEqual(['plus']);
	});
});
