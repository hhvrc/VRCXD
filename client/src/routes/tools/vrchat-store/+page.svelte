<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import X from '@lucide/svelte/icons/x';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { replaceBioSymbols } from '#lib/helpers/format.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';

	// The original's VRChat Store tool (Tools/VRChatStore.vue with useStoreShelves.js): VRChat's
	// official store shelves, filtered by the search box, with Buy or Owned on each listing.
	type Json = Record<string, any>; // eslint-disable-line @typescript-eslint/no-explicit-any

	let searchText = $state('');
	let shelves = $state.raw<Json[]>([]);
	let purchases = $state.raw<Json[]>([]);
	let credits = $state<number | null>(null);
	let loading = $state(false);
	let purchasing = $state('');
	let isSupporter = $state(false);
	let confirming = $state<Confirming | undefined>();

	const account = $derived(vrchatAuth.ownerUserId);
	const path = (rest: string) => accountPath(account, `/search${rest}`);

	const activePurchases = $derived(purchases.filter((p) => p.purchaseActive && p.buyerId === account));
	const ownedListings = $derived(new Set(activePurchases.map((p) => p.listingId)));
	const ownedProducts = $derived(new Set(activePurchases.flatMap((p) => (p.products ?? []).map((product: Json) => product.id))));
	const shownShelves = $derived.by((): Json[] => {
		const query = searchText.trim().toLowerCase();
		if (!query) return shelves;
		return shelves
			.map((shelf) => ({
				...shelf,
				listings: shelf.listings.filter((l: Json) => l.displayName.toLowerCase().includes(query) || l.subtitle?.toLowerCase().includes(query))
			}))
			.filter((shelf) => shelf.listings.length > 0);
	});

	async function loadStore() {
		if (!account) return;
		loading = true;
		try {
			const data = await api.get<{ shelves: Json[]; purchases: Json[]; balance: number | null }>(path('/store'));
			shelves = data.shelves.map((shelf) => ({
				...shelf,
				bannerImageId: shelf.shelfClientBannerImageId || shelf.shelfBackgroundImageId || '',
				logoImageId: shelf.shelfClientBannerImageId ? '' : shelf.shelfMobileLogoImageId || shelf.shelfMobileHeroBannerImageId || '',
				listings: (shelf.listings ?? [])
					.filter((l: Json) => l.active)
					.map((l: Json) => ({ ...l, displayName: replaceBioSymbols(l.displayName), description: replaceBioSymbols(l.description) }))
			}));
			purchases = data.purchases;
			credits = data.balance;
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (!account) return;
		void loadStore();
		api.get<{ tags?: string[] }>(`/1/users/${encodeURIComponent(account)}`).then(
			(me) => (isSupporter = me.tags?.includes('system_supporter') ?? false),
			() => (isSupporter = false)
		);
	});

	// The original's refreshOwnedItems: an inventory content-refresh (a bundle consumed, an item
	// bought) re-reads the purchases, at most every 5 seconds, so Owned stays right.
	$effect(() => {
		let timer: ReturnType<typeof setTimeout> | undefined;
		const stop = onLiveEvent((event) => {
			if (event.event.case !== 'contentRefresh' || event.event.value.contentType !== 'inventory' || event.event.value.accountId !== account) return;
			if (!shelves.length || timer) return;
			timer = setTimeout(() => {
				timer = undefined;
				void loadStore();
			}, 5000);
		});
		return () => {
			stop();
			clearTimeout(timer);
		};
	});

	const commaNumber = (value: number) => value.toLocaleString();
	const fileImage = (fileId: string) => imageSrc(`https://api.vrchat.cloud/api/1/file/${fileId}/1/`);
	const listingImage = (listing: Json) => listing.imageUrl || (listing.imageId ? `https://api.vrchat.cloud/api/1/file/${listing.imageId}/1/` : '');

	function daysLeft(listing: Json): number | null {
		const expireTime = Date.parse(listing.whenToExpire);
		if (!Number.isFinite(expireTime) || expireTime <= Date.now()) return null;
		return Math.ceil((expireTime - Date.now()) / 86400000);
	}

	function listingPrice(listing: Json): string {
		if (!listing.priceTokens) return m.view_tools_marketplace_free();
		const base = `${commaNumber(listing.priceTokens)}V`;
		if (listing.vrcPlusDiscountPrice == null || listing.vrcPlusDiscountPrice === listing.priceTokens) return base;
		return `${base} · ${m.view_tools_marketplace_vrc_plus_price({ price: `${commaNumber(listing.vrcPlusDiscountPrice)}V` })}`;
	}

	function isOwned(listing: Json): boolean {
		if (ownedListings.has(listing.id)) return true;
		const ids: string[] = listing.productIds ?? [];
		return ids.length > 0 && ids.every((id) => ownedProducts.has(id));
	}

	const payable = (listing: Json): number =>
		isSupporter && listing.vrcPlusDiscountPrice != null ? listing.vrcPlusDiscountPrice : (listing.priceTokens ?? 0);
	const canBuy = (listing: Json) => !purchasing && credits !== null && credits >= payable(listing);

	function buy(listing: Json) {
		const total = payable(listing);
		confirming = {
			get title() { return m.confirm_title(); },
			description: m.dialog_avatar_info_purchase_confirm({ name: listing.displayName, price: commaNumber(total) }),
			run: async () => {
				confirming = undefined;
				purchasing = listing.id;
				try {
					await api.post(path('/store/purchase'), { listingId: listing.id, totalPrice: total, quantity: 1 });
					toast.success(m.dialog_avatar_info_purchase_success());
				} catch (err) {
					toastError(err);
				} finally {
					purchasing = '';
					await loadStore();
				}
			}
		};
	}
</script>

