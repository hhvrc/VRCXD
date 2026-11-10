<script lang="ts">
	import { COUNT_BADGE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { lookupUser } from '#lib/people/user-lookup.js';
	import { getLocalTimeZone, today, type DateValue } from '@internationalized/date';
	import type { DateRange } from 'bits-ui';
	import type { Snippet } from 'svelte';
	import { untrack } from 'svelte';
	import CalendarRange from '@lucide/svelte/icons/calendar-range';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import Copy from '@lucide/svelte/icons/copy';
	import ExternalLink from '@lucide/svelte/icons/external-link';
	import LogIn from '@lucide/svelte/icons/log-in';
	import LogOut from '@lucide/svelte/icons/log-out';
	import Play from '@lucide/svelte/icons/play';
	import UserMinus from '@lucide/svelte/icons/user-minus';
	import UserPlus from '@lucide/svelte/icons/user-plus';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { RangeCalendar } from '#lib/components/ui/range-calendar/index.js';
	import { Skeleton } from '#lib/components/ui/skeleton/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import FavoritesOnlyToggle from '#lib/components/common/favorites-only-toggle.svelte';
	import LocationMenu from '#lib/components/instance/location-menu.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import { userHref } from '#lib/api/links.js';
	import { formatDateFilter, timeToText } from '#lib/helpers/format.js';
	import { buildGameLogSessions, isGroup, searchSessions, type GameLogRow, type GroupMember, type SessionEvent, type SessionSegment } from '#lib/activity/game-log-sessions.js';
	import { desktop } from '#lib/instances/instance-launch.svelte.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GameLogSessions: stays with what happened in them, loading more on scroll.
	let { leading, onpreviousinstances }: { leading: Snippet; onpreviousinstances: (location: string) => void } = $props();

	type SessionsPage = { locations: GameLogRow[]; events: GameLogRow[]; hasMore: boolean };

	const FILTER_TYPES = [
		{ value: 'OnPlayerJoined', get label() { return m.view_game_log_filters_OnPlayerJoined(); } },
		{ value: 'OnPlayerLeft', get label() { return m.view_game_log_filters_OnPlayerLeft(); } },
		{ value: 'VideoPlay', get label() { return m.view_game_log_filters_VideoPlay(); } }
	];
	const FIRST_BATCH = 10;
	const NEXT_BATCH = 5;
	const SEARCH_BATCH = 50;
	const SEARCH_ATTEMPTS = 3;
	const SEARCH_LIMIT = 5000;
	const RANGE_MAX_DAYS = 7;

	let rawLocations = $state.raw<GameLogRow[]>([]);
	let rawEvents = $state.raw<GameLogRow[]>([]);
	let cursor: number | null = null;
	let hasMore = $state(true);
	let loading = $state(false);
	let resolved = $state(false);
	let loadError = $state('');
	let vip = $state(false);
	let filters = $state<string[]>([]);
	let typed = $state('');
	let search = $state('');
	let dateFrom = $state('');
	let dateTo = $state('');
	let dateRange = $state<DateRange>({ start: undefined, end: undefined });
	let dateOpen = $state(false);
	let collapsed = $state<Record<number, boolean>>({});
	let expanded = $state<Record<string, boolean>>({});
	let generation = 0;
	const todayDate = today(getLocalTimeZone());

	const selection = $derived(filters.length === 0 ? ['All'] : filters);
	const hasDateFilter = $derived(!!dateFrom || !!dateTo);

	const segments = $derived.by(() => {
		let events = rawEvents;
		if (vip) events = events.filter((event) => event.type === 'VideoPlay' || event.isFavorite);
		if (filters.length > 0) events = events.filter((event) => filters.includes(event.type));
		return searchSessions(buildGameLogSessions(rawLocations, events), search, SEARCH_LIMIT);
	});

	async function fetchPage(limit: number, beforeId: number | null = cursor): Promise<SessionsPage> {
		const params: Record<string, string | number> = { limit };
		if (beforeId !== null) params.beforeId = beforeId;
		if (dateFrom) params.from = dateFrom;
		if (dateTo) params.to = dateTo;
		return api.get<SessionsPage>(ownPath('/game-log/sessions'), params);
	}

	function take(page: SessionsPage) {
		rawLocations = [...rawLocations, ...page.locations];
		rawEvents = [...rawEvents, ...page.events];
		if (page.locations.length > 0) cursor = page.locations[page.locations.length - 1].id;
		hasMore = page.hasMore;
	}

	async function reload() {
		const mine = ++generation;
		loading = true;
		cursor = null;
		rawLocations = [];
		rawEvents = [];
		hasMore = true;
		try {
			const searching = !!search.trim() && !hasDateFilter;
			let attempts = 0;
			do {
				const page = await fetchPage(searching ? SEARCH_BATCH : FIRST_BATCH);
				if (mine !== generation) return;
				take(page);
				attempts++;
			} while (searching && hasMore && segments.length === 0 && attempts < SEARCH_ATTEMPTS);
			loadError = '';
		} catch (err) {
			if (mine === generation) loadError = ApiError.from(err).message;
		} finally {
			if (mine === generation) {
				loading = false;
				resolved = true;
			}
		}
	}

	/**
	 * A new visit or log line: fetch the newest sessions and merge them over what is loaded, so the
	 * pages already scrolled in stay (the original appends live rows the same way).
	 */
	async function refreshNewest() {
		if (loading) return;
		const mine = generation;
		try {
			const page = await fetchPage(FIRST_BATCH, null);
			if (mine !== generation) return;
			const byKey = <T extends { key: string }>(fresh: T[], old: T[]) => {
				const keys = new Set(fresh.map((row) => row.key));
				return [...fresh, ...old.filter((row) => !keys.has(row.key))];
			};
			const newestFirst = (a: GameLogRow, b: GameLogRow) => b.createdAt.localeCompare(a.createdAt) || (b.id ?? 0) - (a.id ?? 0);
			rawLocations = byKey(page.locations, rawLocations).sort(newestFirst);
			rawEvents = byKey(page.events, rawEvents);
			if (cursor === null && page.locations.length > 0) {
				cursor = page.locations[page.locations.length - 1].id;
				hasMore = page.hasMore;
			}
		} catch {
			// The next event or a scroll tries again.
		}
	}

	async function loadMore() {
		if (loading || !hasMore) return;
		const mine = generation;
		loading = true;
		try {
			const searching = !!search.trim() && !hasDateFilter;
			const before = segments.length;
			let attempts = 0;
			do {
				const page = await fetchPage(searching ? SEARCH_BATCH : NEXT_BATCH);
				if (mine !== generation) return;
				take(page);
				attempts++;
			} while (searching && hasMore && segments.length === before && attempts < SEARCH_ATTEMPTS);
		} catch (err) {
			if (mine === generation) loadError = ApiError.from(err).message;
		} finally {
			if (mine === generation) loading = false;
		}
	}

	function setFilters(value: string[]) {
		const types = value.filter((item) => item !== 'All');
		const wasAll = filters.length === 0;
		if (value.includes('All') && !wasAll) filters = [];
		else filters = types.length === FILTER_TYPES.length ? [] : types;
	}

	function applySearch() {
		if (search === typed) return;
		search = typed;
		reload();
	}

	function clampDays(start: DateValue, end: DateValue): DateValue {
		return end.compare(start.add({ days: RANGE_MAX_DAYS })) > 0 ? start.add({ days: RANGE_MAX_DAYS }) : end;
	}

	function applyDateRange() {
		dateOpen = false;
		const first = dateRange.start ?? dateRange.end;
		const last = dateRange.end ?? dateRange.start;
		if (!first || !last) return clearDateRange();
		const zone = getLocalTimeZone();
		const from = first.toDate(zone);
		from.setHours(0, 0, 0, 0);
		const to = clampDays(first, last).toDate(zone);
		to.setHours(23, 59, 59, 999);
		dateFrom = from.toISOString();
		dateTo = to.toISOString();
		reload();
	}

	function clearDateRange() {
		dateOpen = false;
		dateRange = { start: undefined, end: undefined };
		dateFrom = '';
		dateTo = '';
		reload();
	}

	function counts(segment: SessionSegment) {
		let joined = 0;
		let left = 0;
		let videos = 0;
		for (const event of segment.events) {
			if (event.type === 'OnPlayerJoined') joined++;
			else if (event.type === 'OnPlayerLeft') left++;
			else if (event.type === 'VideoPlay') videos++;
			else if (event.type === 'JoinGroup' && isGroup(event)) joined += event.count;
			else if (event.type === 'LeftGroup' && isGroup(event)) left += event.count;
		}
		return { joined, left, videos };
	}

	function onScroll(event: Event) {
		const box = event.currentTarget as HTMLElement;
		if (box.scrollHeight - box.scrollTop - box.clientHeight <= 320) loadMore();
	}

	// A short page has nothing to scroll: keep filling until the sentinel is off screen.
	function sentinel(node: HTMLElement) {
		const observer = new IntersectionObserver((entries) => {
			if (entries.some((entry) => entry.isIntersecting)) loadMore();
		}, { rootMargin: '200px' });
		observer.observe(node);
		return { destroy: () => observer.disconnect() };
	}

	// The original's openExternalLink: asks before leaving.
	const openLink = (url: string) => openExternalLink(url);

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(reload);
		return watchLive(['visit', 'gameLog'], () => {
			if (!search.trim() && !hasDateFilter) void refreshNewest();
		}, 1000);
	});
