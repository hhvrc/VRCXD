<script lang="ts">
	import { readText, storeText } from '#lib/settings/settings-storage.js';
	import { TAB_PANE } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import { onDestroy, tick, untrack } from 'svelte';
	import * as echarts from 'echarts';
	import { toast } from 'svelte-sonner';
	import ImageIcon from '@lucide/svelte/icons/image';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { imageSrc, worldHref } from '#lib/api/links.js';
	import { activityNormalizeConfig, buildDailySummary, buildSessionsFromEvents, buildSessionsFromGamelog, computeActivityView, computeOverlapView, overlapNormalizeConfig, type PresenceRow, type Session, type VisitRow } from '#lib/activity/activity-engine.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { isDarkMode } from '#lib/activity/charts.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's UserDialogActivityTab: when someone is online through the week as a heatmap, how
	// much of that overlaps with your own time, and for yourself daily playtime and most visited worlds.
	let { userId, active }: { userId: string; active: boolean } = $props();

	type TopWorld = { worldId: string; worldName: string; thumbnailImageUrl?: string | null; visitCount: number; totalTime: number };
	type HeatmapView = { rawBuckets: number[]; normalizedBuckets: number[] };

	const DAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
	// Settings > Interface > Week Starts On; the heatmaps redraw when it changes.
	const WEEK_STARTS_ON = $derived(appearance.weekStartsOn.value);
	const DISPLAY_DAY_LABELS = $derived(Array.from({ length: 7 }, (_, index) => DAY_LABELS[(WEEK_STARTS_ON + index) % 7]));
	const HOUR_LABELS = Array.from({ length: 24 }, (_, index) => `${String(index).padStart(2, '0')}:00`);
	const PERIODS = [
		{ value: '0', get label() { return m.dialog_user_activity_period_all(); } },
		{ value: '365', get label() { return m.dialog_user_activity_period_365(); } },
		{ value: '180', get label() { return m.dialog_user_activity_period_180(); } },
		{ value: '90', get label() { return m.dialog_user_activity_period_90(); } },
		{ value: '30', get label() { return m.dialog_user_activity_period_30(); } },
		{ value: '7', get label() { return m.dialog_user_activity_period_7(); } }
	];
	const SORT_KEY = 'VRCX_activitySelfTopWorldsSortBy';
	const EXCLUDE_HOME_KEY = 'VRCX_activitySelfExcludeHomeWorld';
	const EXCLUDE_ENABLED_KEY = 'VRCX_overlapExcludeEnabled';
	const EXCLUDE_START_KEY = 'VRCX_overlapExcludeStart';
	const EXCLUDE_END_KEY = 'VRCX_overlapExcludeEnd';

	let period = $state('30');
	let loading = $state(false);
	let error = $state('');
	let loadedFor = $state('');
	let targetSessions = $state.raw<Session[]>([]);
	let selfSessions = $state.raw<Session[]>([]);
	let topWorlds = $state.raw<TopWorld[]>([]);
	let topWorldsLoading = $state(false);
	let topWorldsSortBy = $state<'time' | 'count'>(readText(SORT_KEY) === 'time' ? 'time' : 'count');
	let excludeHomeWorld = $state(readText(EXCLUDE_HOME_KEY) === 'true');
	let excludeHours = $state(readText(EXCLUDE_ENABLED_KEY) === 'true');
	let excludeStart = $state(readText(EXCLUDE_START_KEY) || '1');
	let excludeEnd = $state(readText(EXCLUDE_END_KEY) || '6');
	let activityEl = $state<HTMLDivElement | undefined>();
	let overlapEl = $state<HTMLDivElement | undefined>();
	let playtimeEl = $state<HTMLDivElement | undefined>();
	let hintTimer: ReturnType<typeof setTimeout> | null = null;

	const isSelf = $derived(userId === vrchatAuth.ownerUserId);
	const homeWorldId = $derived.by(() => {
		const home = sidebarData.me?.homeLocation;
		return home ? parseLocation(home).worldId || home : '';
	});
	// "All time" spans from the first session, as the original's longest cached range does.
	const rangeDays = $derived.by(() => {
		const days = Number(period);
		if (days > 0) return days;
		const sessions = isSelf ? selfSessions : targetSessions;
		const first = sessions[0]?.start;
		return first ? Math.max(1, Math.ceil((Date.now() - first) / 86400000)) : 1;
	});
	const view = $derived(
		computeActivityView({ sessions: isSelf ? selfSessions : targetSessions, dayLabels: DAY_LABELS, rangeDays, normalizeConfig: activityNormalizeConfig(rangeDays) })
	);
	const overlap = $derived(
		isSelf
			? undefined
			: computeOverlapView({
					selfSessions,
					targetSessions,
					dayLabels: DAY_LABELS,
					rangeDays,
					excludeHours: excludeHours ? { startHour: Number(excludeStart), endHour: Number(excludeEnd) } : null,
					normalizeConfig: overlapNormalizeConfig(rangeDays)
				})
	);
	const hasOverlap = $derived(!!overlap && overlap.rawBuckets.some((value) => value > 0));
	const daily = $derived(isSelf && selfSessions.length ? buildDailySummary(selfSessions, Number(period) > 0 ? Date.now() - Number(period) * 86400000 : selfSessions[0].start) : []);
	const dailyAverage = $derived(daily.length ? daily.reduce((total, d) => total + d.totalMs, 0) / daily.length : 0);
	const topWorldMax = $derived(Math.max(...topWorlds.map((w) => (topWorldsSortBy === 'count' ? w.visitCount : w.totalTime)), 0));


	async function load(force = false) {
		const id = userId;
		if (!id || (!force && loadedFor === `${id}:${period}`)) return;
		loading = true;
		error = '';
		try {
			const data = await api.get<{ events: PresenceRow[]; selfVisits: VisitRow[] }>(ownPath(`/users/${encodeURIComponent(id)}/activity`), { days: period });
			if (id !== userId) return;
			selfSessions = buildSessionsFromGamelog(data.selfVisits);
			targetSessions = id === vrchatAuth.ownerUserId ? selfSessions : buildSessionsFromEvents(data.events).sessions;
			loadedFor = `${id}:${period}`;
			if (id === vrchatAuth.ownerUserId) void loadTopWorlds();
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	async function loadTopWorlds() {
		topWorldsLoading = true;
		try {
			if (!sidebarData.me) await sidebarData.loadMe();
			topWorlds = await api.get<TopWorld[]>(ownPath('/top-worlds'), {
				days: period,
				limit: '5',
				sortBy: topWorldsSortBy,
				excludeWorldId: excludeHomeWorld ? homeWorldId : ''
			});
		} catch {
			topWorlds = [];
		} finally {
			topWorldsLoading = false;
		}
	}

	// The original's buildHeatmapOption.
	function heatmapOption(heatmap: HeatmapView, dark: boolean, scaleColors: string[], unitLabel: string): echarts.EChartsOption {
		const data: [number, number, number][] = [];
		for (let day = 0; day < 7; day++) for (let hour = 0; hour < 24; hour++) data.push([hour, (day - WEEK_STARTS_ON + 7) % 7, heatmap.normalizedBuckets[day * 24 + hour]]);
		return {
			tooltip: {
				confine: true,
				position: 'top',
				formatter: (params) => {
					const [hour, dayIndex] = (params as echarts.DefaultLabelFormatterCallbackParams).data as [number, number, number];
					const slot = ((dayIndex + WEEK_STARTS_ON) % 7) * 24 + hour;
					return `${DISPLAY_DAY_LABELS[dayIndex]} ${HOUR_LABELS[hour]}<br/><b>${Math.round(heatmap.rawBuckets[slot] || 0)}</b> ${unitLabel}`;
				}
			},
			grid: { top: 6, left: 42, right: 16, bottom: 32 },
			xAxis: { type: 'category', data: HOUR_LABELS, splitArea: { show: false }, axisLine: { show: false }, axisLabel: { interval: 2, fontSize: 10 }, axisTick: { show: false } },
			yAxis: { type: 'category', data: DISPLAY_DAY_LABELS, inverse: true, splitArea: { show: false }, axisLabel: { fontSize: 11 }, axisTick: { show: false } },
			visualMap: {
				min: 0,
				max: 1,
				calculable: false,
				show: false,
				type: 'piecewise',
				dimension: 2,
				pieces: [
					{ min: 0, max: 0, color: dark ? 'hsl(220, 15%, 12%)' : 'hsl(210, 30%, 95%)' },
					{ gt: 0, lte: 0.2, color: scaleColors[0] },
					{ gt: 0.2, lte: 0.4, color: scaleColors[1] },
					{ gt: 0.4, lte: 0.6, color: scaleColors[2] },
					{ gt: 0.6, lte: 0.8, color: scaleColors[3] },
					{ gt: 0.8, lte: 1, color: scaleColors[4] }
				]
			},
			series: [
				{
					type: 'heatmap',
					data,
					emphasis: { itemStyle: { borderColor: dark ? 'hsl(220, 15%, 18%)' : 'hsl(210, 18%, 78%)', borderWidth: 1.5, opacity: 0.92 } },
					itemStyle: { borderWidth: 1.5, borderColor: dark ? 'hsl(220, 15%, 8%)' : 'hsl(0, 0%, 100%)', borderRadius: 2 }
				}
			],
			backgroundColor: 'transparent'
		};
	}

	const GREEN_DARK = ['hsl(160, 40%, 24%)', 'hsl(150, 48%, 32%)', 'hsl(142, 55%, 38%)', 'hsl(142, 65%, 46%)', 'hsl(142, 80%, 55%)'];
	const GREEN_LIGHT = ['hsl(160, 40%, 82%)', 'hsl(155, 45%, 68%)', 'hsl(142, 55%, 55%)', 'hsl(142, 65%, 40%)', 'hsl(142, 76%, 30%)'];
	const PURPLE_DARK = ['hsl(260, 30%, 26%)', 'hsl(260, 42%, 36%)', 'hsl(260, 50%, 45%)', 'hsl(260, 60%, 54%)', 'hsl(260, 70%, 62%)'];
	const PURPLE_LIGHT = ['hsl(260, 35%, 85%)', 'hsl(260, 42%, 70%)', 'hsl(260, 48%, 58%)', 'hsl(260, 55%, 48%)', 'hsl(260, 60%, 38%)'];

	// Each chart lives with its element and is resized with it, as in the original.
	function chartOn(el: HTMLDivElement, option: echarts.EChartsOption, height: number) {
		const chart = echarts.init(el, isDarkMode() ? 'dark' : undefined, { height });
		chart.setOption(option);
		const observer = new ResizeObserver((entries) => {
			for (const entry of entries) chart.resize({ width: entry.contentRect.width });
		});
		observer.observe(el);
		return () => {
			observer.disconnect();
			chart.dispose();
		};
	}

	$effect(() => {
		if (!activityEl || !view.filteredEventCount) return;
		return chartOn(activityEl, heatmapOption(view, isDarkMode(), isDarkMode() ? GREEN_DARK : GREEN_LIGHT, 'min online'), 240);
	});

	$effect(() => {
		if (!overlapEl || !overlap || !hasOverlap) return;
		return chartOn(overlapEl, heatmapOption(overlap, isDarkMode(), isDarkMode() ? PURPLE_DARK : PURPLE_LIGHT, 'min overlap'), 240);
	});

	// The original's DailyPlaytime line chart.
	$effect(() => {
		if (!playtimeEl || !daily.length) return;
		const dates = daily.map((d) => d.date);
		const hours = daily.map((d) => +(d.totalMs / 3600000).toFixed(2));
		return chartOn(
			playtimeEl,
			{
				tooltip: {
					trigger: 'axis',
					formatter: (params) => {
						// On a category axis the point's name is its date.
						const p = (params as echarts.DefaultLabelFormatterCallbackParams[])[0];
						const value = Number(p.value);
						const hrs = Math.floor(value);
						return `<strong>${p.name}</strong><br/>${hrs}h ${Math.round((value - hrs) * 60)}m`;
					}
				},
				grid: { top: 20, left: 45, right: 15, bottom: dates.length > 60 ? 55 : 25 },
				xAxis: { type: 'category', data: dates, axisLabel: { rotate: 0, hideOverlap: true, fontSize: 10 }, boundaryGap: false },
				yAxis: { type: 'value', max: 24, interval: 6, axisLabel: { formatter: (v: number) => `${v}h`, fontSize: 10 }, splitLine: { lineStyle: { type: 'dashed' } } },
				dataZoom: dates.length > 60 ? [{ type: 'slider', start: 0, end: 100, height: 20, bottom: 5 }, { type: 'inside', start: 0, end: 100 }] : undefined,
				series: [{ name: 'Playtime', type: 'line', data: hours, smooth: true, symbol: 'none', lineStyle: { width: 2 }, areaStyle: { opacity: 0.3 }, emphasis: { focus: 'series' } }],
				backgroundColor: 'transparent'
			},
			200
		);
	});

	// Load the first time the tab is shown for a user, and again when the period changes.
	$effect(() => {
		if (!active || !userId) return;
		void period;
		untrack(() => void load());
	});

	$effect(() => {
		void userId;
		untrack(() => {
			loadedFor = '';
			targetSessions = [];
			selfSessions = [];
			topWorlds = [];
		});
	});

	function formatHours(ms: number): string {
		const hours = ms / 3600000;
		return hours < 1 ? `${Math.round(ms / 60000)}m` : `${hours.toFixed(1)}h`;
	}

	function onChartRightClick(event: MouseEvent) {
		event.preventDefault();
		toast(m.dialog_user_activity_chart_hint(), { position: 'bottom-center' });
		if (hintTimer) clearTimeout(hintTimer);
		hintTimer = setTimeout(() => (hintTimer = null), 5000);
	}

	function onOverlapRightClick(event: MouseEvent) {
		event.preventDefault();
		if (hintTimer) toast("You can't farm this.", { position: 'bottom-center' });
	}

	onDestroy(() => {
		if (hintTimer) clearTimeout(hintTimer);
	});

	async function setSort(value: string) {
		topWorldsSortBy = value === 'time' ? 'time' : 'count';
		storeText(SORT_KEY, topWorldsSortBy);
		await tick();
		void loadTopWorlds();
	}
</script>

<div class="min-w-0 overflow-hidden {TAB_PANE}">
	<div class="flex items-center justify-between">
		<div class="flex items-center">
			<Button class="rounded-full" variant="ghost" size="icon-sm" disabled={loading} title={m.dialog_user_activity_refresh_hint()} aria-label={m.dialog_user_activity_refresh_hint()} onclick={() => load(true)}>
				{#if loading}<Spinner />{:else}<RefreshCw />{/if}
			</Button>
			{#if view.filteredEventCount > 0}
				<span class="ml-1 text-sm text-accent-foreground">{view.filteredEventCount} online events</span>
			{/if}
		</div>
		<div class="flex items-center gap-2">
			<span class="text-sm text-muted-foreground">{m.dialog_user_activity_period()}</span>
			<Select.Root type="single" bind:value={period} disabled={loading}>
				<Select.Trigger size="sm" class="w-40">{PERIODS.find((p) => p.value === period)?.label}</Select.Trigger>
				<Select.Content>
					{#each PERIODS as option (option.value)}
						<Select.Item value={option.value} label={option.label} />
					{/each}
				</Select.Content>
			</Select.Root>
		</div>
	</div>

	<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto">
		{#if error}<p class="mt-2 text-sm text-destructive">{error}</p>{/if}
		{#if view.peakDay || view.peakTime}
			<div class="mt-2 mb-1 flex gap-4 text-sm">
				{#if view.peakDay}<div><span class="text-muted-foreground">{m.dialog_user_activity_most_active_day()}</span><span class="ml-1 font-medium">{view.peakDay}</span></div>{/if}
				{#if view.peakTime}<div><span class="text-muted-foreground">{m.dialog_user_activity_most_active_time()}</span><span class="ml-1 font-medium">{view.peakTime}</span></div>{/if}
			</div>
		{/if}
		{#if loading && view.filteredEventCount === 0}
			<div class="my-8 flex flex-1 flex-col items-center justify-center gap-2">
				<Spinner class="h-5 w-5" />
				<span class="text-sm text-muted-foreground">{m.dialog_user_activity_preparing_data()}</span>
				<span class="text-xs text-muted-foreground">{m.dialog_user_activity_preparing_data_hint()}</span>
			</div>
		{:else if !loading && view.filteredEventCount === 0}
			<div class="my-8 flex flex-1 items-center justify-center"><span class="text-sm text-muted-foreground">{m.dialog_user_activity_no_data_in_period()}</span></div>
		{/if}
		{#if view.filteredEventCount > 0}
			<div bind:this={activityEl} class="min-w-0" style="width: 100%; height: 240px" role="img" aria-label="Online activity heatmap" oncontextmenu={onChartRightClick}></div>
		{/if}

		{#if isSelf && daily.length}
			<div class="mt-4 border-t border-border pt-3">
				<div class="mb-2 flex items-center justify-between">
					<span class="text-sm font-medium">{m.view_charts_playtime_trend_header()}</span>
					<span class="text-sm text-muted-foreground">Daily Average: <strong class="text-foreground">{formatHours(dailyAverage)}</strong></span>
				</div>
				<div bind:this={playtimeEl} style="width: 100%; height: 200px"></div>
			</div>
		{/if}

		{#if !isSelf && view.filteredEventCount > 0}
			<div class="mt-4 border-t border-border pt-3">
				<div class="mb-2 flex items-center justify-between">
					<span class="text-sm font-medium">{m.dialog_user_activity_overlap_header()}</span>
					<div class="flex shrink-0 items-center gap-1.5">
						<Switch checked={excludeHours} class="scale-75" onCheckedChange={(value) => { excludeHours = value; storeText(EXCLUDE_ENABLED_KEY, String(value)); }} />
						<span class="text-sm whitespace-nowrap text-muted-foreground">{m.dialog_user_activity_overlap_exclude_hours()}</span>
						<Select.Root type="single" bind:value={() => excludeStart, (value) => { excludeStart = value; storeText(EXCLUDE_START_KEY, value); }}>
							<Select.Trigger size="sm" class="h-6 w-19.5 px-2 text-sm">{excludeStart.padStart(2, '0')}:00</Select.Trigger>
							<Select.Content>
								{#each HOUR_LABELS as label, hour (label)}<Select.Item value={String(hour)} {label} />{/each}
							</Select.Content>
						</Select.Root>
						<span class="text-xs text-muted-foreground">–</span>
						<Select.Root type="single" bind:value={() => excludeEnd, (value) => { excludeEnd = value; storeText(EXCLUDE_END_KEY, value); }}>
							<Select.Trigger size="sm" class="h-6 w-19.5 px-2 text-sm">{excludeEnd.padStart(2, '0')}:00</Select.Trigger>
							<Select.Content>
								{#each HOUR_LABELS as label, hour (label)}<Select.Item value={String(hour)} {label} />{/each}
							</Select.Content>
						</Select.Root>
					</div>
				</div>
				{#if overlap && hasOverlap}
					<div class="mb-2 flex flex-col gap-1">
						<div class="flex items-center gap-2">
							<span class={['text-sm font-medium', overlap.overlapPercent > 0 ? 'text-accent-foreground' : 'text-muted-foreground']}>{overlap.overlapPercent}%</span>
							<div class="h-2 flex-1 overflow-hidden rounded-full bg-muted">
								<div class="h-full rounded-full transition-all duration-500" style="width: {overlap.overlapPercent}%; background-color: {isDarkMode() ? 'hsl(260, 60%, 55%)' : 'hsl(260, 55%, 50%)'}"></div>
							</div>
						</div>
						{#if overlap.bestOverlapTime}
							<div class="text-sm"><span class="text-muted-foreground">{m.dialog_user_activity_overlap_peak_overlap()}</span><span class="ml-1 font-medium">{overlap.bestOverlapTime}</span></div>
						{/if}
					</div>
					<div bind:this={overlapEl} class="min-w-0" style="width: 100%; height: 240px" role="img" aria-label="Online overlap heatmap" oncontextmenu={onOverlapRightClick}></div>
				{:else if !loading}
					<div class="my-8 flex items-center justify-center text-sm text-muted-foreground">{m.dialog_user_activity_overlap_no_data()}</div>
				{/if}
			</div>
		{/if}

		{#if isSelf && view.filteredEventCount > 0}
			<div class="mt-4 border-t border-border pt-3">
				<div class="mb-2 flex items-center justify-between">
					<div class="flex items-center gap-2">
						<span class="text-sm font-medium">{m.dialog_user_activity_most_visited_worlds_header()}</span>
						{#if topWorldsLoading}<Spinner class="h-3.5 w-3.5" />{/if}
					</div>
					<div class="flex items-center gap-4">
						{#if homeWorldId}
							<div class="flex items-center gap-1.5 text-sm text-muted-foreground">
								<Switch checked={excludeHomeWorld} class="scale-75" onCheckedChange={(value) => { excludeHomeWorld = value; storeText(EXCLUDE_HOME_KEY, String(value)); void loadTopWorlds(); }} />
								<span class="whitespace-nowrap">{m.dialog_user_activity_most_visited_worlds_exclude_home_world()}</span>
							</div>
						{/if}
						{#if topWorlds.length > 0}
							<div class="flex items-center gap-2">
								<span class="text-sm text-muted-foreground">{m.side_panel_settings_sort_primary()}</span>
								<Select.Root type="single" value={topWorldsSortBy} onValueChange={setSort} disabled={topWorldsLoading}>
									<Select.Trigger size="sm" class="w-32">{topWorldsSortBy === 'time' ? 'By Time' : 'By Visits'}</Select.Trigger>
									<Select.Content>
										<Select.Item value="time" label={m.dialog_user_activity_most_visited_worlds_sort_by_time()} />
										<Select.Item value="count" label={m.dialog_user_activity_most_visited_worlds_sort_by_count()} />
									</Select.Content>
								</Select.Root>
							</div>
						{/if}
					</div>
				</div>
				{#if topWorldsLoading && topWorlds.length === 0}
					<div class="flex items-center gap-2 py-2 text-sm text-muted-foreground"><Spinner class="h-4 w-4" /><span>{m.dialog_user_activity_most_visited_worlds_loading()}</span></div>
				{:else if topWorlds.length === 0 && !loading && !topWorldsLoading}
					<div class="py-2 text-sm text-muted-foreground">{m.dialog_user_activity_no_data_in_period()}</div>
				{:else}
					<div class="flex flex-col gap-0.5">
						{#each topWorlds as world, index (world.worldId)}
							{@const value = topWorldsSortBy === 'time' ? world.totalTime : world.visitCount}
							<a href={worldHref(world.worldId)} class={['group flex w-full cursor-pointer items-start gap-3 rounded-lg px-3 py-2 text-left transition-colors hover:bg-accent/50', index === 0 && 'bg-primary/4']}>
								<span class={['mt-1 w-5 shrink-0 text-right font-mono text-xs font-bold', index === 0 ? 'text-primary' : 'text-muted-foreground']}>#{index + 1}</span>
								<Avatar.Root class="mt-0.5 size-8 shrink-0 rounded-sm">
									{#if world.thumbnailImageUrl}<Avatar.Image src={imageSrc(world.thumbnailImageUrl.replace('256', '128')) ?? undefined} loading="lazy" decoding="async" class="rounded-sm object-cover" />{/if}
									<Avatar.Fallback class="rounded-sm"><ImageIcon class="size-3.5 text-muted-foreground" /></Avatar.Fallback>
								</Avatar.Root>
								<div class="min-w-0 flex-1">
									<div class="flex items-baseline justify-between gap-2">
										<span class="truncate text-sm font-medium">{world.worldName}</span>
										<span class="shrink-0 text-xs text-muted-foreground tabular-nums">
											{topWorldsSortBy === 'time' ? (world.totalTime > 0 ? timeToText(world.totalTime) : '0m') : `${world.visitCount} visits`}
										</span>
									</div>
									<div class="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-black/6 dark:bg-white/8">
										<div class="h-full rounded-full bg-black/25 transition-all duration-500 dark:bg-white/45" style="width: {topWorldMax > 0 ? Math.max((value / topWorldMax) * 100, 8) : 0}%"></div>
									</div>
								</div>
							</a>
						{/each}
					</div>
				{/if}
			</div>
		{/if}
	</div>
</div>
