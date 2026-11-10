<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { untrack } from 'svelte';
	import Info from '@lucide/svelte/icons/info';
	import MapPin from '@lucide/svelte/icons/map-pin';
	import RefreshCcw from '@lucide/svelte/icons/refresh-ccw';
	import TrendingDown from '@lucide/svelte/icons/trending-down';
	import TrendingUp from '@lucide/svelte/icons/trending-up';
	import Users from '@lucide/svelte/icons/users';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import * as Sheet from '#lib/components/ui/sheet/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import type { HotWorld, HotWorldFriend } from '#lib/api/types.js';
	import { worldHref } from '#lib/api/links.js';
	import { isDarkMode } from '#lib/activity/charts.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Hot Worlds chart: the top 20 worlds friends moved into over 7/30/90 days,
	// ranked by unique friends, in two columns with a bar per world, and a side sheet listing
	// which friends went there. Data from GET /1/accounts/{id}/hot-worlds.

	let isLoading = $state(true);
	let isLoadingDetail = $state(false);
	let loadError = $state('');
	let selectedDays = $state(30);
	let hotWorlds = $state<HotWorld[]>([]);
	let friendDetail = $state<HotWorldFriend[]>([]);
	let isSheetOpen = $state(false);
	let selectedWorld = $state<(HotWorld & { _rank: number }) | null>(null);
	const dark = $derived.by(() => (typeof document === 'undefined' ? true : isDarkMode()));

	const displayed = $derived(hotWorlds.slice(0, 20));
	const columns = $derived.by(() => {
		const items = displayed.map((w, i) => ({ ...w, _rank: i + 1 }));
		const mid = Math.ceil(items.length / 2);
		return [items.slice(0, mid), items.slice(mid)];
	});
	const maxFriends = $derived(displayed.length === 0 ? 1 : displayed[0].uniqueFriends || 1);
	const risingCount = $derived(displayed.filter((w) => w.trend === 'rising').length);
	const coolingCount = $derived(displayed.filter((w) => w.trend === 'cooling').length);
	const totalVisits = $derived(displayed.reduce((sum, w) => sum + (w.visitCount || 0), 0));

	function barWidth(uniqueFriends: number): string {
		return `${Math.max(4, (uniqueFriends / maxFriends) * 100)}%`;
	}

	async function loadData() {
		if (!vrchatAuth.ownerUserId) return;
		isLoading = true;
		loadError = '';
		try {
			hotWorlds = await api.get<HotWorld[]>(ownPath('/hot-worlds'), { days: selectedDays, limit: 30 });
		} catch (err) {
			hotWorlds = [];
			loadError = ApiError.from(err).message;
		} finally {
			isLoading = false;
		}
	}

	function handleDaysChange(value: string) {
		if (!value) return;
		selectedDays = parseInt(value, 10);
		handleSheetClose(false);
		loadData();
	}

	async function openDetail(world: HotWorld & { _rank: number }) {
		if (!vrchatAuth.ownerUserId) return;
		selectedWorld = world;
		isSheetOpen = true;
		isLoadingDetail = true;
		try {
			friendDetail = await api.get<HotWorldFriend[]>(ownPath(`/hot-worlds/${encodeURIComponent(world.worldId)}/friends`), { days: selectedDays });
		} catch {
			friendDetail = [];
		} finally {
			isLoadingDetail = false;
		}
	}

	function handleSheetClose(open: boolean) {
		if (!open) {
			isSheetOpen = false;
			selectedWorld = null;
			friendDetail = [];
		}
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(() => loadData());
	});
</script>

<svelte:head>
	<title>Hot Worlds · VRCXD</title>
</svelte:head>

