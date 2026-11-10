<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { replaceBioSymbols } from '#lib/helpers/format.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { storeCheckSettings } from '#lib/content/store-checks.svelte.js';
	import { failedFreeListingsKey, newBundles, newFreeListings, ownership, STORE_CHECK_INTERVAL_MS, type Bundle, type Listing, type Purchase } from '#lib/content/store-checks.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InventoryBundleDialog and FreeStoreItemDialog with the checks that open them:
	// on login and every 3 hours, bundles waiting in the inventory and free store items the account
	// does not own yet. Each is shown once per session; "Don't show this again" turns the check off.

	const account = $derived(vrchatAuth.ownerUserId);

	let bundles = $state<{ open: boolean; items: Bundle[]; dontShow: boolean; busy: boolean }>({ open: false, items: [], dontShow: false, busy: false });
	let listings = $state<{ open: boolean; items: Listing[]; dontShow: boolean; busy: boolean }>({ open: false, items: [], dontShow: false, busy: false });
	let shownBundles = new Set<string>();
	let shownListings = new Set<string>();

	async function checkBundles(accountId: string) {
		if (!storeCheckSettings.inventoryBundles) return;
		let items: Bundle[];
		try {
			items = await api.get<Bundle[]>(accountPath(accountId, '/inventory'));
		} catch (err) {
			console.error('Failed to check inventory bundles:', err);
			return;
		}
		if (accountId !== account) return;
		const found = newBundles(items, shownBundles);
		if (!found.length) return;
		for (const item of found) shownBundles.add(item.id);
		bundles.items = bundles.open ? [...bundles.items, ...found] : found;
		bundles.open = true;
	}

	function failedListings(accountId: string) {
		return new Set(readSetting<string[]>(failedFreeListingsKey(accountId), []));
	}

	async function checkListings(accountId: string) {
		if (!storeCheckSettings.freeStoreItems) return;
		let store: { shelves: { listings?: Listing[] | null }[]; purchases: Purchase[] };
		let supporter = false;
		try {
			const [result, me] = await Promise.all([
				api.get<typeof store>(accountPath(accountId, '/search/store')),
				api.get<{ tags?: string[] }>(`/1/users/${encodeURIComponent(accountId)}`).catch(() => null)
			]);
			store = result;
			supporter = me?.tags?.includes('system_supporter') ?? false;
		} catch (err) {
			console.error('Failed to check free store items:', err);
			return;
		}
		if (accountId !== account) return;
		const found = newFreeListings(store.shelves ?? [], ownership(store.purchases ?? [], accountId), supporter, shownListings, failedListings(accountId)).map((l) => ({ ...l, displayName: replaceBioSymbols(l.displayName) }));
		if (!found.length) return;
		listings.items = listings.open ? [...listings.items, ...found] : found;
		listings.open = true;
	}

	// A new login starts over, as the original's isLoggedIn watch does.
	$effect(() => {
		const accountId = account;
		bundles.open = false;
		listings.open = false;
		shownBundles = new Set();
		shownListings = new Set();
		if (!accountId) return;
		const run = () => {
			checkBundles(accountId);
			checkListings(accountId);
		};
		untrack(run);
		const timer = setInterval(run, STORE_CHECK_INTERVAL_MS);
		return () => clearInterval(timer);
	});

	// Turning a setting off hides its popup; turning it on checks at once.
	let bundlesWasOn = storeCheckSettings.inventoryBundles;
	$effect(() => {
		const on = storeCheckSettings.inventoryBundles;
		if (on === bundlesWasOn) return;
		bundlesWasOn = on;
		if (!on) bundles.open = false;
		else if (account) untrack(() => checkBundles(account!));
	});
	let listingsWasOn = storeCheckSettings.freeStoreItems;
	$effect(() => {
		const on = storeCheckSettings.freeStoreItems;
		if (on === listingsWasOn) return;
		listingsWasOn = on;
		if (!on) listings.open = false;
		else if (account) untrack(() => checkListings(account!));
	});

	function closeBundles() {
		bundles.open = false;
		if (bundles.dontShow && storeCheckSettings.inventoryBundles) storeCheckSettings.setInventoryBundles(false);
		bundles.dontShow = false;
	}

	function closeListings() {
		listings.open = false;
		if (listings.dontShow && storeCheckSettings.freeStoreItems) storeCheckSettings.setFreeStoreItems(false);
		listings.dontShow = false;
	}

	async function consumeBundles() {
		if (!account) return;
		bundles.busy = true;
		for (const bundle of bundles.items) {
			try {
				await api.post(accountPath(account, `/inventory/${encodeURIComponent(bundle.id)}/consume`));
				toast.success(m.dialog_inventory_bundle_dialog_consume_success());
			} catch (err) {
				console.error('Error consuming inventory bundle:', err);
			}
		}
		bundles.busy = false;
		closeBundles();
	}

	async function claimListings() {
		if (!account) return;
		listings.busy = true;
		let claimed = 0;
		const failed: string[] = [];
		for (const listing of listings.items) {
			try {
				await api.post(accountPath(account, '/search/store/purchase'), { listingId: listing.id, totalPrice: 0, quantity: 1 });
				claimed++;
			} catch (err) {
				console.error('Error claiming free store item:', err);
				failed.push(listing.id);
			}
		}
		if (failed.length) storeSetting(failedFreeListingsKey(account), [...new Set([...failedListings(account), ...failed])]);
		if (claimed > 0) toast.success(`Claimed ${claimed} free item(s)`);
		listings.busy = false;
		closeListings();
	}

	const fileImage = (fileId: string) => `https://api.vrchat.cloud/api/1/file/${fileId}/1/`;
