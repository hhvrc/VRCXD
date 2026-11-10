<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { onMount, untrack } from 'svelte';
	import * as echarts from 'echarts';
	import Location from '#lib/components/instance/location.svelte';
	import { activityTooltip, clock, isDarkMode, truncateName } from '#lib/activity/charts.js';

	// The original's InstanceActivityDetail.vue: one instance the account was in, every
	// player's stays as stacked bars (offset + time per visit) against the account's own
	// time window. Clicking a name opens the user.

	export type DetailEntry = {
		id: number;
		userId: string;
		displayName: string;
		location: string;
		worldName: string;
		joinTime: Date;
		leaveTime: Date;
		time: number;
		isFriend: boolean | null;
		isFavorite: boolean | null;
	};

	// fullRange spans every stay, for an instance the account was in more than once or never.
	let { entries, barWidth, accountId, fullRange = false }: { entries: DetailEntry[]; barWidth: number; accountId: string; fullRange?: boolean } = $props();

	let chartEl = $state<HTMLDivElement | null>(null);
	let rootEl = $state<HTMLDivElement | null>(null);
	let chart: echarts.ECharts | null = null;
	let usersFirstActivity: DetailEntry[] = [];

	const startTimeStamp = $derived(fullRange && entries.length ? Math.min(...entries.map((e) => e.joinTime.getTime())) : entries.find((e) => e.userId === accountId)?.joinTime.getTime());
	const endTimeStamp = $derived(fullRange && entries.length ? Math.max(...entries.map((e) => e.leaveTime.getTime())) : entries.find((e) => e.userId === accountId)?.leaveTime.getTime());

	export function scrollIntoView() {
		rootEl?.scrollIntoView({ behavior: 'smooth', block: 'start' });
	}

	export function rerender() {
		if (chart) draw();
	}

	function friendOrFavIcon(displayName: string): string {
		const found = entries.find((e) => e.displayName === displayName);
		if (!found) return '';
		if (found.isFavorite) return '⭐';
		if (found.isFriend) return '💚';
		return '';
	}

	function buildOption(): echarts.EChartsOption | null {
		if (!entries.length || !startTimeStamp || !endTimeStamp) return null;
		type Element = { offset: number; time: number; tail: number; entry: DetailEntry };
		const grouped = new Map<string, Element[]>();
		const unique: DetailEntry[] = [];
		for (const entry of entries) {
			if (!grouped.has(entry.userId)) {
				grouped.set(entry.userId, []);
				unique.push(entry);
			}
			const elements = grouped.get(entry.userId)!;
			const last = elements[elements.length - 1];
			const offset = Math.max(0, elements.length === 0 ? entry.joinTime.getTime() - startTimeStamp : entry.joinTime.getTime() - startTimeStamp - last.tail);
			const tail = elements.length === 0 ? offset + entry.time : last.tail + offset + entry.time;
			elements.push({ offset, time: entry.time, tail, entry });
		}
		usersFirstActivity = unique;

		const maxEntryCount = Math.max(...Array.from(grouped.values()).map((v) => v.length));
		const series: echarts.SeriesOption[] = [];
		for (let index = 0; index < maxEntryCount; index++) {
			series.push({
				name: 'Placeholder',
				type: 'bar',
				stack: 'Total',
				itemStyle: { borderColor: 'transparent', color: 'transparent' },
				emphasis: { itemStyle: { borderColor: 'transparent', color: 'transparent' } },
				data: unique.map((u) => grouped.get(u.userId)![index]?.offset ?? 0)
			});
			series.push({
				name: m.table_previous_instances_time(),
				type: 'bar',
				stack: 'Total',
				colorBy: 'data',
				barWidth,
				emphasis: { focus: 'self' },
				itemStyle: { borderRadius: 2, shadowBlur: 2, shadowOffsetX: 0.7, shadowOffsetY: 0.5 },
				data: unique.map((u) => grouped.get(u.userId)![index]?.time ?? 0)
			});
		}

		return {
			tooltip: {
				trigger: 'item',
				axisPointer: { type: 'shadow' },
				formatter: (params: unknown) => {
					const p = params as { seriesIndex: number; dataIndex: number; color: string };
					if (p.seriesIndex % 2 !== 1) return '';
					const user = unique[p.dataIndex];
					const element = grouped.get(user?.userId)?.[Math.floor(p.seriesIndex / 2)]?.entry;
					if (!element) return '';
					return activityTooltip({
						color: p.color,
						displayName: element.displayName,
						icon: friendOrFavIcon(element.displayName),
						joinTime: element.joinTime,
						leaveTime: element.leaveTime,
						durationMs: element.time
					});
				}
			},
			grid: { top: 50, left: 160, right: 90 },
			yAxis: {
				type: 'category',
				axisLabel: { interval: 0, formatter: (value: string) => `${friendOrFavIcon(value)} ${truncateName(value)}` },
				inverse: true,
				data: unique.map((u) => u.displayName),
				triggerEvent: true
			},
			xAxis: {
				type: 'value',
				min: 0,
				max: endTimeStamp - startTimeStamp,
				axisLine: { show: true },
				axisLabel: { formatter: (value: number) => clock(new Date(value + startTimeStamp)) },
				splitLine: { lineStyle: { type: 'dashed' } }
			},
			series,
			backgroundColor: 'transparent'
		};
	}

	function draw() {
		if (!chartEl) return;
		const height = entries.length * (barWidth + 10) + 200;
		if (!chart) {
			chart = echarts.init(chartEl, isDarkMode() ? 'dark' : undefined, { height, useDirtyRect: entries.length > 80 });
			chart.on('click', 'yAxis', (params: unknown) => {
				const p = params as { dataIndex: number };
				const user = usersFirstActivity[p.dataIndex];
				if (user?.userId) userDialog.open(user.userId);
			});
		}
		chart.resize({ height, animation: { duration: 300 } });
		const option = buildOption();
		if (option) {
			chart.clear();
			chart.setOption(option, { notMerge: true });
		} else {
			chart.clear();
		}
	}

	onMount(() => {
		const observer = new ResizeObserver((observed) => {
			if (!chart) return;
			for (const entry of observed) chart.resize({ width: entry.contentRect.width, animation: { duration: 300 } });
		});
		if (chartEl) observer.observe(chartEl);
		draw();
		return () => {
			observer.disconnect();
			chart?.dispose();
			chart = null;
		};
	});

	$effect(() => {
		void barWidth;
		void entries;
		untrack(() => {
			if (chart) draw();
		});
	});
</script>

<div bind:this={rootEl} style="width: 100%">
	<div class="mt-15" style="height: 25px">
		<Location class="flex items-center justify-center" location={entries[0]?.location} hint={entries[0]?.worldName ?? ''} />
	</div>
	{#if entries.length}
		<div bind:this={chartEl}></div>
	{:else}
		<div class="flex min-h-40 w-full items-center justify-center">
			<DataTableEmpty />
		</div>
	{/if}
</div>