</script>

{#snippet friendMark(member: GroupMember)}
	{#if member.isFriend}<span class="ml-1">{member.isFavorite ? '⭐' : '💚'}</span>{/if}
{/snippet}

{#snippet time(value: string)}
	<span class="min-w-22 shrink-0 text-[0.75rem] text-muted-foreground tabular-nums">{formatDateFilter(value, 'short')}</span>
{/snippet}

{#snippet kind(label: string)}
	<div class="min-w-28 shrink-0"><Badge variant="outline" class="justify-center text-muted-foreground">{label}</Badge></div>
{/snippet}

{#snippet userName(member: GroupMember)}
	{#if member.userId}<a class="cursor-pointer" href={userHref(member.userId)}>{member.displayName}</a>{:else}<button type="button" class="cursor-pointer" onclick={() => lookupUser(member)}>{member.displayName}</button>{/if}
{/snippet}

{#snippet singlePlayer(event: SessionEvent, joined: boolean)}
	<div class={['flex min-h-7 items-center gap-1.5 rounded px-2 py-0.5 text-[0.8125rem] hover:bg-muted/50', !joined && 'text-muted-foreground']}>
		{@render time(event.createdAt)}
		{@render kind(joined ? 'Player joined' : 'Player left')}
		<span class="flex min-w-0 flex-1 items-center gap-1 truncate">
			{#if joined}<LogIn class="size-3 shrink-0" />{:else}<LogOut class="size-3 shrink-0" />{/if}
			{@render userName(event)}{@render friendMark(event)}
		</span>
	</div>
{/snippet}

{#snippet video(event: SessionEvent)}
	{@const showLink = event.videoId !== 'PopcornPalace' && !!event.videoUrl}
	{@const label = event.videoName || event.videoUrl}
	<ContextMenu.Root>
		<ContextMenu.Trigger class="block">
			<div class="flex min-h-7 items-center gap-1.5 rounded px-2 py-0.5 text-[0.8125rem] hover:bg-muted/50">
				{@render time(event.createdAt)}
				{@render kind('Video play')}
				<span class="flex min-w-0 flex-1 items-center gap-1 truncate">
					<Play class="size-3 shrink-0" />
					{#if showLink}
						<button type="button" class="cursor-pointer truncate" onclick={() => openLink(event.videoUrl!)}>{label}</button>
					{:else}
						<span class="truncate">{label}</span>
					{/if}
					{#if (event.playCount ?? 1) > 1}
						<Badge variant="secondary" class="h-4 shrink-0 px-1 text-[0.625rem]">×{event.playCount}</Badge>
					{/if}
				</span>
				{#if event.displayName}<span class="shrink-0 text-[0.75rem] text-muted-foreground">{event.displayName}</span>{/if}
			</div>
		</ContextMenu.Trigger>
		<ContextMenu.Content>
			{#if showLink}
				<ContextMenu.Item onclick={() => openLink(event.videoUrl!)}><ExternalLink class="size-4" />{m.common_actions_open_link()}</ContextMenu.Item>
				<ContextMenu.Separator />
			{/if}
			<ContextMenu.Item onclick={() => navigator.clipboard.writeText(event.videoUrl || event.videoName || '')}><Copy class="size-4" />{m.view_favorite_copy()}</ContextMenu.Item>
		</ContextMenu.Content>
	</ContextMenu.Root>
{/snippet}

<div class="flex h-full min-h-0 flex-col overflow-hidden">
	<div class="flex shrink-0 items-center gap-2 border-b border-border pb-4">
		<div class="flex min-w-0 flex-1 items-center gap-2 overflow-x-auto whitespace-nowrap">
			{@render leading()}
			<FavoritesOnlyToggle class="shrink-0" pressed={vip} onPressedChange={(value) => (vip = value)} />
			<Popover.Root bind:open={dateOpen}>
				<Popover.Trigger>
					{#snippet child({ props })}
						<Button {...props} variant="outline" size="sm" class={['h-8 shrink-0 gap-1.5', hasDateFilter && 'bg-accent text-accent-foreground']} aria-label={m.view_tools_group_calendar()}>
							<CalendarRange class="size-4" />
							{#if hasDateFilter}<Badge variant="secondary" class={COUNT_BADGE}>1</Badge>{/if}
						</Button>
					{/snippet}
				</Popover.Trigger>
				<Popover.Content class="w-auto" side="bottom" align="start">
					<RangeCalendar bind:value={dateRange} locale={appearance.appLanguage.value} maxValue={todayDate} maxDays={RANGE_MAX_DAYS} numberOfMonths={2} />
					<div class="mt-3 flex justify-end gap-2">
						<Button variant="outline" size="sm" onclick={clearDateRange}>{m.view_favorite_clear()}</Button>
						<Button size="sm" onclick={applyDateRange}>{m.dialog_alertdialog_confirm()}</Button>
					</div>
				</Popover.Content>
			</Popover.Root>
			<ToggleGroup.Root type="multiple" variant="outline" size="sm" value={selection} onValueChange={setFilters} class="shrink-0 justify-start">
				<ToggleGroup.Item value="All">{m.view_search_avatar_all()}</ToggleGroup.Item>
				{#each FILTER_TYPES as type (type.value)}
					<ToggleGroup.Item value={type.value}>{type.label}</ToggleGroup.Item>
				{/each}
			</ToggleGroup.Root>
		</div>
		<Input
			class="h-8 w-60 shrink-0"
			placeholder={m.view_game_log_search_placeholder()}
			bind:value={typed}
			oninput={() => {
				if (!typed.trim()) applySearch();
			}}
			onchange={applySearch}
			onkeyup={(event) => {
				if (event.key === 'Enter') applySearch();
			}}
		/>
	</div>
	{#if loadError}<p class="mt-2 text-sm text-destructive">Couldn't load game log: {loadError}</p>{/if}

	<div class="flex-1 overflow-y-auto" onscroll={onScroll}>
		{#if loading && !resolved}
			<div class="flex flex-col gap-4 p-4">
				{#each [1, 2, 3] as i (i)}
					<div class="space-y-2">
						<Skeleton class="h-5 w-48" />
						<Skeleton class="h-3 w-24" />
						<div class="space-y-1.5 pl-2">
							<Skeleton class="h-4 w-full" />
							<Skeleton class="h-4 w-3/4" />
							<Skeleton class="h-4 w-5/6" />
						</div>
					</div>
				{/each}
			</div>
		{:else if segments.length === 0 && !loading}
			<p class="m-4 text-center text-sm text-muted-foreground">{m.view_game_log_sessions_no_data()}</p>
		{:else}
			{#each segments as segment, index (segment.id)}
				{@const count = counts(segment)}
				{@const open = !collapsed[segment.id]}
				<div class="border-b border-border last:border-b-0">
					<div
						role="button"
						tabindex="0"
						class="sticky top-0 z-5 flex w-full cursor-pointer items-center gap-2 bg-muted/80 px-3 py-2 text-left backdrop-blur-sm"
						onclick={() => (collapsed[segment.id] = open)}
						onkeydown={(event) => {
							if (event.key === 'Enter' && event.target === event.currentTarget) collapsed[segment.id] = open;
						}}
					>
						<ChevronRight class={['size-3.5 shrink-0 text-muted-foreground transition-transform duration-150', open && 'rotate-90', segment.events.length === 0 && 'pointer-events-none opacity-0']} />
						<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
						<div class="min-w-0" onclick={(event) => event.stopPropagation()}>
							<LocationMenu location={segment.location} hint={segment.worldName ?? ''} grouphint={segment.groupName ?? ''} class="min-w-0 text-sm" {onpreviousinstances} />
						</div>
						<span class="shrink-0 text-[0.6875rem] text-muted-foreground">{formatDateFilter(segment.createdAt, 'long')}</span>
						{#if segment.duration && segment.duration > 0}
							<Badge variant="outline" class="h-4 px-1 text-[0.625rem] tabular-nums">{timeToText(segment.duration)}</Badge>
						{:else if index === 0 && desktop.gameRunning}
							<Badge variant="outline" class="h-4 px-1 text-[0.625rem]">{m.view_player_list_photon_current()}</Badge>
						{/if}
						{#if segment.events.length > 0}
							<div class="ml-auto flex shrink-0 items-center gap-2 text-[0.6875rem] text-muted-foreground">
								{#if count.joined}<span class="flex items-center gap-0.5" title={m.view_game_log_filters_OnPlayerJoined()}><UserPlus class="size-3" /> {count.joined}</span>{/if}
								{#if count.left}<span class="flex items-center gap-0.5" title={m.view_game_log_filters_OnPlayerLeft()}><UserMinus class="size-3" /> {count.left}</span>{/if}
								{#if count.videos}<span class="flex items-center gap-0.5" title={m.view_game_log_filters_VideoPlay()}><Play class="size-3" /> {count.videos}</span>{/if}
							</div>
						{/if}
					</div>
					{#if open && segment.events.length > 0}
						<div class="px-1 py-1">
							{#each segment.events as event (event.key)}
								<div class="py-0.5">
									{#if isGroup(event)}
										{@const joined = event.type === 'JoinGroup'}
										<button
											type="button"
											class="flex min-h-7 w-full cursor-pointer items-center gap-1.5 rounded px-2 py-0.5 text-left text-[0.8125rem] hover:bg-muted/50"
											onclick={() => (expanded[event.key] = !expanded[event.key])}
										>
											{@render time(event.createdAt)}
											{@render kind(joined ? 'Player joined' : 'Player left')}
											<span class="flex-1 font-medium">{event.count} players {joined ? 'joined' : 'left'}</span>
											<ChevronRight class={['size-3.5 shrink-0 text-muted-foreground transition-transform duration-150', expanded[event.key] && 'rotate-90']} />
										</button>
										{#if expanded[event.key]}
											<div class="py-0.5 pb-1 pl-20">
												{#each event.members as member, memberIndex (memberIndex)}
													<div class={['flex items-center gap-1 rounded px-2 py-px text-[0.8125rem] hover:bg-muted/30', !joined && 'text-muted-foreground']}>
														{@render userName(member)}{@render friendMark(member)}
													</div>
												{/each}
											</div>
										{/if}
									{:else if event.type === 'OnPlayerJoined'}
										{@render singlePlayer(event, true)}
									{:else if event.type === 'OnPlayerLeft'}
										{@render singlePlayer(event, false)}
									{:else if event.type === 'VideoPlay'}
										{@render video(event)}
									{/if}
								</div>
							{/each}
						</div>
					{/if}
				</div>
			{/each}
			<div use:sentinel class="flex items-center justify-center py-4 pb-6 text-[0.8125rem] text-muted-foreground">
				{#if loading}
					<Spinner class="mr-2" />Load more...
				{:else if !hasMore}
					<span>{m.common_no_more()}</span>
				{/if}
			</div>
		{/if}
	</div>
</div>
