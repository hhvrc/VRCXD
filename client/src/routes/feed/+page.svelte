<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { nearestPageSize, readTableState, restoredPageSize, writeTableState } from '#lib/settings/table-state.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import { COUNT_BADGE, LOADING_OVERLAY } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import FavoritesOnlyToggle from '#lib/components/common/favorites-only-toggle.svelte';
	import TablePager from '#lib/components/common/table-pager.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { untrack } from 'svelte';
	import { getLocalTimeZone, today, type DateValue } from '@internationalized/date';
	import type { DateRange } from 'bits-ui';
	import ArrowDown from '@lucide/svelte/icons/arrow-down';
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import ListFilter from '@lucide/svelte/icons/list-filter';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { RangeCalendar } from '#lib/components/ui/range-calendar/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import AvatarInfo from '#lib/components/details/avatar-info.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import UserContextMenu from '#lib/components/common/user-context-menu.svelte';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { FEED_TYPES, type FeedEntry, type FeedType, type Friend } from '#lib/api/types.js';
	import { formatDateFilter, formatDifference, getFaviconUrl, replaceBioSymbols, statusClass, timeToText } from '#lib/helpers/format.js';
	import { live, watchLive } from '#lib/api/live.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';

	// The original's Feed view over GET /1/accounts/{id}/feed. Filters (type set, favorites
	// only, search, date range) go to the server; like the original, the result is capped at
	// maxTableSize rows and sorted and paged here. New rows arrive by refetching when the live
	// socket reports a presence or profile change.

	// Settings > Interface > Table Entries Settings and Table Page Sizes.
	const MAX_TABLE_SIZE = $derived(appearance.maxTableSize.value);
	const PAGE_SIZES = $derived(appearance.tablePageSizes.value);
	const FILTERS_KEY = 'VRCX_feedTableFilters';
	const VIP_KEY = 'VRCX_feedTableVIPFilter';


	let filter = $state<FeedType[]>(readSetting<FeedType[]>(FILTERS_KEY, []).filter((t) => FEED_TYPES.includes(t)));
	let vip = $state(readSetting<boolean>(VIP_KEY, false));
	let search = $state('');
	let dateFrom = $state('');
	let dateTo = $state('');
	let rows = $state<FeedEntry[]>([]);
	let loading = $state(false);
	let loadError = $state('');
	let friends = $state<Map<string, Friend>>(new Map());

	// The sort and page size stay across visits under the original's vrcx:table:feed name (upstream
	// issue 1230); with no saved page size the Table Page Size setting is used.
	const TABLE_KEY = 'feed';
	const savedTable = readTableState(TABLE_KEY);
	const savedSort = savedTable.sorting?.find((s) => s.id === 'created_at');
	let sortAsc = $state<boolean | null>(savedSort ? !savedSort.desc : null);
	$effect(() => {
		const sorting = sortAsc === null ? [] : [{ id: 'created_at', desc: !sortAsc }];
		writeTableState(TABLE_KEY, { sorting });
	});
	let pageSize = $state(restoredPageSize(savedTable, tableSettings.pageSize));
	let currentPage = $state(1);
	let expanded = $state<Set<string>>(new Set());

	// Date range popover
	let popoverOpen = $state(false);
	let dateRange = $state<DateRange>({ start: undefined, end: undefined });
	const todayDate = today(getLocalTimeZone());
	const activeFilterCount = $derived(dateFrom || dateTo ? 1 : 0);

	const activeFilterSelection = $derived(filter.length === 0 ? ['All'] : filter as string[]);

	function rowKey(row: FeedEntry) {
		return `row:${row.rowId}:${row.type}`;
	}

	const sorted = $derived.by(() => {
		if (sortAsc === null) return rows;
		const copy = [...rows];
		copy.sort((a, b) => (sortAsc ? 1 : -1) * (new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()));
		return copy;
	});
	// The original shows the cap, not the overflow, when the sweep left a few extra rows behind.
	const totalItems = $derived(rows.length > MAX_TABLE_SIZE && rows.length < MAX_TABLE_SIZE + 51 ? MAX_TABLE_SIZE : rows.length);
	const pageCount = $derived(Math.max(1, Math.ceil(totalItems / pageSize)));
	const pageRows = $derived(sorted.slice((currentPage - 1) * pageSize, currentPage * pageSize));
	const hasActiveFilters = $derived(search.trim().length > 0 || vip || filter.length > 0 || !!dateFrom || !!dateTo);

	let requestId = 0;
	async function lookup() {
		const ownerUserId = vrchatAuth.ownerUserId;
		if (!ownerUserId) return;
		const id = ++requestId;
		loading = true;
		loadError = '';
		storeSetting(FILTERS_KEY, filter);
		storeSetting(VIP_KEY, vip);
		try {
			const result = await api.get<FeedEntry[]>(accountPath(ownerUserId, '/feed'), {
				types: filter.join(','),
				search: search.trim(),
				vip,
				from: dateFrom,
				to: dateTo,
				limit: search.trim() ? appearance.searchLimit.value : MAX_TABLE_SIZE
			});
			if (id !== requestId) return;
			rows = result;
			if (currentPage > Math.max(1, Math.ceil(result.length / pageSize))) currentPage = 1;
		} catch (err) {
			if (id !== requestId) return;
			loadError = ApiError.from(err).message;
		} finally {
			if (id === requestId) loading = false;
		}
	}

	async function loadFriends() {
		if (!vrchatAuth.ownerUserId) return;
		try {
			const list = await api.get<Friend[]>(ownPath('/friends'));
			friends = new Map(list.map((f) => [f.userId, f]));
		} catch {
			// Only the context menu uses this.
		}
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(() => {
			lookup();
			loadFriends();
		});
	});

	// Refetch (debounced) when the feed can have grown.
	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		return watchLive(['presence', 'user'], lookup, 750);
	});

	function handleFeedFilterChange(value: string[]) {
		const wasAll = activeFilterSelection.includes('All');
		const hasAll = value.includes('All');
		const types = value.filter((v): v is FeedType => v !== 'All' && FEED_TYPES.includes(v as FeedType));
		if (hasAll && !wasAll) filter = [];
		else if (wasAll && types.length) filter = types;
		else filter = types.length === FEED_TYPES.length ? [] : types;
		currentPage = 1;
		lookup();
	}

	function dayStart(d: DateValue): string {
		return new Date(d.year, d.month - 1, d.day, 0, 0, 0, 0).toISOString();
	}
	function dayEnd(d: DateValue): string {
		return new Date(d.year, d.month - 1, d.day, 23, 59, 59, 999).toISOString();
	}
	function applyDateFilter() {
		dateFrom = dateRange.start ? dayStart(dateRange.start) : '';
		dateTo = dateRange.end ? dayEnd(dateRange.end) : '';
		popoverOpen = false;
		currentPage = 1;
		lookup();
	}
	function clearDateFilter() {
		dateRange = { start: undefined, end: undefined };
		dateFrom = '';
		dateTo = '';
		popoverOpen = false;
		currentPage = 1;
		lookup();
	}

	function toggleExpanded(key: string) {
		const next = new Set(expanded);
		if (next.has(key)) next.delete(key);
		else next.add(key);
		expanded = next;
	}

	function setPageSize(value: string) {
		const size = Number(value);
		if (!PAGE_SIZES.includes(size)) return;
		pageSize = size;
		currentPage = 1;
		writeTableState(TABLE_KEY, { pageSize: size });
	}

	// A page size no longer offered moves to the closest offered one, as the original's pager does.
	$effect(() => {
		const next = nearestPageSize(pageSize, PAGE_SIZES);
		if (next !== pageSize) untrack(() => setPageSize(String(next)));
	});

	function friendState(userId: string): string {
		return live.presence.get(userId)?.state ?? friends.get(userId)?.state ?? '';
	}
	function friendLocation(userId: string): string {
		return live.presence.get(userId)?.location ?? friends.get(userId)?.location ?? '';
	}

	function bioLinkDifference(previousLinks: string[] | null | undefined, links: string[] | null | undefined) {
		const prev = previousLinks ?? [];
		const cur = links ?? [];
		return [
			...cur.map((link) => ({ link, className: prev.includes(link) ? '' : 'x-text-added' })),
			...prev.filter((link) => !cur.includes(link)).map((link) => ({ link, className: 'x-text-removed' }))
		];
	}