</script>

{#snippet row(image: string | null | undefined, title: string | null | undefined, subtitle: string | null | undefined)}
	<div class="flex items-center gap-3">
		{#if image}<img src={imageSrc(image)} alt="" loading="lazy" class="size-12 shrink-0 rounded object-cover" />{:else}<div class="size-12 shrink-0 rounded bg-muted"></div>{/if}
		<div class="min-w-0">
			<div class="truncate text-sm">{title}</div>
			{#if subtitle}<div class="truncate text-xs text-muted-foreground">{subtitle}</div>{/if}
		</div>
	</div>
{/snippet}

<Dialog.Root bind:open={() => bundles.open, (v) => !v && closeBundles()}>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_inventory_bundle_dialog_header()}</Dialog.Title>
			<Dialog.Description>{m.dialog_inventory_bundle_dialog_description()}</Dialog.Description>
		</Dialog.Header>
		<div class="flex max-h-[50vh] flex-col gap-2 overflow-y-auto">
			{#each bundles.items as bundle (bundle.id)}
				{@render row(bundle.imageUrl, bundle.name, bundle.description)}
			{/each}
		</div>
		<Dialog.Footer>
			<label class="inline-flex items-center gap-2 text-sm sm:mr-auto">
				<Checkbox bind:checked={bundles.dontShow} />
				<span>{m.dialog_alertdialog_dont_show_again()}</span>
			</label>
			<Button size="sm" variant="secondary" onclick={closeBundles}>{m.dialog_change_log_close()}</Button>
			<Button size="sm" disabled={bundles.busy} onclick={consumeBundles}>{m.dialog_gallery_icons_consume_bundle()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => listings.open, (v) => !v && closeListings()}>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_free_store_item_dialog_header()}</Dialog.Title>
			<Dialog.Description>{m.dialog_free_store_item_dialog_description()}</Dialog.Description>
		</Dialog.Header>
		<div class="flex max-h-[50vh] flex-col gap-2 overflow-y-auto">
			{#each listings.items as listing (listing.id)}
				{@render row(listing.imageUrl || (listing.imageId ? fileImage(listing.imageId) : ''), listing.displayName, listing.subtitle)}
			{/each}
		</div>
		<Dialog.Footer>
			<label class="inline-flex items-center gap-2 text-sm sm:mr-auto">
				<Checkbox bind:checked={listings.dontShow} />
				<span>{m.dialog_alertdialog_dont_show_again()}</span>
			</label>
			<Button size="sm" variant="secondary" onclick={closeListings}>{m.dialog_change_log_close()}</Button>
			<Button size="sm" disabled={listings.busy} onclick={claimListings}>{m.dialog_free_store_item_dialog_claim()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