{#if !vrchatAuth.ownerUserId}
	<Card.Root>
		<Card.Header>
			<Card.Description>Log in to VRChat to see hot worlds.</Card.Description>
		</Card.Header>
	</Card.Root>
{:else}
	<div class="pt-4">
		<div class="mt-0 flex items-center justify-between">
			<div class="mb-4 flex items-center gap-2">
				<span class="shrink-0">{m.view_charts_hot_worlds_header()}</span>
				<HoverCard.Root>
					<HoverCard.Trigger>
						{#snippet child({ props })}
							<span {...props}><Info class="ml-1 size-3 opacity-70" /></span>
						{/snippet}
					</HoverCard.Trigger>
					<HoverCard.Content side="bottom" align="start" class="w-75">
						<div class="text-xs">
							{m.view_charts_hot_worlds_tips_description()}
						</div>
					</HoverCard.Content>
				</HoverCard.Root>
			</div>

			<div class="flex items-center gap-2">
				<ToggleGroup.Root variant="outline" type="single" value={String(selectedDays)} onValueChange={handleDaysChange}>
					<ToggleGroup.Item value="7">{m.view_charts_hot_worlds_period_days_7()}</ToggleGroup.Item>
					<ToggleGroup.Item value="30">{m.view_charts_hot_worlds_period_days_30()}</ToggleGroup.Item>
					<ToggleGroup.Item value="90">{m.view_charts_hot_worlds_period_days_90()}</ToggleGroup.Item>
				</ToggleGroup.Root>
			</div>
		</div>

		{#if isLoading}
			<div class="mt-[100px] flex items-center justify-center">
				<RefreshCcw class="size-6 animate-spin text-muted-foreground" />
			</div>
		{:else if loadError}
			<p class="mt-[100px] text-center text-sm text-destructive">Couldn't load hot worlds: {loadError}</p>
		{:else if hotWorlds.length === 0}
			<div class="mt-[100px] flex items-center justify-center">
				<DataTableEmpty />
			</div>
		{:else}
			<div class="mx-auto mt-3 flex max-w-[1100px] items-center gap-3">
				<div class="flex items-center gap-2 rounded-lg border px-3 py-2">
					<MapPin class="size-3.5 text-muted-foreground" />
					<span class="text-sm font-medium">{totalVisits.toLocaleString()}</span>
					<span class="text-xs text-muted-foreground">{m.view_charts_hot_worlds_stats_total_visits()}</span>
				</div>
				{#if risingCount > 0}
					<div class="flex items-center gap-2 rounded-lg border px-3 py-2">
						<TrendingUp class="size-3.5 text-green-500/50" />
						<span class="text-sm font-medium">{risingCount}</span>
						<span class="text-xs text-muted-foreground">{m.view_charts_hot_worlds_stats_rising()}</span>
					</div>
				{/if}
				{#if coolingCount > 0}
					<div class="flex items-center gap-2 rounded-lg border px-3 py-2">
						<TrendingDown class="size-3.5 text-blue-400/50" />
						<span class="text-sm font-medium">{coolingCount}</span>
						<span class="text-xs text-muted-foreground">{m.view_charts_hot_worlds_stats_cooling()}</span>
					</div>
				{/if}
				<span class="ml-auto text-xs text-muted-foreground/50">{m.view_charts_hot_worlds_sorted_by()}</span>
			</div>

			<div class="mx-auto mt-3 flex max-w-[1100px] gap-x-6">
				{#each columns as column, colIdx (colIdx)}
					<div class="min-w-0 flex-1">
						{#each column as world (world.worldId)}
							<button
								type="button"
								class="group flex w-full cursor-pointer items-start gap-3 rounded-lg px-3 py-2 text-left transition-colors hover:bg-accent {world._rank === 1 ? 'bg-primary/[0.04]' : ''}"
								onclick={() => openDetail(world)}
							>
								<span class="mt-0.5 w-6 shrink-0 text-right font-mono text-sm font-bold {world._rank === 1 ? 'text-primary' : 'text-muted-foreground'}">#{world._rank}</span>
								<div class="min-w-0 flex-1">
									<div class="flex items-center gap-1.5">
										<span class="block max-w-[380px] cursor-pointer truncate text-sm font-medium hover:underline">{world.worldName}</span>
										{#if world.trend === 'rising'}
											<TrendingUp class="size-3 shrink-0 text-green-500/50" />
										{:else if world.trend === 'cooling'}
											<TrendingDown class="size-3 shrink-0 text-blue-400/50" />
										{/if}
									</div>
									<div class="mt-0.5 text-xs text-muted-foreground">
										{world.uniqueFriends} friends
										<span class="text-muted-foreground/50">({world.visitCount} visits)</span>
									</div>
									<div class="mt-1.5 h-2 w-full overflow-hidden rounded-full {dark ? 'bg-white/[0.08]' : 'bg-black/[0.06]'}">
										<div class="h-full rounded-full transition-all duration-500 {dark ? 'bg-white/[0.45]' : 'bg-black/[0.25]'}" style="width: {barWidth(world.uniqueFriends)}"></div>
									</div>
								</div>
							</button>
						{/each}
					</div>
				{/each}
			</div>
		{/if}
	</div>

	<Sheet.Root open={isSheetOpen} onOpenChange={handleSheetClose}>
		<Sheet.Content side="right" class="w-[340px] sm:max-w-[340px]">
			<Sheet.Header class="px-5">
				<Sheet.Title class="text-left">
					<a class="text-left text-base font-semibold hover:underline" href={selectedWorld ? worldHref(selectedWorld.worldId) : undefined}>{selectedWorld?.worldName}</a>
				</Sheet.Title>
			</Sheet.Header>

			{#if selectedWorld}
				<div class="flex flex-col gap-4 overflow-y-auto px-5">
					<div class="flex flex-wrap items-center gap-2">
						<span class="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2.5 py-1 text-xs font-medium">
							<Users class="size-3" />
							{selectedWorld.uniqueFriends} friends
						</span>
						<span class="inline-flex items-center gap-1 rounded-full bg-muted px-2.5 py-1 text-xs text-muted-foreground">
							<MapPin class="size-3" />
							{selectedWorld.visitCount} visits
						</span>
						{#if selectedWorld.trend === 'rising'}
							<span class="inline-flex items-center gap-1 rounded-full bg-green-500/10 px-2.5 py-1 text-xs text-green-500/70">
								<TrendingUp class="size-3" />
								{m.view_charts_hot_worlds_trend_rising()}
							</span>
						{:else if selectedWorld.trend === 'cooling'}
							<span class="inline-flex items-center gap-1 rounded-full bg-blue-400/10 px-2.5 py-1 text-xs text-blue-400/70">
								<TrendingDown class="size-3" />
								{m.view_charts_hot_worlds_trend_cooling()}
							</span>
						{/if}
					</div>

					<Separator />

					<div>
						<div class="mb-2 text-xs font-medium tracking-wide text-muted-foreground/70 uppercase">{m.view_charts_hot_worlds_sheet_friends_who_visited()}</div>
						{#if isLoadingDetail}
							<div class="flex items-center justify-center py-8">
								<RefreshCcw class="size-4 animate-spin text-muted-foreground" />
							</div>
						{:else if friendDetail.length === 0}
							<div class="{EMPTY_STATE} text-xs">{m.view_charts_hot_worlds_no_friend_data()}</div>
						{:else}
							<div class="space-y-0.5">
								{#each friendDetail as friend (friend.userId)}
									<button
										type="button"
										class="flex w-full cursor-pointer items-center gap-2 rounded-md px-2.5 py-2 text-left text-sm transition-colors hover:bg-accent"
										onclick={() => userDialog.open(friend.userId)}
									>
										<span class="min-w-0 flex-1 truncate">{friend.displayName}</span>
										<span class="shrink-0 rounded-md bg-muted px-1.5 py-0.5 text-2xs text-muted-foreground tabular-nums">{friend.visitCount}×</span>
									</button>
								{/each}
							</div>
						{/if}
					</div>
				</div>
			{/if}
		</Sheet.Content>
	</Sheet.Root>
{/if}