<svelte:head><title>{m.view_tools_marketplace_vrchat_store()} · VRCXD</title></svelte:head>

<Tooltip.Provider>
	<div class="flex h-full flex-col overflow-hidden p-4">
		<div class="ml-2 flex items-center gap-2">
			<Button variant="ghost" size="sm" class="mr-3" href="/tools"><ArrowLeft class="size-4" />{m.nav_tooltip_tools()}</Button>
			<span class="text-lg font-semibold">{m.view_tools_marketplace_vrchat_store()}</span>
		</div>

		{#if !account}
			<p class="mt-2 ml-2 text-xs text-muted-foreground">Log in to VRChat first.</p>
		{:else}
			<div class="my-2 flex shrink-0 items-center justify-end gap-2">
				<div class="relative w-[200px]">
					<Input bind:value={searchText} placeholder={m.view_tools_marketplace_search_placeholder()} class="h-8 pr-8" />
					{#if searchText}
						<button type="button" aria-label={m.view_favorite_clear()} class="absolute top-1/2 right-2 -translate-y-1/2 cursor-pointer text-muted-foreground" onclick={() => (searchText = '')}>
							<X class="size-4" />
						</button>
					{/if}
				</div>
				{#if credits !== null}<span class="text-sm text-muted-foreground">{m.view_tools_marketplace_credits({ credits: commaNumber(credits) })}</span>{/if}
				<Tooltip.Root>
					<Tooltip.Trigger>
						{#snippet child({ props })}
							<Button {...props} class="rounded-full" size="icon-sm" variant="ghost" aria-label={m.view_tools_marketplace_refresh_tooltip()} disabled={loading} onclick={loadStore}>
								{#if loading}<Spinner />{:else}<RefreshCw />{/if}
							</Button>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="bottom">{m.view_tools_marketplace_refresh_tooltip()}</Tooltip.Content>
				</Tooltip.Root>
			</div>

			<div class="min-h-0 flex-1 overflow-y-auto">
				{#if loading && !shownShelves.length}
					<div class="flex h-full items-center justify-center"><Spinner class="text-2xl" /></div>
				{:else if shownShelves.length > 0}
					{#each shownShelves as shelf (shelf.id)}
						<div class="mb-6">
							{#if shelf.bannerImageId}
								<div class="relative mb-3 overflow-hidden rounded-lg">
									<img src={fileImage(shelf.bannerImageId)} alt="" loading="lazy" class="max-h-60 w-full object-cover" />
									{#if shelf.logoImageId}
										<img src={fileImage(shelf.logoImageId)} alt="" loading="lazy" class="absolute top-2 left-4 h-[calc(100%-4rem)] max-w-[50%] object-contain object-left drop-shadow-lg" />
									{/if}
									{#if shelf.shelfTitle}
										<div class="absolute inset-x-0 bottom-0 flex items-center gap-3 bg-linear-to-t from-black/80 to-transparent p-4 pt-12 text-lg font-semibold text-white">
											{#if shelf.shelfIconImageId}<img src={fileImage(shelf.shelfIconImageId)} alt="" loading="lazy" class="size-10 rounded object-contain drop-shadow" />{/if}
											<span class="drop-shadow">{shelf.shelfTitle}</span>
										</div>
									{/if}
								</div>
							{:else if shelf.shelfTitle}
								<div class="mb-2 flex items-center gap-2 font-medium">
									{#if shelf.shelfIconImageId}<img src={fileImage(shelf.shelfIconImageId)} alt="" loading="lazy" class="size-6 rounded object-contain" />{/if}
									<span>{shelf.shelfTitle}</span>
								</div>
							{/if}
							{#if shelf.shelfDescription}<div class="mb-2 text-sm text-muted-foreground">{shelf.shelfDescription}</div>{/if}
							{#if shelf.listings.length > 0}
								<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
									{#each shelf.listings as listing (listing.id)}
										{@const image = listingImage(listing)}
										{@const days = daysLeft(listing)}
										<div class="overflow-hidden rounded-lg border p-3">
											<div class="relative">
												{#if days !== null}
													<Badge variant="outline" class="absolute top-2 right-2 border-transparent bg-black/50 text-white/90 backdrop-blur-sm">{m.view_tools_marketplace_days_left({ days })}</Badge>
												{/if}
												{#if image}
													<button type="button" class="block w-full cursor-pointer" aria-label={listing.displayName} onclick={() => imageViewer.show(imageSrc(image))}>
														<img src={imageSrc(image)} alt={listing.displayName} loading="lazy" class="aspect-square w-full rounded-lg object-cover" />
													</button>
												{/if}
											</div>
											<Tooltip.Root>
												<Tooltip.Trigger class="mt-2 block w-full truncate text-left text-sm font-medium">{listing.displayName}</Tooltip.Trigger>
												<Tooltip.Content side="top" class="max-w-80">{listing.description}</Tooltip.Content>
											</Tooltip.Root>
											<p class="line-clamp-1 text-xs text-muted-foreground">{listing.subtitle}</p>
											<p class="line-clamp-1 text-xs text-muted-foreground">{listingPrice(listing)}</p>
											<Button size="sm" variant="outline" class="mt-1 w-full" disabled={isOwned(listing) || !canBuy(listing)} onclick={() => buy(listing)}>
												{#if purchasing === listing.id}<Spinner />{/if}
												{isOwned(listing) ? m.view_tools_marketplace_owned() : m.view_tools_marketplace_buy()}
											</Button>
										</div>
									{/each}
								</div>
							{/if}
						</div>
					{/each}
				{:else}
					<div class="flex h-full items-center justify-center text-sm text-muted-foreground">{m.common_no_data()}</div>
				{/if}
			</div>
		{/if}
	</div>
</Tooltip.Provider>

<ActionDialogs bind:confirming />