</script>

<svelte:head>
	<title>Feed · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="feed flex min-h-0 w-full flex-1 flex-col">
		{#if !vrchatAuth.ownerUserId}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see the feed.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else}
			<!-- toolbar -->
			<div class="mx-0 mt-0 mb-2 flex items-center">
				<div class="mr-2 flex flex-none items-center">
					<Popover.Root bind:open={popoverOpen}>
						<Popover.Trigger>
							{#snippet child({ props })}
								<Button {...props} variant="outline" size="sm" class="mx-2 h-8 gap-1.5">
									<ListFilter class="size-4" />
									Filter
									{#if activeFilterCount}
										<Badge variant="secondary" class={COUNT_BADGE}>{activeFilterCount}</Badge>
									{/if}
								</Button>
							{/snippet}
						</Popover.Trigger>
						<Popover.Content class="w-auto" side="bottom" align="end">
							<RangeCalendar bind:value={dateRange} maxValue={todayDate} numberOfMonths={2} />
							<div class="mt-3 flex justify-end gap-2">
								<Button variant="outline" size="sm" onclick={clearDateFilter}>{m.view_favorite_clear()}</Button>
								<Button size="sm" onclick={applyDateFilter}>{m.dialog_alertdialog_confirm()}</Button>
							</div>
						</Popover.Content>
					</Popover.Root>
					<FavoritesOnlyToggle
						pressed={vip}
						onPressedChange={(v) => {
							vip = v;
							currentPage = 1;
							lookup();
						}}
					/>
				</div>
				<ToggleGroup.Root
					type="multiple"
					variant="outline"
					size="sm"
					value={activeFilterSelection}
					onValueChange={handleFeedFilterChange}
					class="w-full flex-1 justify-start"
				>
					<ToggleGroup.Item value="All">{m.view_search_avatar_all()}</ToggleGroup.Item>
					{#each FEED_TYPES as type (type)}
						<ToggleGroup.Item value={type}>{type}</ToggleGroup.Item>
					{/each}
				</ToggleGroup.Root>
				<Input
					class="ml-2 h-8"
					style="flex: 0.4"
					placeholder={m.view_favorite_worlds_search()}
					bind:value={search}
					onkeyup={(e) => {
						if (e.key === 'Enter') {
							currentPage = 1;
							lookup();
						}
					}}
					onchange={() => {
						currentPage = 1;
						lookup();
					}}
				/>
			</div>

			<!-- table -->
			<div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-md border">
				<div class="relative min-h-0 max-w-full flex-1 overflow-auto">
					<Table.Root class="w-full table-fixed">
						<Table.Header>
							<Table.Row>
								<Table.Head
									class="sticky top-0 z-10 w-5 border-b border-border bg-background p-0 dark:bg-sidebar"
								/>

								<Table.Head
									class="sticky top-0 z-10 w-[140px] border-b border-border bg-background dark:bg-sidebar"
								>
									<Button
										variant="ghost"
										class="pl-0!"
										onclick={() => sortAsc = sortAsc === true ? false : sortAsc === false ? null : true}
									>{m.dialog_registry_backup_date()} <ArrowUpDown class="ml-1 h-4 w-4" /></Button>
								</Table.Head>
								<Table.Head class="sticky top-0 z-10 w-[130px] border-b border-border bg-background dark:bg-sidebar">{m.dialog_group_member_moderation_type()}</Table.Head>
								<Table.Head class="sticky top-0 z-10 w-[190px] border-b border-border bg-background dark:bg-sidebar">{m.view_search_user_header()}</Table.Head>
								<Table.Head class="sticky top-0 z-10 border-b border-border bg-background dark:bg-sidebar">{m.table_feed_detail()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#if pageRows.length}
								{#each pageRows as row (rowKey(row))}
									{@const key = rowKey(row)}
									{@const isExpanded = expanded.has(key)}
									<Table.Row class="group/row" onclick={() => toggleExpanded(key)}>
										<Table.Cell class="p-0 pl-2">
											<button
												type="button"
												class="inline-flex h-6 cursor-pointer items-center justify-center text-xs text-muted-foreground hover:text-foreground"
												onclick={(e) => {
													e.stopPropagation();
													toggleExpanded(key);
												}}
											>
												{#if isExpanded}<ChevronDown class="size-4" />{:else}<ChevronRight class="size-4" />{/if}
											</button>
										</Table.Cell>
										<Table.Cell>
											<Tooltip.Root>
												<Tooltip.Trigger>
													{#snippet child({ props })}
														<span {...props}>{formatDateFilter(row.createdAt, 'short')}</span>
													{/snippet}
												</Tooltip.Trigger>
												<Tooltip.Content side="right">{formatDateFilter(row.createdAt, 'long')}</Tooltip.Content>
											</Tooltip.Root>
										</Table.Cell>
										<Table.Cell>
											<div><Badge variant="outline" class="text-muted-foreground">{row.type}</Badge></div>
										</Table.Cell>
										<Table.Cell class="truncate">
											<UserContextMenu userId={row.userId} name={row.displayName} state={friendState(row.userId)} location={friendLocation(row.userId)}>
												<span
													class="cursor-pointer pr-2.5"
													role="link"
													tabindex="0"
													onclick={(e) => {
														e.stopPropagation();
														userDialog.open(row.userId);
													}}
													onkeydown={(e) => {
														if (e.key === 'Enter') userDialog.open(row.userId);
													}}>{row.displayName}</span
												>
											</UserContextMenu>
										</Table.Cell>
										<Table.Cell class="min-w-0">
											{#if row.type === 'GPS' || row.type === 'Online' || row.type === 'Offline'}
												{#if row.location}
													<div class="w-full min-w-0 truncate">
														<Location location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} disableTooltip />
													</div>
												{/if}
											{:else if row.type === 'Status'}
												{#if row.statusDescription === row.previousStatusDescription}
													<div class="flex items-center">
														<i class="x-user-status {statusClass(row.previousStatus)}"></i>
														<span class="mx-2"><ArrowRight class="size-4" /></span>
														<i class="x-user-status {statusClass(row.status)}"></i>
													</div>
												{:else}
													<div class="w-full min-w-0 truncate">
														<i class="x-user-status mr-2 {statusClass(row.status)}" style="display:-webkit-inline-box"></i>
														<span style="display:-webkit-inline-box">{row.statusDescription}</span>
													</div>
												{/if}
											{:else if row.type === 'Avatar'}
												<div class="w-full min-w-0 truncate">
													<AvatarInfo imageUrl={row.currentAvatarImageUrl} userId={row.userId} ownerId={row.ownerId} avatarName={row.avatarName} />
												</div>
											{:else if row.type === 'Bio'}
												<span class="block w-full min-w-0 truncate">{replaceBioSymbols(row.bio)}</span>
											{/if}
										</Table.Cell>
									</Table.Row>
									{#if isExpanded}
										<Table.Row>
											<Table.Cell colspan={5} class="whitespace-normal">
												{#if row.type === 'GPS'}
													<div class="pl-5 text-sm">
														{#if row.previousLocation}
															<Location location={row.previousLocation} hint={row.previousWorldName ?? ''} class="inline-block" />
															<Badge variant="secondary" class="ml-1 w-fit">{timeToText(row.time)}</Badge>
															<br />
															<span><ArrowDown class="size-4" /></span>
														{/if}
														{#if row.location}
															<Location location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} />
														{/if}
													</div>
												{:else if row.type === 'Offline'}
													{#if row.location}
														<div class="pl-5 text-sm">
															<Location location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} />
															<Badge variant="secondary" class="ml-1 w-fit">{timeToText(row.time)}</Badge>
														</div>
													{/if}
												{:else if row.type === 'Online'}
													{#if row.location}
														<div class="pl-5 text-sm">
															<Location location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} />
														</div>
													{/if}
												{:else if row.type === 'Avatar'}
													<div class="pl-5 text-sm">
														<div class="flex items-center">
															<div class="inline-block w-40 align-top">
																{#if row.previousCurrentAvatarThumbnailImageUrl}
																		<ViewableImage src={row.previousCurrentAvatarThumbnailImageUrl} full={row.previousCurrentAvatarImageUrl} imageClass="h-30 w-40 rounded" />
																	<br />
																	<AvatarInfo
																		imageUrl={row.previousCurrentAvatarThumbnailImageUrl}
																		userId={row.userId}
																		ownerId={row.previousOwnerId}
																		avatarName={row.previousAvatarName}
																	/>
																{/if}
															</div>
															<span class="mx-2"><ArrowRight class="size-4" /></span>
															<div class="inline-block w-40 align-top">
																{#if row.currentAvatarThumbnailImageUrl}
																		<ViewableImage src={row.currentAvatarThumbnailImageUrl} full={row.currentAvatarImageUrl} imageClass="h-30 w-40 rounded" />
																	<br />
																	<AvatarInfo imageUrl={row.currentAvatarThumbnailImageUrl} userId={row.userId} ownerId={row.ownerId} avatarName={row.avatarName} />
																{/if}
															</div>
														</div>
													</div>
												{:else if row.type === 'Status'}
													<div class="flex items-center pl-5 text-sm">
														<i class="x-user-status {statusClass(row.previousStatus)}"></i>
														<span class="ml-1">{row.previousStatusDescription}</span>
														<span class="mx-2"><ArrowRight class="size-4" /></span>
														<i class="x-user-status mx-1 {statusClass(row.status)}"></i>
														<span>{row.statusDescription}</span>
													</div>
												{:else if row.type === 'Bio'}
													{@const links = bioLinkDifference(row.previousBioLinks, row.bioLinks)}
													<div class="pl-5 text-sm">
														<!-- eslint-disable-next-line svelte/no-at-html-tags -- formatDifference escapes its input -->
														<pre class="font-[inherit] text-xs leading-5.5 whitespace-pre-wrap">{@html formatDifference(replaceBioSymbols(row.previousBio), replaceBioSymbols(row.bio))}</pre>
														{#if links.length}
															<div class="mt-1 flex flex-col gap-0.5 text-xs">
																{#each links as { link, className } (link)}
																	<a class="flex cursor-pointer items-center gap-1" href={link} target="_blank" rel="noopener noreferrer">
																		<img src={getFaviconUrl(link)} alt="" class="size-4" loading="lazy" />
																		<span class={className}>{link}</span>
																	</a>
																{/each}
															</div>
														{/if}
													</div>
												{/if}
											</Table.Cell>
										</Table.Row>
									{/if}
								{/each}
							{:else}
								<Table.Row>
									<Table.Cell class="h-24 text-center" colspan={5}>
										{#if loadError}
											<span class="text-sm text-destructive">Couldn't load the feed: {loadError}</span>
										{:else if !loading}
											<DataTableEmpty type={hasActiveFilters ? 'nomatch' : 'nodata'} />
										{/if}
									</Table.Cell>
								</Table.Row>
							{/if}
						</Table.Body>
					</Table.Root>
					{#if loading}
						<div class={LOADING_OVERLAY}>
							<Spinner class="size-6" />
						</div>
					{/if}
				</div>
			</div>

			<!-- pagination footer -->
			<TablePager count={totalItems} {pageSize} pageSizes={PAGE_SIZES} onPageSizeChange={setPageSize} bind:page={currentPage} />
			{#if pageCount < currentPage}
				<!-- keeps the derived in use so the page clamps when the data shrinks -->
			{/if}
		{/if}
	</div>
</Tooltip.Provider>
