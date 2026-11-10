<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import ChevronLeft from '@lucide/svelte/icons/chevron-left';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { formatDateFilter, replaceBioSymbols } from '#lib/helpers/format.js';
	import type { CalendarEvent, CalendarGroup, CalendarResponse } from '#lib/nav/tools.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import CalendarEventCard from './calendar-event-card.svelte';
	import { groupEventEdit, groupEvents } from '#lib/groups/group-dialog.svelte.js';

	// The original's GroupCalendarDialog and GroupCalendarMonth: a day's events beside a month grid
	// with event counts, or a searchable list per group, and an optional featured events layer.

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const FEATURED_KEY = 'vrcx.groupCalendarShowFeaturedEvents';

	let calendar = $state<CalendarEvent[]>([]);
	let following = $state<CalendarEvent[]>([]);
	let featured = $state<CalendarEvent[]>([]);
	let groups = $state<Record<string, CalendarGroup>>({});
	let selectedDay = $state(new Date());
	let isLoading = $state(false);
	let viewMode = $state<'timeline' | 'grid'>('timeline');
	let search = $state('');
	let collapsed = $state<Record<string, boolean>>({});
	let showFeatured = $state(readSetting<boolean>(FEATURED_KEY, false) === true);
	let message = $state('');
	// Group id to the permissions you hold there, for the edit pencil (the original's cachedGroups).
	let permissions = $state<Record<string, string[]>>({});


	const pad = (n: number) => String(n).padStart(2, '0');
	const dateKey = (date: Date | string) => {
		const d = new Date(date);
		return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
	};
	// VRChat wants the viewer's local time here, written with a Z.
	const queryDate = (d: Date) => `${dateKey(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}Z`;
	const sameMonth = (iso: string) => {
		const d = new Date(iso);
		return d.getMonth() === selectedDay.getMonth() && d.getFullYear() === selectedDay.getFullYear();
	};
	const byStart = (a: CalendarEvent, b: CalendarEvent) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime();
	const clean = (event: CalendarEvent): CalendarEvent => ({ ...event, title: replaceBioSymbols(event.title), description: replaceBioSymbols(event.description) });

	async function fetchScope(scope: string): Promise<CalendarEvent[]> {
		const response = await api.get<CalendarResponse>(ownPath(`/calendar/${scope}`), { date: queryDate(selectedDay) });
		groups = { ...groups, ...response.groups };
		return response.events.map(clean);
	}

	// Only the latest load is applied: a slower answer for a month already left must not replace the
	// current one (upstream issue 1341).
	let loadSeq = 0;
	async function load() {
		const seq = ++loadSeq;
		isLoading = true;
		message = '';
		try {
			const [all, followed, featuredEvents] = await Promise.all([fetchScope('all'), fetchScope('following'), showFeatured ? fetchScope('featured') : Promise.resolve([])]);
			if (seq !== loadSeq) return;
			calendar = all;
			following = followed;
			featured = featuredEvents;
		} catch (err) {
			if (seq === loadSeq) message = ApiError.from(err).message;
		} finally {
			if (seq === loadSeq) isLoading = false;
		}
	}

	$effect(() => {
		if (open) {
			untrack(() => {
				selectedDay = new Date();
				load();
				api.get<{ groups: { id: string; permissions?: string[] }[] }>(ownPath('/instances/options'))
					.then((options) => (permissions = Object.fromEntries(options.groups.map((g) => [g.id, g.permissions ?? []]))))
					.catch(() => {});
			});
		}
	});

	// An event saved or deleted in the edit dialog reloads the calendar.
	$effect(() => {
		if (groupEvents.revision && untrack(() => open)) untrack(load);
	});

	const canEdit = (event: CalendarEvent) => !!permissions[event.ownerId]?.some((p) => p === '*' || p === 'group-calendar-manage');
	const edit = (event: CalendarEvent) => groupEventEdit.editById(vrchatAuth.ownerUserId, event.ownerId, event.id);

	function selectDay(day: Date) {
		const monthChanged = day.getMonth() !== selectedDay.getMonth() || day.getFullYear() !== selectedDay.getFullYear();
		selectedDay = day;
		if (monthChanged) load();
	}

	function toggleFeatured(value: boolean) {
		showFeatured = value;
		storeSetting(FEATURED_KEY, value);
		load();
	}

	const allEvents = $derived(showFeatured ? [...calendar, ...featured] : calendar);
	const followingIds = $derived(new Set(following.map((event) => event.id)));

	const eventsByDate = $derived.by(() => {
		const result: Record<string, CalendarEvent[]> = {};
		for (const event of allEvents) (result[dateKey(event.startsAt)] ??= []).push(event);
		for (const events of Object.values(result)) events.sort(byStart);
		return result;
	});

	const followingDays = $derived(new Set(calendar.filter((event) => followingIds.has(event.id)).map((event) => dateKey(event.startsAt))));

	const timeline = $derived.by(() => {
		const groupsByTime = new Map<string, CalendarEvent[]>();
		for (const event of eventsByDate[dateKey(selectedDay)] ?? []) {
			const key = formatDateFilter(event.startsAt, 'time');
			groupsByTime.set(key, [...(groupsByTime.get(key) ?? []), event]);
		}
		return [...groupsByTime].map(([startTime, events]) => ({ startTime, events, startsAt: events[0].startsAt }));
	});

	const eventsByGroup = $derived.by(() => {
		const map = new Map<string, CalendarEvent[]>();
		for (const event of allEvents.filter((e) => sameMonth(e.startsAt))) map.set(event.ownerId, [...(map.get(event.ownerId) ?? []), event]);
		return [...map].map(([groupId, events]) => ({ groupId, groupName: groups[groupId]?.name ?? '', events: [...events].sort(byStart) }));
	});

	const filteredGroups = $derived.by(() => {
		const query = search.trim().toLowerCase();
		if (!query) return eventsByGroup;
		return eventsByGroup.filter((group) => group.groupName.toLowerCase().includes(query) || group.events.some((e) => e.title?.toLowerCase().includes(query) || e.description?.toLowerCase().includes(query)));
	});

	const monthDays = $derived.by(() => {
		const first = new Date(selectedDay.getFullYear(), selectedDay.getMonth(), 1);
		const start = new Date(first);
		start.setDate(1 - first.getDay());
		return Array.from({ length: 42 }, (_, i) => new Date(start.getFullYear(), start.getMonth(), start.getDate() + i));
	});

	const shiftMonth = (by: number) => selectDay(new Date(selectedDay.getFullYear(), selectedDay.getMonth() + by, 1));

	async function follow(event: CalendarEvent) {
		try {
			const updated = await api.post<CalendarEvent>(ownPath(`/calendar/${encodeURIComponent(event.ownerId)}/${encodeURIComponent(event.id)}/follow`), { isFollowing: !followingIds.has(event.id) });
			following = following.filter((e) => e.id !== updated.id);
			if (updated.userInterest?.isFollowing) following = [...following, clean(updated)];
		} catch (err) {
			message = ApiError.from(err).message;
		}
	}

	const weekday = (iso: string) => new Date(iso).toLocaleDateString(undefined, { month: '2-digit', day: '2-digit', weekday: 'short' });
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="flex h-[70vh] flex-col overflow-hidden sm:max-w-[50vw]">
		<Dialog.Header class="flex-row items-center justify-between pr-8">
			<Dialog.Title>{m.dialog_group_calendar_header()}</Dialog.Title>
			<div class="flex items-center gap-2 text-sm">
				<span>{m.dialog_group_calendar_featured_events()}</span>
				<Switch checked={showFeatured} onCheckedChange={toggleFeatured} class="mr-2" />
				<Button size="sm" variant="outline" onclick={() => (viewMode = viewMode === 'timeline' ? 'grid' : 'timeline')}>{viewMode === 'timeline' ? 'List View' : 'Calendar View'}</Button>
			</div>
		</Dialog.Header>
		{#if message}<p class="text-xs text-muted-foreground">{message}</p>{/if}

		{#if viewMode === 'timeline'}
			<div class="flex min-h-0 flex-1 gap-4">
				<div class="min-w-0 flex-1 overflow-y-auto pr-2">
					{#if timeline.length}
						{#each timeline as group (group.startTime)}
							<div class="mb-4">
								<div class="mb-2 text-xs font-medium text-muted-foreground">{weekday(group.startsAt)} {group.startTime}</div>
								<div class="flex flex-wrap gap-2">
									{#each group.events as event (event.id)}
										<CalendarEventCard {event} group={groups[event.ownerId]} mode="timeline" isFollowing={followingIds.has(event.id)} onfollow={follow} onmessage={(m) => (message = m)} onedit={canEdit(event) ? edit : undefined} />
									{/each}
								</div>
							</div>
						{/each}
					{:else}
						<p class="py-10 text-center text-sm text-muted-foreground">{m.dialog_group_calendar_no_events()}</p>
					{/if}
				</div>
				<div class="w-96 flex-none {isLoading ? 'opacity-60' : ''}">
					<div class="mb-3 flex items-center justify-between">
						<Button size="icon-sm" variant="outline" aria-label="Previous month" onclick={() => shiftMonth(-1)}><ChevronLeft class="size-4" /></Button>
						<div class="text-base">{selectedDay.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}</div>
						<Button size="icon-sm" variant="outline" aria-label="Next month" onclick={() => shiftMonth(1)}><ChevronRight class="size-4" /></Button>
					</div>
					<div class="grid grid-cols-7 gap-1 text-center">
						{#each ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'] as day (day)}
							<div class="text-sm text-muted-foreground">{day}</div>
						{/each}
						{#each monthDays as day (day.getTime())}
							{@const key = dateKey(day)}
							{@const count = eventsByDate[key]?.length ?? 0}
							<button
								type="button"
								class="relative flex size-12 cursor-pointer items-center justify-center rounded-md text-sm hover:bg-accent {day.getMonth() !== selectedDay.getMonth() ? 'text-muted-foreground/50' : ''} {key === dateKey(selectedDay) ? 'bg-primary text-primary-foreground hover:bg-primary' : ''} {count > 0 ? 'font-semibold' : ''}"
								onclick={() => selectDay(day)}
							>
								{day.getDate()}
								{#if count > 0}
									<span class="absolute top-0.5 right-0.5 min-w-3.5 rounded-full px-1 text-3xs leading-3.5 {followingDays.has(key) ? 'bg-primary text-primary-foreground ring-1 ring-background' : 'bg-foreground text-accent'}">{count}</span>
								{/if}
							</button>
						{/each}
					</div>
				</div>
			</div>
		{:else}
			<div class="flex min-h-0 flex-1 flex-col gap-3">
				<div class="flex items-center gap-2">
					<Button size="icon-sm" variant="outline" aria-label="Previous month" onclick={() => shiftMonth(-1)}><ChevronLeft class="size-4" /></Button>
					<span class="text-sm">{selectedDay.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}</span>
					<Button size="icon-sm" variant="outline" aria-label="Next month" onclick={() => shiftMonth(1)}><ChevronRight class="size-4" /></Button>
					<Input bind:value={search} placeholder={m.dialog_group_calendar_search_placeholder()} class="ml-auto h-8 w-64" />
				</div>
				<div class="min-h-0 flex-1 overflow-y-auto">
					{#if filteredGroups.length}
						{#each filteredGroups as group (group.groupId)}
							{@const isCollapsed = !search.trim() && collapsed[group.groupId]}
							<div class="mb-4">
								<button type="button" class="mb-2 flex cursor-pointer items-center gap-1 text-sm font-medium" onclick={() => (collapsed[group.groupId] = !collapsed[group.groupId])}>
									<ChevronDown class="size-4 transition-transform {isCollapsed ? '-rotate-90' : ''}" />{group.groupName}
								</button>
								{#if !isCollapsed}
									<div class="flex flex-wrap gap-2">
										{#each group.events as event (event.id)}
											<CalendarEventCard {event} group={groups[event.ownerId]} mode="grid" isFollowing={followingIds.has(event.id)} onfollow={follow} onmessage={(m) => (message = m)} onedit={canEdit(event) ? edit : undefined} />
										{/each}
									</div>
								{/if}
							</div>
						{/each}
					{:else}
						<p class="py-10 text-center text-sm text-muted-foreground">{search ? 'No matching events found' : 'No events this month'}</p>
					{/if}
				</div>
			</div>
		{/if}
	</Dialog.Content>
</Dialog.Root>

