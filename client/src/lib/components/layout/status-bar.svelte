<script lang="ts" module>
	const appStartAt = Date.now();
</script>

<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { onMount, tick } from 'svelte';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { formatSeconds, timeToText } from '#lib/helpers/format.js';
	import { desktop } from '#lib/instances/instance-launch.svelte.js';
	import { live, liveMessageCount, onLiveEvent } from '#lib/api/live.svelte.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import NumberField from '#lib/components/common/number-field.svelte';
	import type { GameLogRow } from '#lib/activity/game-log-sessions.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { vrchatStatus } from '#lib/api/vrchat-status.svelte.js';
	import { clockElapsed, formatAppUptime, formatUtcHour, loadClockCount, loadClocks, loadVisibility, normalizeUtcHour, parseClockOffset, type NowPlayingClock, type Visibility } from './status-bar-utils.js';

	// The original's StatusBar: the game and its session, the live socket's message rate, uptime and
	// clocks, VRChat's server status and the video now playing in the game's instance, each hidden or
	// shown from the right-click menu, and the page zoom (the Interface setting). SteamVR is as the
	// account's PC reports it. The proxy segment is not ported: the server makes VRChat's requests.
	const VISIBILITY_KEY = 'VRCX_statusBarVisibility';
	const CLOCKS_KEY = 'VRCX_statusBarClocks';
	const CLOCK_COUNT_KEY = 'VRCX_statusBarClockCount';

	let visibility = $state<Visibility>(loadVisibility(readSetting<unknown>(VISIBILITY_KEY, null)));

	function toggle(key: keyof Visibility) {
		visibility[key] = !visibility[key];
		storeSetting(VISIBILITY_KEY, $state.snapshot(visibility));
	}

	let now = $state(Date.now());

	// The original's Now Playing: the last video played in the game's current instance, until it
	// leaves or the game closes. Its play position comes from the desktop's video events, which
	// the server does not keep, so there is no progress bar here.
	// The original's zoom segment: click to edit the zoom in place; leaving the field closes it.
	let zoomEditing = $state(false);
	let zoomSegment = $state<HTMLElement>();
	async function toggleZoomEdit() {
		if (zoomEditing) {
			zoomEditing = false;
			return;
		}
		zoomEditing = true;
		await tick();
		zoomSegment?.querySelector('input')?.focus();
	}

	// The original's nowPlaying: start is when playback began (logged time less the logged position),
	// and a known length gives the progress bar; a video past its length is cleared, as the original's
	// updateNowPlaying does.
	// A ProTV world's nowplaying producers send their own clock, thumbnail and hidden URL instead.
	let nowPlaying = $state<{ url: string; name: string; start: number; length: number; clock?: NowPlayingClock; thumbnailUrl?: string; urlHidden?: boolean } | null>(null);
	let nowPlayingNow = $state(Date.now() / 1000);
	const nowPlayingElapsed = $derived(
		!nowPlaying || nowPlaying.length <= 0 ? 0
		: nowPlaying.clock ? clockElapsed(nowPlaying.clock, nowPlaying.length, nowPlayingNow)
		: Math.min(nowPlaying.length, Math.max(0, nowPlayingNow - nowPlaying.start))
	);
	$effect(() => {
		if (!nowPlaying || nowPlaying.length <= 0 || nowPlaying.clock?.paused) return;
		const timer = setInterval(() => {
			nowPlayingNow = Date.now() / 1000;
			if (nowPlaying && !nowPlaying.clock && nowPlayingNow - nowPlaying.start >= nowPlaying.length) nowPlaying = null;
		}, 1000);
		return () => clearInterval(timer);
	});
	const isYouTubeNowPlaying = $derived(/youtu\.?be/i.test(nowPlaying?.url ?? ''));

	// The last position a world reported for the video playing (a seek or resume), kept across reloads
	// of the newest stored play, which only holds where it started.
	let seek: { url: string; start: number; length: number } | null = null;

	async function loadNowPlaying(account: string) {
		if (nowPlaying?.clock) return;
		try {
			const [last] = await api.get<GameLogRow[]>(accountPath(account, '/game-log'), { types: 'Location,VideoPlay', limit: 1 });
			if (account !== vrchatAuth.ownerUserId || nowPlaying?.clock) return;
			nowPlaying =
				last?.type === 'VideoPlay' && last.videoUrl
					? {
							url: last.videoUrl,
							name: last.videoName ? (last.displayName ? `${last.videoName} (${last.displayName})` : last.videoName) : '',
							start: Date.parse(last.createdAt) / 1000 - (last.videoPos ?? 0),
							length: last.videoLength ?? 0
						}
					: null;
			if (nowPlaying && seek?.url === nowPlaying.url) nowPlaying = { ...nowPlaying, start: seek.start, length: seek.length || nowPlaying.length };
			else seek = null;
			nowPlayingNow = Date.now() / 1000;
			if (nowPlaying && nowPlaying.length > 0 && nowPlayingNow - nowPlaying.start >= nowPlaying.length) nowPlaying = null;
		} catch {
			// Keeps what it showed.
		}
	}

	$effect(() => {
		const account = vrchatAuth.ownerUserId;
		if (!account || !desktop.gameRunning || !visibility.nowPlaying) {
			nowPlaying = null;
			return;
		}
		loadNowPlaying(account);
		let timer: ReturnType<typeof setTimeout> | undefined;
		const unsubscribe = onLiveEvent((event) => {
			if (event.event.case === 'nowPlayingSpec') {
				const p = event.event.value;
				if (p.accountId !== account) return;
				if (p.cleared || !p.loggedAt) {
					nowPlaying = null;
					loadNowPlaying(account);
					return;
				}
				const at = Number(p.loggedAt.seconds) + p.loggedAt.nanos / 1e9;
				const clock = { position: p.position, at, speed: p.speed || 1, paused: p.paused };
				nowPlaying = { url: p.videoUrl, name: p.name, start: at - p.position, length: p.length, clock, thumbnailUrl: p.thumbnailUrl, urlHidden: p.urlHidden };
				nowPlayingNow = Date.now() / 1000;
				return;
			}
			if (event.event.case === 'nowPlayingPosition') {
				const p = event.event.value;
				if (p.accountId !== account || !p.loggedAt) return;
				// The original's setNowPlaying for the same URL: start is the logged time less the position.
				seek = { url: p.videoUrl, start: Number(p.loggedAt.seconds) + p.loggedAt.nanos / 1e9 - p.videoPos, length: p.videoLength };
				if (nowPlaying?.url === p.videoUrl) {
					nowPlaying = { ...nowPlaying, start: seek.start, length: p.videoLength || nowPlaying.length };
					nowPlayingNow = Date.now() / 1000;
				}
				return;
			}
			if (event.event.case !== 'visit' && event.event.case !== 'gameLog') return;
			clearTimeout(timer);
			timer = setTimeout(() => loadNowPlaying(account), 1000);
		});
		return () => {
			clearTimeout(timer);
			unsubscribe();
		};
	});

	// The original opens the Servers card for 5 seconds when an issue starts.
	let serversOpen = $state(false);
	let serversTimer: ReturnType<typeof setTimeout> | undefined;
	onMount(() => vrchatStatus.start());
	$effect(() => {
		const issue = vrchatStatus.hasIssue;
		clearTimeout(serversTimer);
		serversOpen = issue && visibility.servers;
		if (serversOpen) serversTimer = setTimeout(() => (serversOpen = false), 5000);
	});

	// Game session.
	const startedAt = $derived(desktop.gameRunning && desktop.gameStartedAt ? Date.parse(desktop.gameStartedAt) : 0);
	const sessionText = $derived(startedAt && now - startedAt > 0 ? timeToText(now - startedAt) : '');
	const lastStart = $derived(desktop.lastGameStartedAt ? Date.parse(desktop.lastGameStartedAt) : 0);
	const lastStop = $derived(desktop.lastGameStoppedAt ? Date.parse(desktop.lastGameStoppedAt) : 0);
	const lastSessionMs = $derived(lastStart && lastStop ? lastStop - lastStart : 0);

	function shortDate(ms: number): string {
		if (!ms) return '-';
		const d = new Date(ms);
		const pad = (n: number) => String(n).padStart(2, '0');
		return `${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
	}

	// Live socket rate and sparkline, sampled each second over the last minute.
	const GRAPH_POINTS = 60;
	const WIDTH = 48;
	const HEIGHT = 12;
	let history = new Array<number>(GRAPH_POINTS).fill(0);
	let lastCount = liveMessageCount();
	let perMinute = $state(0);
	let canvas = $state<HTMLCanvasElement>();

	function cssColor(name: string, fallback: string): string {
		const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
		if (!value) return fallback;
		return /^(#|rgb|hsl|oklch)/.test(value) ? value : `hsl(${value})`;
	}

	function draw() {
		const ctx = canvas?.getContext('2d');
		if (!canvas || !ctx) return;
		const dpr = window.devicePixelRatio || 1;
		canvas.width = Math.floor(WIDTH * dpr);
		canvas.height = Math.floor(HEIGHT * dpr);
		canvas.style.width = `${WIDTH}px`;
		canvas.style.height = `${HEIGHT}px`;
		ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
		ctx.clearRect(0, 0, WIDTH, HEIGHT);
		const max = Math.max(...history, 1);
		const step = WIDTH / (history.length - 1);
		ctx.globalAlpha = 0.75;
		ctx.strokeStyle = cssColor('--foreground', '#cfd3dc');
		ctx.lineWidth = 1;
		ctx.beginPath();
		history.forEach((value, i) => {
			const y = HEIGHT - (value / max) * (HEIGHT - 2);
			if (i === 0) ctx.moveTo(0, y);
			else ctx.lineTo(i * step, y);
		});
		ctx.stroke();
		ctx.globalAlpha = 1;
	}

	onMount(() => {
		const timer = setInterval(() => {
			now = Date.now();
			const count = liveMessageCount();
			history = [...history.slice(1), count - lastCount];
			lastCount = count;
			perMinute = Math.round(history.reduce((a, b) => a + b, 0));
			draw();
		}, 1000);
		return () => clearInterval(timer);
	});

	// Clocks.
	const localOffset = normalizeUtcHour(-new Date().getTimezoneOffset() / 60);
	let clocks = $state(loadClocks(readSetting<unknown>(CLOCKS_KEY, null), [{ offset: localOffset }, { offset: 0 }, { offset: localOffset < 0 ? 9 : -5 }]));
	let clockCount = $state(loadClockCount(readSetting<unknown>(CLOCK_COUNT_KEY, null)));
	let clockOpen = $state([false, false, false]);

	function setClockCount(count: number) {
		clockCount = count;
		storeSetting(CLOCK_COUNT_KEY, String(count));
		if (count > 0 && !visibility.clocks) toggle('clocks');
	}

	function setClockOffset(index: number, value: string) {
		clocks[index].offset = parseClockOffset(value);
		storeSetting(CLOCKS_KEY, $state.snapshot(clocks));
		clockOpen[index] = false;
	}

	function formatClock(offset: number): string {
		const shifted = new Date(now + normalizeUtcHour(offset) * 3_600_000);
		const pad = (n: number) => String(n).padStart(2, '0');
		return `${pad(shifted.getUTCHours())}:${pad(shifted.getUTCMinutes())} ${formatUtcHour(offset)}`;
	}

	const timezones = Array.from({ length: 27 }, (_, i) => i - 12);
	const SEGMENT = 'flex h-[22px] items-center gap-1 border-r border-border px-2 whitespace-nowrap';
</script>

<!-- The layout renders the bar outside its own tooltip provider. -->
<Tooltip.Provider>
<div class="h-[22px] shrink-0 overflow-hidden border-t border-border bg-sidebar text-xs select-none" style="font-family: var(--font-mono-cjk)">
	<ContextMenu.Root>
		<ContextMenu.Trigger class="flex h-full w-full items-center px-2">
			<div
				class="flex min-w-0 flex-1 items-center overflow-hidden [&>*:first-child]:pl-0.5"
				style="mask-image: linear-gradient(to right, black calc(100% - 20px), transparent 100%)"
			>
				{#if visibility.steamvr}
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<div {...props} class={SEGMENT}>
									<span class="inline-block size-2 shrink-0 rounded-full {desktop.steamVrRunning ? 'bg-status-online' : 'bg-status-offline-alt'}"></span>
									<span class="text-2xs text-foreground">{m.status_bar_steamvr()}</span>
								</div>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">{desktop.steamVrRunning ? m.status_bar_steamvr_running() : m.status_bar_steamvr_stopped()}</Tooltip.Content>
					</Tooltip.Root>
				{/if}

				{#if visibility.vrchat}
					<HoverCard.Root openDelay={50} closeDelay={50}>
						<HoverCard.Trigger>
							{#snippet child({ props })}
								<div {...props} class={SEGMENT}>
									<span class="inline-block size-2 shrink-0 rounded-full {desktop.gameRunning ? 'bg-status-online' : 'bg-status-offline'}"></span>
									<span class="text-2xs text-foreground">{m.status_bar_game()}</span>
									{#if desktop.gameRunning}<span class="text-3xs text-foreground">{sessionText}</span>{/if}
								</div>
							{/snippet}
						</HoverCard.Trigger>
						{#if desktop.gameRunning && startedAt}
							<HoverCard.Content side="top" align="start" sideOffset={4} class="w-auto min-w-[160px] px-3 py-2">
								<div class="flex flex-col gap-1">
									<div class="flex items-center justify-between gap-3"><span class="text-2xs text-muted-foreground">{m.status_bar_game_started_at()}</span><span class="text-2xs text-foreground">{shortDate(startedAt)}</span></div>
									<div class="flex items-center justify-between gap-3"><span class="text-2xs text-muted-foreground">{m.status_bar_game_session_duration()}</span><span class="text-2xs text-foreground">{now - startedAt > 0 ? timeToText(now - startedAt, true) : '-'}</span></div>
								</div>
							</HoverCard.Content>
						{:else if !desktop.gameRunning && lastSessionMs > 0}
							<HoverCard.Content side="top" align="start" sideOffset={4} class="w-auto min-w-[160px] px-3 py-2">
								<div class="flex flex-col gap-1">
									<div class="flex items-center justify-between gap-3"><span class="text-2xs text-muted-foreground">{m.status_bar_game_last_session()}</span><span class="text-2xs text-foreground">{timeToText(lastSessionMs)}</span></div>
									<div class="flex items-center justify-between gap-3"><span class="text-2xs text-muted-foreground">{m.status_bar_game_last_offline()}</span><span class="text-2xs text-foreground">{shortDate(lastStop)}</span></div>
								</div>
							</HoverCard.Content>
						{/if}
					</HoverCard.Root>
				{/if}

				{#if visibility.servers}
					{@const dot = vrchatStatus.hasIssue ? (vrchatStatus.isMajor ? 'bg-destructive' : 'bg-status-askme') : 'bg-status-online'}
					<HoverCard.Root bind:open={serversOpen} openDelay={50} closeDelay={50}>
						<HoverCard.Trigger>
							{#snippet child({ props })}
								<button {...props} type="button" class="{SEGMENT} cursor-pointer hover:bg-accent" title={vrchatStatus.hasIssue ? undefined : 'VRChat servers are operational'} onclick={() => vrchatStatus.openPage()}>
									<span class="inline-block size-2 shrink-0 rounded-full {dot}"></span>
									<span class="text-2xs text-foreground">{m.status_bar_servers()}</span>
								</button>
							{/snippet}
						</HoverCard.Trigger>
						{#if vrchatStatus.hasIssue}
							<HoverCard.Content side="top" align="start" sideOffset={4} class="w-[280px] px-3 py-2.5">
								<div class="mb-1.5 flex items-center gap-1.5">
									<span class="inline-block size-2 shrink-0 rounded-full {dot}"></span>
									<span class="text-xs font-semibold text-foreground">{m.status_bar_servers_issue()}</span>
								</div>
								<p class="m-0 text-2xs leading-[1.4] text-muted-foreground">{vrchatStatus.text}</p>
							</HoverCard.Content>
						{/if}
					</HoverCard.Root>
				{/if}

				{#if visibility.ws}
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<div {...props} class={SEGMENT}>
									<span class="inline-block size-2 shrink-0 rounded-full {live.connected ? 'bg-status-online' : 'bg-status-offline'}"></span>
									<span class="text-2xs text-foreground">{m.view_login_field_websocket()}</span>
									<canvas bind:this={canvas} class="shrink-0 rounded-sm"></canvas>
									<span class="text-3xs text-foreground">{perMinute} avg/min</span>
									{#if live.error && !live.connected}<span class="text-3xs text-destructive">{live.error}</span>{/if}
								</div>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">WebSocket: {live.connected ? 'Connected' : 'Disconnected'}{live.serverVersion ? ` (server ${live.serverVersion})` : ''}</Tooltip.Content>
					</Tooltip.Root>
				{/if}

				{#if visibility.nowPlaying && nowPlaying}
					<div class="{SEGMENT} max-w-[400px] min-w-0">
						{#if isYouTubeNowPlaying}<i class="ri-youtube-fill shrink-0 text-[12px] text-[#FF0000]"></i>{:else}<i class="ri-play-fill shrink-0 text-3xs"></i>{/if}
						<Tooltip.Root>
							<Tooltip.Trigger>
								{#snippet child({ props })}
									<button {...props} type="button" class="cursor-pointer truncate text-2xs text-foreground" onclick={() => nowPlaying && !nowPlaying.urlHidden && window.open(nowPlaying.url, '_blank', 'noopener,noreferrer')}>{nowPlaying?.name || nowPlaying?.url}</button>
								{/snippet}
							</Tooltip.Trigger>
							<Tooltip.Content side="top" class="p-2">
								<div class="flex max-w-[320px] flex-col gap-1.5">
									{#if nowPlaying.thumbnailUrl}<img src={nowPlaying.thumbnailUrl} alt="" class="max-h-[180px] w-full rounded object-contain" loading="lazy" />{/if}
									{#if nowPlaying.name}<span class="whitespace-pre-wrap">{nowPlaying.name}</span>{/if}
									{#if !nowPlaying.urlHidden}<span class="break-all text-muted-foreground">{nowPlaying.url}</span>{/if}
								</div>
							</Tooltip.Content>
						</Tooltip.Root>
						{#if nowPlaying.length > 0}
							{@const progress = `${formatSeconds(nowPlayingElapsed)} / ${formatSeconds(nowPlaying.length)}`}
							<div class="ml-1 h-[4px] w-[40px] shrink-0 overflow-hidden rounded-full bg-muted" title={progress}>
								<div class="h-full rounded-full bg-foreground/60 transition-[width] duration-1000 ease-linear" style="width: {Math.round((nowPlayingElapsed * 1000) / nowPlaying.length) / 10}%"></div>
							</div>
							<span class="shrink-0 text-3xs tabular-nums">{progress}</span>
						{/if}
					</div>
				{/if}

				{#if visibility.zoom}
					<Tooltip.Root disabled={zoomEditing}>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<div
									{...props}
									bind:this={zoomSegment}
									role="button"
									tabindex="0"
									class="{SEGMENT} cursor-pointer hover:bg-accent"
									onclick={toggleZoomEdit}
									onkeydown={(e) => e.key === 'Enter' && !zoomEditing && toggleZoomEdit()}
									onfocusout={(e) => zoomEditing && !zoomSegment?.contains(e.relatedTarget as Node) && (zoomEditing = false)}
								>
									<span class="text-3xs text-foreground">{m.status_bar_zoom()}</span>
									{#if zoomEditing}
										<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
										<span onclick={(e) => e.stopPropagation()}>
											<NumberField class="h-[18px] w-24 text-2xs" value={appearance.zoomLevel.value} min={30} max={500} step={1} ariaLabel="Zoom" onchange={(v) => (appearance.zoomLevel.value = v)} />
										</span>
									{:else}
										<span class="text-3xs text-foreground">{appearance.zoomLevel.value}%</span>
									{/if}
								</div>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">{m.status_bar_zoom_tooltip()}</Tooltip.Content>
					</Tooltip.Root>
				{/if}

				{#if visibility.uptime}
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<div {...props} class={SEGMENT}>
									<span class="text-3xs text-foreground">{m.status_bar_app_uptime_short()}</span>
									<span class="text-3xs text-foreground">{formatAppUptime((now - appStartAt) / 1000)}</span>
								</div>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">{m.status_bar_app_uptime()}</Tooltip.Content>
					</Tooltip.Root>
				{/if}
			</div>

			<div class="ml-auto flex shrink-0 items-center [&>*:last-child]:border-r-0 [&>*:last-child]:pr-0.5">
				{#if visibility.clocks}
					{#each clocks.slice(0, clockCount) as clock, index (index)}
						<Popover.Root bind:open={clockOpen[index]}>
							<Popover.Trigger>
								{#snippet child({ props })}
									<div {...props} class="{SEGMENT} cursor-pointer hover:bg-accent"><span class="text-3xs text-foreground">{formatClock(clock.offset)}</span></div>
								{/snippet}
							</Popover.Trigger>
							<Popover.Content class="w-[280px]" side="top" align="center">
								<div class="flex flex-col gap-2 p-1">
									<span class="text-xs font-medium">{m.status_bar_timezone()}</span>
									<Select.Root type="single" value={String(clock.offset)} onValueChange={(value) => setClockOffset(index, value)}>
										<Select.Trigger size="sm">{formatUtcHour(clock.offset)}</Select.Trigger>
										<Select.Content class="max-h-60">
											{#each timezones as offset (offset)}
												<Select.Item value={String(offset)}><div class="flex w-full items-center justify-end font-mono">{formatUtcHour(offset)}</div></Select.Item>
											{/each}
										</Select.Content>
									</Select.Root>
								</div>
							</Popover.Content>
						</Popover.Root>
					{/each}
				{/if}
			</div>
		</ContextMenu.Trigger>

		<ContextMenu.Content>
			<ContextMenu.CheckboxItem checked={visibility.vrchat} closeOnSelect={false} onCheckedChange={() => toggle('vrchat')}>{m.status_bar_game()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.servers} closeOnSelect={false} onCheckedChange={() => toggle('servers')}>{m.status_bar_servers()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.steamvr} closeOnSelect={false} onCheckedChange={() => toggle('steamvr')}>{m.status_bar_steamvr()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.ws} closeOnSelect={false} onCheckedChange={() => toggle('ws')}>{m.view_login_field_websocket()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.nowPlaying} closeOnSelect={false} onCheckedChange={() => toggle('nowPlaying')}>{m.status_bar_now_playing()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.uptime} closeOnSelect={false} onCheckedChange={() => toggle('uptime')}>{m.status_bar_app_uptime_short()}</ContextMenu.CheckboxItem>
			<ContextMenu.CheckboxItem checked={visibility.zoom} closeOnSelect={false} onCheckedChange={() => toggle('zoom')}>{m.status_bar_zoom()}</ContextMenu.CheckboxItem>
			<ContextMenu.Separator />
			<ContextMenu.Sub>
				<ContextMenu.SubTrigger>{m.status_bar_clocks()}</ContextMenu.SubTrigger>
				<ContextMenu.SubContent>
					<ContextMenu.CheckboxItem checked={clockCount === 0} closeOnSelect={false} onCheckedChange={() => setClockCount(0)}>{m.status_bar_clocks_none()}</ContextMenu.CheckboxItem>
					{#each [1, 2, 3] as count (count)}
						<ContextMenu.CheckboxItem checked={clockCount === count} closeOnSelect={false} onCheckedChange={() => setClockCount(count)}>{count} Clocks</ContextMenu.CheckboxItem>
					{/each}
				</ContextMenu.SubContent>
			</ContextMenu.Sub>
		</ContextMenu.Content>
	</ContextMenu.Root>
</div>
</Tooltip.Provider>
