<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import { onMount, tick, untrack } from 'svelte';
	import * as echarts from 'echarts';
	import { CalendarDate, getLocalTimeZone, today, type DateValue } from '@internationalized/date';
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import CalendarIcon from '@lucide/svelte/icons/calendar';
	import Info from '@lucide/svelte/icons/info';
	import RefreshCcw from '@lucide/svelte/icons/refresh-ccw';
	import Settings from '@lucide/svelte/icons/settings';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Calendar } from '#lib/components/ui/calendar/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import InstanceActivityDetail, { type DetailEntry } from '#lib/components/charts/instance-activity-detail.svelte';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import type { ActivityVisit, InstanceActivity } from '#lib/api/types.js';
	import { activityTooltip, clockFromMidnight, dayKey, endOfDay, isDarkMode, startOfDay, truncateName } from '#lib/activity/charts.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Instance Activity chart: one day of the account's game log as a bar per
	// instance along a 24 h axis, the day's online time above it, and below (when "Show
	// Detail" is on) one chart per instance with every player's stays. Data comes from
	// GET /1/accounts/{id}/activity and .../activity/dates.

	const SETTINGS = {
		barWidth: 'VRCX_InstanceActivityBarWidth',
		detail: 'VRCX_InstanceActivityDetailVisible',
		solo: 'VRCX_InstanceActivitySoloInstanceVisible',
		noFriend: 'VRCX_InstanceActivityNoFriendInstanceVisible'
	};

	let barWidth = $state(readSetting(SETTINGS.barWidth, 25));
	let barWidthDraft = $state([readSetting(SETTINGS.barWidth, 25)]);
	let isDetailVisible = $state(readSetting(SETTINGS.detail, true));
	let isSoloInstanceVisible = $state(readSetting(SETTINGS.solo, true));
	let isNoFriendInstanceVisible = $state(readSetting(SETTINGS.noFriend, true));

	let selectedDate = $state(new Date());
	let allDates = $state<Set<string>>(new Set());
	let activityData = $state<DetailEntry[]>([]);
	let activityDetailData = $state<DetailEntry[][]>([]);
	let isLoading = $state(true);
	let loadError = $state('');
	let isDatePickerOpen = $state(false);

	let chartEl = $state<HTMLDivElement | null>(null);
	let chart: echarts.ECharts | null = null;
	let detailRefs = $state<(InstanceActivityDetail | undefined)[]>([]);

	const sortedDates = $derived(Array.from(allDates).sort((a, b) => a < b ? 1 : -1));
	const isNextDayBtnDisabled = $derived(sortedDates.length === 0 || dayKey(selectedDate) >= sortedDates[0]);
	const isPrevDayBtnDisabled = $derived(sortedDates.length === 0 || dayKey(selectedDate) === sortedDates[sortedDates.length - 1]);

	const totalOnlineTime = $derived.by(() => {
		const midnight = startOfDay(selectedDate).getTime();
		return activityData.reduce((acc, item, idx) => {
			if (idx === 0 && midnight > item.joinTime.getTime()) return item.leaveTime.getTime() - midnight;
			return acc + item.time;
		}, 0);
	});

	const filteredActivityDetailData = $derived.by(() => {
		if (!isDetailVisible) return [];
		let result = [...activityDetailData];
		if (!isSoloInstanceVisible) result = result.filter((arr) => arr.length > 1);
		if (!isNoFriendInstanceVisible) result = result.filter((arr) => arr.length === 1 || arr.some((item) => item.isFriend));
		return result;
	});

	function toEntry(v: ActivityVisit): DetailEntry {
		return {
			id: v.id,
			userId: v.userId,
			displayName: v.displayName,
			location: v.location,
			worldName: v.worldName,
			joinTime: new Date(v.joinedAt),
			leaveTime: new Date(v.leftAt),
			time: v.time < 0 ? 0 : v.time,
			isFriend: v.isFriend,
			isFavorite: v.isFavorite
		};
	}

	function sortEntries(arr: DetailEntry[]): DetailEntry[] {
		return [...arr].sort((a, b) => {
			const diff = Math.abs(a.joinTime.getTime() - b.joinTime.getTime());
			// recording delay: under 3 s counts as the same join, order by leave instead
			return diff < 3000 ? a.leaveTime.getTime() - b.leaveTime.getTime() : a.joinTime.getTime() - b.joinTime.getTime();
		});
	}

	// The original's handleSplitActivityDetailData: an instance the account visited twice in a
	// day is split into separate groups of overlapping stays.
	function splitDetail(groups: DetailEntry[][], accountId: string): DetailEntry[][] {
		const out: DetailEntry[][] = [];
		for (const group of groups) {
			const own = group.filter((e) => e.userId === accountId).length;
			if (own <= 1) {
				out.push(group);
				continue;
			}
			const n = group.length;
			const adj: number[][] = Array.from({ length: n }, () => []);
			const overlaps = (a: DetailEntry, b: DetailEntry) => !(a.leaveTime.getTime() < b.joinTime.getTime() - 999 || b.leaveTime.getTime() < a.joinTime.getTime() - 999);
			for (let i = 0; i < n; i++) for (let j = i + 1; j < n; j++) if (overlaps(group[i], group[j])) (adj[i].push(j), adj[j].push(i));
			const visited = new Array(n).fill(false);
			for (let i = 0; i < n; i++) {
				if (visited[i]) continue;
				const component: number[] = [];
				const stack = [i];
				visited[i] = true;
				while (stack.length) {
					const k = stack.pop()!;
					component.push(k);
					for (const m of adj[k]) if (!visited[m]) (visited[m] = true, stack.push(m));
				}
				out.push(component.map((idx) => group[idx]));
			}
		}
		return out.sort((a, b) => a[0].joinTime.getTime() - b[0].joinTime.getTime());
	}

	async function loadDates() {
		if (!vrchatAuth.ownerUserId) return;
		try {
			const stamps = await api.get<string[]>(ownPath('/activity/dates'));
			allDates = new Set(stamps.map((s) => dayKey(new Date(s))));
		} catch {
			// dates only drive the navigation buttons
		}
	}

	async function loadActivity() {
		const accountId = vrchatAuth.ownerUserId;
		if (!accountId) return;
		const data = await api.get<InstanceActivity>(accountPath(accountId, '/activity'), {
			from: startOfDay(selectedDate).toISOString(),
			to: endOfDay(selectedDate).toISOString()
		});
		activityData = data.currentUserData.map(toEntry);
		const locationSet = new Set(activityData.map((e) => e.location));
		const pre = Object.values(data.detailData)
			.map((arr) => sortEntries(arr.map(toEntry)))
			.filter((arr) => arr.every((e) => locationSet.has(e.location)));
		activityDetailData = splitDetail(pre, accountId);
	}

	async function reloadData() {
		isLoading = true;
		loadError = '';
		try {
			await loadActivity();
			await loadDates();
			await tick();
			drawChart();
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			isLoading = false;
		}
	}

	function findMatchingDetail(item: DetailEntry): DetailEntry[] | undefined {
		return activityDetailData.find((arr) => arr[0]?.location === item.location && arr.find((e) => e.userId === vrchatAuth.ownerUserId)?.joinTime.getTime() === item.joinTime.getTime());
	}

	function isDetailFiltered(detail: DetailEntry[] | undefined): boolean {
		if (!detail) return false;
		if (!isSoloInstanceVisible && detail.length <= 1) return true;
		if (!isNoFriendInstanceVisible && detail.length > 1 && !detail.some((e) => e.isFriend)) return true;
		return false;
	}

	function yAxisData(): string[] {
		return activityData.map((item) => {
			const name = truncateName(item.worldName || parseLocation(item.location).worldId);
			const filtered = isDetailVisible && isDetailFiltered(findMatchingDetail(item));
			return filtered ? `{filtered|${name}}` : `{normal|${name}}`;
		});
	}

	function buildOption(): echarts.EChartsOption {
		const midnight = startOfDay(selectedDate).getTime();
		return {
			tooltip: {
				trigger: 'axis',
				axisPointer: { type: 'shadow' },
				formatter: (params: unknown) => {
					const param = (params as { dataIndex: number; name: string; color: string; data: number }[])[1];
					const item = activityData[param?.dataIndex];
					if (!item) return '';
					const L = parseLocation(item.location);
					let name = param.name;
					name = name.endsWith('}') ? name.slice(0, -1) : name;
					name = name.replaceAll('{filtered|', '').replaceAll('{normal|', '');
					return activityTooltip({
						color: param.color,
						displayName: `${name} #${L.instanceName}`,
						icon: L.accessTypeName,
						joinTime: item.joinTime,
						leaveTime: item.leaveTime,
						durationMs: param.data
					});
				}
			},
			grid: { top: 50, left: 160, right: 90 },
			yAxis: {
				type: 'category',
				axisLabel: { interval: 0, rich: { filtered: { opacity: 0.4 }, normal: { opacity: 1 } } },
				inverse: true,
				data: yAxisData(),
				triggerEvent: true
			},
			xAxis: {
				type: 'value',
				min: 0,
				max: 24 * 60 * 60 * 1000,
				interval: 3 * 60 * 60 * 1000,
				axisLine: { show: true },
				axisLabel: { formatter: (value: number) => clockFromMidnight(value) },
				splitLine: { lineStyle: { type: 'dashed' } }
			},
			series: [
				{
					name: 'Placeholder',
					type: 'bar',
					stack: 'Total',
					itemStyle: { borderColor: 'transparent', color: 'transparent' },
					emphasis: {
						itemStyle: { borderColor: 'transparent', color: 'transparent' }
					},
					data: activityData.map((item, idx) => idx === 0 && midnight > item.joinTime.getTime() ? 0 : item.joinTime.getTime() - midnight)
				},

				{
					name: m.table_previous_instances_time(),
					type: 'bar',
					stack: 'Total',
					colorBy: 'data',
					barWidth,
					emphasis: { focus: 'self' },
					itemStyle: {
						borderRadius: 2,
						shadowBlur: 2,
						shadowOffsetX: 0.7,
						shadowOffsetY: 0.5
					},
					data: activityData.map((item, idx) => idx === 0 && midnight > item.joinTime.getTime() ? item.leaveTime.getTime() - midnight : item.time)
				}
			],
			backgroundColor: 'transparent'
		};
	}

	function handleYAxisLabelClick(params: unknown) {
		const target = activityData[(params as { dataIndex: number }).dataIndex];
		if (!target) return;
		const idx = filteredActivityDetailData.findIndex((arr) => arr[0]?.location === target.location && arr.find((e) => e.userId === vrchatAuth.ownerUserId)?.joinTime.getTime() === target.joinTime.getTime());
		if (idx === -1) return;
		detailRefs[idx]?.scrollIntoView();
	}

	function drawChart(recreate = false) {
		if (!chartEl) return;
		const height = activityData.length * (barWidth + 10) + 200;
		if (chart && recreate) {
			chart.dispose();
			chart = null;
		}
		if (!chart) {
			chart = echarts.init(chartEl, isDarkMode() ? 'dark' : undefined, { height });
			chart.on('click', 'yAxis', handleYAxisLabelClick);
		}
		chart.resize({ height, animation: { duration: 300 } });
		if (activityData.length) {
			chart.clear();
			chart.setOption(buildOption(), { notMerge: true });
		} else {
			chart.clear();
		}
	}

	function handleSettingsChange() {
		if (chart) chart.setOption({ yAxis: buildOption().yAxis });
		for (const ref of detailRefs) ref?.rerender();
	}

	function changeSelectedDateFromBtn(isNext: boolean) {
		if (sortedDates.length === 0) return;
		const key = dayKey(selectedDate);
		const idx = sortedDates.findIndex((d) => d === key);
		if (idx === -1 && !isNext) {
			const newIdx = sortedDates.findIndex((d) => d < key);
			if (newIdx !== -1) {
				selectedDate = new Date(`${sortedDates[newIdx]}T00:00:00`);
				reloadData();
				return;
			}
		}
		if (idx !== -1) {
			const newIdx = isNext ? idx - 1 : idx + 1;
			if (newIdx >= 0 && newIdx < sortedDates.length) {
				selectedDate = new Date(`${sortedDates[newIdx]}T00:00:00`);
				reloadData();
				return;
			}
		}
		selectedDate = new Date(`${isNext ? sortedDates[0] : sortedDates[sortedDates.length - 1]}T00:00:00`);
		reloadData();
	}

	function isCalendarDateDisabled(value: DateValue): boolean {
		const date = value.toDate(getLocalTimeZone());
		if (date.getTime() > Date.now()) return true;
		const earliest = sortedDates[sortedDates.length - 1];
		if (!earliest) return true;
		const key = dayKey(date);
		return key < earliest || !allDates.has(key);
	}

	const calendarValue = $derived(new CalendarDate(selectedDate.getFullYear(), selectedDate.getMonth() + 1, selectedDate.getDate()));
	function handleCalendarChange(value: DateValue | undefined) {
		if (!value) return;
		selectedDate = value.toDate(getLocalTimeZone());
		isDatePickerOpen = false;
		reloadData();
	}

	onMount(() => {
		const observer = new ResizeObserver((observed) => {
			if (!chart) return;
			for (const entry of observed) chart.resize({ width: entry.contentRect.width, animation: { duration: 300 } });
		});
		if (chartEl) observer.observe(chartEl);
		return () => {
			observer.disconnect();
			chart?.dispose();
			chart = null;
		};
	});

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(() => reloadData());
	});
