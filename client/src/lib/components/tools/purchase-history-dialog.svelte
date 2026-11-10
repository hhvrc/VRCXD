<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { untrack } from 'svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import ToolRefreshButton from './tool-refresh-button.svelte';
	import ToolTypeSelect from './tool-type-select.svelte';
	import { api } from '#lib/api/client.js';
	import { userHref } from '#lib/api/links.js';
	import { formatDateFilter, replaceBioSymbols } from '#lib/helpers/format.js';
	import { LoadState } from '#lib/helpers/load-state.svelte.js';
	import type { EconomyTransaction, ProductPurchase } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's PurchaseHistoryDialog: purchases filtered by listing type, and the last
	// hundred credit transactions.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	let activeTab = $state('purchases');
	let typeFilter = $state('all');
	let purchases = $state<ProductPurchase[]>([]);
	let transactions = $state<EconomyTransaction[]>([]);
	const loader = new LoadState();

	const sorted = $derived([...purchases].sort((a, b) => new Date(b.purchaseDate ?? 0).getTime() - new Date(a.purchaseDate ?? 0).getTime()));
	const typeOptions = $derived([
		{ value: 'all', get label() { return m.dialog_purchase_history_all(); } },
		...[...new Set(sorted.flatMap((p) => (p.listingType ? [p.listingType] : [])))].map((type) => ({ value: type, label: type.charAt(0).toUpperCase() + type.slice(1) }))
	]);
	const filtered = $derived(typeFilter === 'all' ? sorted : sorted.filter((p) => p.listingType === typeFilter));

	const imageUrl = (fileId: string) => `https://api.vrchat.cloud/api/1/file/${encodeURIComponent(fileId)}/1/`;
	const credits = (value: number) => `${value.toLocaleString()}V`;

	async function loadActive() {
		await loader.run(async () => {
			if (activeTab === 'transactions') {
				transactions = (await api.get<{ transactions: EconomyTransaction[] }>(ownPath('/economy-transactions'))).transactions;
			} else {
				purchases = await api.get<ProductPurchase[]>(ownPath('/purchases'));
			}
		});
	}

	$effect(() => {
		if (open && activeTab) untrack(loadActive);
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-3xl">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_purchase_history_header()}</Dialog.Title>
		</Dialog.Header>
		<Tabs.Root bind:value={activeTab}>
			<div class="flex items-center gap-2">
				<Tabs.List>
					<Tabs.Trigger value="purchases">{m.dialog_purchase_history_purchases()}</Tabs.Trigger>
					<Tabs.Trigger value="transactions">{m.dialog_purchase_history_transactions()}</Tabs.Trigger>
				</Tabs.List>
				{#if activeTab === 'purchases'}
					<ToolTypeSelect bind:value={typeFilter} options={typeOptions} />
				{/if}
				<ToolRefreshButton label={m.dialog_note_export_refresh()} loading={loader.isLoading} onclick={loadActive} />
			</div>
			{#if loader.error}<p class="mt-2 text-xs text-destructive">{loader.error}</p>{/if}

			<Tabs.Content value="purchases" class="max-h-[60vh] overflow-y-auto">
				{#if !filtered.length && !loader.isLoading}<p class="{EMPTY_STATE} text-xs">{m.common_no_data()}</p>{/if}
				{#each filtered as purchase (purchase.purchaseId)}
					<div class="flex items-start gap-3 border-b py-3 last:border-b-0">
						{#if purchase.listingImageId}
							<ViewableImage src={imageUrl(purchase.listingImageId)} alt={purchase.listingDisplayName ?? ''} class="flex-none" imageClass="size-12 rounded-md object-cover" />
						{/if}
						<div class="min-w-0 flex-1 text-sm">
							<div class="flex items-center gap-2">
								<span class="truncate font-medium">{replaceBioSymbols(purchase.listingDisplayName)}</span>
								<Badge variant="outline">{purchase.listingType}</Badge>
								{#if purchase.isGift}<Badge variant="secondary">{m.dialog_purchase_history_gift()}</Badge>{/if}
								{#if !purchase.purchaseActive}<Badge variant="destructive">{m.dialog_purchase_history_inactive()}</Badge>{/if}
							</div>
							{#if purchase.listingSubtitle}<div class="text-muted-foreground">{replaceBioSymbols(purchase.listingSubtitle)}</div>{/if}
							<div class="text-muted-foreground">Seller: {purchase.sellerDisplayName} · {purchase.purchasePrice ? credits(purchase.purchasePrice) : 'Free'}</div>
							<div class="mt-1 text-xs text-muted-foreground">
								{formatDateFilter(purchase.purchaseDate, 'long')}
								{#if purchase.purchaseEndDate && purchase.listingType !== 'permanent'}
									· Ends {formatDateFilter(purchase.purchaseEndDate, 'long')}
								{/if}
							</div>
						</div>
					</div>
				{/each}
			</Tabs.Content>

			<Tabs.Content value="transactions" class="max-h-[60vh] overflow-y-auto">
				{#if !transactions.length && !loader.isLoading}<p class="{EMPTY_STATE} text-xs">{m.common_no_data()}</p>{/if}
				{#each transactions as transaction (transaction.transactionLineId)}
					<div class="flex items-start gap-3 border-b py-3 last:border-b-0">
						<div class="min-w-0 flex-1 text-sm">
							<div class="flex items-center gap-2">
								<span class="truncate font-medium">{replaceBioSymbols(transaction.listingDisplayName)}</span>
								{#if transaction.listingType}<Badge variant="outline">{transaction.listingType}</Badge>{/if}
								{#if transaction.reasonLabel}<Badge variant="secondary">{transaction.reasonLabel}</Badge>{/if}
							</div>
							{#if transaction.fromUserDisplayName || transaction.toUserDisplayName}
								<div class="text-muted-foreground">
									{#if transaction.fromUserDisplayName}
										From:
										{#if transaction.fromUserId}<a class="hover:underline" href={userHref(transaction.fromUserId)}>{transaction.fromUserDisplayName}</a>{:else}{transaction.fromUserDisplayName}{/if}
									{/if}
									{#if transaction.fromUserDisplayName && transaction.toUserDisplayName}·{/if}
									{#if transaction.toUserDisplayName}
										To:
										{#if transaction.toUserId}<a class="hover:underline" href={userHref(transaction.toUserId)}>{transaction.toUserDisplayName}</a>{:else}{transaction.toUserDisplayName}{/if}
									{/if}
								</div>
							{/if}
							<div class="mt-1 text-xs text-muted-foreground">
								{formatDateFilter(transaction.date, 'long')}
								{#if transaction.platform}· {transaction.platform}{/if}
							</div>
						</div>
						<div class="flex-none text-right text-sm">
							<div class="font-medium">{credits(transaction.amount)}</div>
							<div class="text-xs text-muted-foreground">Balance: {credits(transaction.balance)}</div>
						</div>
					</div>
				{/each}
			</Tabs.Content>
		</Tabs.Root>
	</Dialog.Content>
</Dialog.Root>