</script>

<svelte:head>
	<title>Instance Activity · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	{#if !vrchatAuth.ownerUserId}
		<Card.Root>
			<Card.Header>
				<Card.Description>Log in to VRChat to see your instance activity.</Card.Description>
			</Card.Header>
		</Card.Root>
	{:else}
		<div class="pt-12">
			<div class="mt-0 flex items-center justify-between">
				<div class="flex items-center justify-between">
					<span class="shrink-0">{m.view_charts_instance_activity_header()}</span>
					<HoverCard.Root>
						<HoverCard.Trigger>
							{#snippet child({ props })}
								<span {...props}><Info class="ml-1 size-3 opacity-70" /></span>
							{/snippet}
						</HoverCard.Trigger>
						<HoverCard.Content side="bottom" align="start" class="w-75">
							<div>
								<div class="mb-1.5 text-xs">{m.view_charts_instance_activity_tips_online_time()}</div>
								<div class="mb-1.5 text-xs">{m.view_charts_instance_activity_tips_click_Y_axis()}</div>
								<div class="mt-2 mb-1.5 flex items-center text-xs">{m.view_charts_instance_activity_tips_click_instance_name()}</div>
							</div>
						</HoverCard.Content>
					</HoverCard.Root>
				</div>

				<div class="flex items-center">
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="mr-1.5 rounded-full" size="icon" variant="ghost" aria-label={m.dialog_note_export_refresh()} onclick={() => reloadData()}>
									<RefreshCcw />
								</Button>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">{m.dialog_note_export_refresh()}</Tooltip.Content>
					</Tooltip.Root>

					<Popover.Root>
						<Popover.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="mr-1.5 rounded-full" size="icon" variant="ghost" title={m.view_login_settings()} aria-label={m.view_login_settings()}>
									<Settings />
								</Button>
							{/snippet}
						</Popover.Trigger>
						<Popover.Content side="bottom" class="w-62.5">
							<div>
								<div class="flex h-[30px] items-center justify-between px-0.5">
									<span class="shrink-0">{m.view_charts_instance_activity_settings_bar_width()}</span>
									<div class="ml-5 w-40">
										<Slider
											type="multiple"
											bind:value={barWidthDraft}
											max={50}
											min={1}
											onValueCommit={(v) => {
												barWidth = v[0] ?? barWidth;
												storeSetting(SETTINGS.barWidth, barWidth);
												drawChart();
												handleSettingsChange();
											}}
										/>
									</div>
								</div>
								<div class="flex h-[30px] items-center justify-between px-0.5">
									<span class="shrink-0">{m.view_charts_instance_activity_settings_show_detail()}</span>
									<Switch
										bind:checked={isDetailVisible}
										onCheckedChange={(v) => {
											storeSetting(SETTINGS.detail, v);
											handleSettingsChange();
										}}
									/>
								</div>
								{#if isDetailVisible}
									<div class="flex h-[30px] items-center justify-between px-0.5">
										<span class="shrink-0">{m.view_charts_instance_activity_settings_show_solo_instance()}</span>
										<Switch
											bind:checked={isSoloInstanceVisible}
											onCheckedChange={(v) => {
												storeSetting(SETTINGS.solo, v);
												handleSettingsChange();
											}}
										/>
									</div>
									<div class="flex h-[30px] items-center justify-between px-0.5">
										<span class="shrink-0">{m.view_charts_instance_activity_settings_show_no_friend_instance()}</span>
										<Switch
											bind:checked={isNoFriendInstanceVisible}
											onCheckedChange={(v) => {
												storeSetting(SETTINGS.noFriend, v);
												handleSettingsChange();
											}}
										/>
									</div>
								{/if}
							</div>
						</Popover.Content>
					</Popover.Root>
					<div class="mr-2 inline-flex">
						<Button variant="ghost" size="icon-sm" title={m.view_charts_instance_activity_previous_day()} aria-label={m.view_charts_instance_activity_previous_day()} disabled={isPrevDayBtnDisabled} onclick={() => changeSelectedDateFromBtn(false)}>
							<ArrowLeft />
						</Button>
						<Button variant="ghost" size="icon-sm" title={m.view_charts_instance_activity_next_day()} aria-label={m.view_charts_instance_activity_next_day()} disabled={isNextDayBtnDisabled} onclick={() => changeSelectedDateFromBtn(true)}>
							<ArrowRight />
						</Button>
					</div>
					<Popover.Root bind:open={isDatePickerOpen}>
						<Popover.Trigger>
							{#snippet child({ props })}
								<Button {...props} variant="outline" class="w-50 justify-start text-left font-normal" disabled={isLoading}>
									<CalendarIcon class="mr-2 h-4 w-4" />
									{dayKey(selectedDate)}
								</Button>
							{/snippet}
						</Popover.Trigger>
						<Popover.Content class="w-auto p-0" align="end">
							<Calendar type="single" value={calendarValue} placeholder={today(getLocalTimeZone())} isDateDisabled={isCalendarDateDisabled} preventDeselect onValueChange={handleCalendarChange} />
						</Popover.Content>
					</Popover.Root>
				</div>
			</div>

			<div class="flex justify-center text-center">
				<div class="text-center">
					<div class="text-sm text-muted-foreground">{m.view_charts_instance_activity_online_time()}</div>
					<div class="text-2xl font-semibold">{timeToText(totalOnlineTime, true)}</div>
				</div>
			</div>

			{#if loadError}
				<p class="mt-4 text-center text-sm text-destructive">Couldn't load the activity: {loadError}</p>
			{/if}

			<div bind:this={chartEl} style="width: 100%"></div>
			{#if !isLoading && activityData.length === 0}
				<div class="mt-[100px] flex items-center justify-center">
					<DataTableEmpty />
				</div>
			{/if}

			{#if isDetailVisible && !isLoading && activityData.length !== 0}
				<div class="px-[400px]">
					<div class="flex items-center">
						<Separator class="flex-1" />
						<span class="px-2 text-muted-foreground">·</span>
						<Separator class="flex-1" />
					</div>
				</div>
				{#each filteredActivityDetailData as arr, i (arr[0].location + arr[0].joinTime.getTime())}
					<InstanceActivityDetail
						bind:this={detailRefs[i]}
						entries={arr}
						barWidth={barWidth}
						accountId={vrchatAuth.ownerUserId}
					/>
				{/each}
			{/if}
		</div>
	{/if}
</Tooltip.Provider>
