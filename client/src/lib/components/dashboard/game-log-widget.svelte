<script lang="ts">
	import { CENTERED_NOTE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	import { untrack } from 'svelte';
	import LogIn from '@lucide/svelte/icons/log-in';
	import LogOut from '@lucide/svelte/icons/log-out';
	import MapPin from '@lucide/svelte/icons/map-pin';
	import Play from '@lucide/svelte/icons/play';
	import Waypoints from '@lucide/svelte/icons/waypoints';
	import LocationMenu from '#lib/components/instance/location-menu.svelte';
	import * as Table from '#lib/components/ui/table/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import type { GameLogRow } from '#lib/activity/game-log-sessions.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { customTag } from '#lib/people/custom-tags.svelte.js';
	import WidgetHeader from './widget-header.svelte';
	import { filterActive } from './widget-filters';
	import WidgetSettingsMenu from './widget-settings-menu.svelte';

	// The original's GameLogWidget: the newest 200 game log rows of the chosen types.
	let { config = {}, configUpdater }: { config?: Record<string, unknown>; configUpdater?: (config: Record<string, unknown>) => void } = $props();

	const TYPES = ['Location', 'OnPlayerJoined', 'OnPlayerLeft', 'VideoPlay', 'PortalSpawn', 'Event', 'External'] as const;
	const LABELS: Record<string, string> = {
		Location: 'Location',
		OnPlayerJoined: 'Player joined',
		OnPlayerLeft: 'Player left',
		VideoPlay: 'Video play',
		PortalSpawn: 'Portal spawn',
		Event: 'Event',
		External: 'External'
	};
	const MAX = 200;

	let rows = $state.raw<GameLogRow[]>([]);
	const showDetail = $derived(config.showDetail === true);
	const filtered = $derived(rows.filter((row) => (TYPES as readonly string[]).includes(row.type) && filterActive(config.filters, row.type)).slice(0, MAX));

	async function load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		try {
			rows = await api.get<GameLogRow[]>(accountPath(account, '/game-log'), { types: '', limit: 500 });
		} catch {
			rows = [];
		}
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(load);
		return watchLive(['visit', 'gameLog'], load, 1000);
	});

	const open = (id: string | null | undefined) => id && userDialog.open(id);
	// The original's openExternalLink: asks before leaving.
	const openLink = (url: string) => openExternalLink(url);
</script>

<div class="flex h-full min-h-0 flex-col">
	<WidgetHeader title={m.dashboard_widget_game_log()} icon="ri-history-line" href="/game-log">
		<WidgetSettingsMenu options={TYPES} labels={LABELS} {config} {configUpdater} toggles={[{ key: 'showDetail', label: m.table_gameLog_detail(), checked: showDetail }]} />
	</WidgetHeader>

	<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto">
		{#if filtered.length}
			<Tooltip.Provider>
				<Table.Root class="table-fixed">
					<Table.Body>
						{#each filtered as row, index (`${row.key}-${index}`)}
							<Table.Row class={['cursor-default hover:bg-transparent', row.isFavorite && 'border-l-2 border-l-chart-4']}>
								<Table.Cell class="w-28 text-2xs text-muted-foreground tabular-nums">
									<Tooltip.Root>
										<Tooltip.Trigger>{formatDateFilter(row.createdAt, 'short')}</Tooltip.Trigger>
										<Tooltip.Content side="top">{formatDateFilter(row.createdAt, 'long')}</Tooltip.Content>
									</Tooltip.Root>
								</Table.Cell>
								<Table.Cell class="max-w-0 truncate">
									{#if row.type === 'Location'}
										<div class="flex min-w-0 items-center">
											<MapPin class="mr-1 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
											<div class="min-w-0 flex-1 truncate"><LocationMenu location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} disableTooltip /></div>
										</div>
									{:else if row.type === 'OnPlayerJoined' || row.type === 'OnPlayerLeft'}
										{#if row.type === 'OnPlayerJoined'}<LogIn class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground" />{:else}<LogOut
												class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground"
											/>{/if}
										{@const tagColour = row.userId && vrchatAuth.ownerUserId ? customTag(vrchatAuth.ownerUserId, row.userId)?.colour : ''}
										<button
											type="button"
											class={['cursor-pointer', row.type === 'OnPlayerLeft' && 'text-muted-foreground/70 hover:underline']}
											style={tagColour ? `color: ${tagColour}` : undefined}
											onclick={() => open(row.userId)}>{row.displayName}</button
										>
										{#if row.isFriend}<span>{row.isFavorite ? '⭐' : '💚'}</span>{/if}
									{:else if row.type === 'VideoPlay'}
										<Play class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground" />
										<Tooltip.Root>
											<Tooltip.Trigger>
												{#if row.videoId}<span class="mr-1 text-muted-foreground">{row.videoId}:</span>{/if}
												{#if row.videoId !== 'PopcornPalace' && row.videoUrl}
													<span role="link" tabindex="0" class="cursor-pointer text-muted-foreground hover:underline" onclick={() => openLink(row.videoUrl!)} onkeydown={(e) => e.key === 'Enter' && openLink(row.videoUrl!)}
														>{row.videoName || row.videoUrl}</span
													>
												{:else}<span class="text-muted-foreground">{row.videoName}</span>{/if}
											</Tooltip.Trigger>
											<Tooltip.Content side="top">{row.videoId ? `${row.videoId}: ${row.videoName || row.videoUrl}` : row.videoName || row.videoUrl}</Tooltip.Content>
										</Tooltip.Root>
									{:else if row.type === 'PortalSpawn'}
										<Waypoints class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground" />
										<button type="button" class="cursor-pointer hover:underline" onclick={() => open(row.userId)}>{row.displayName}</button>
										<span class="text-muted-foreground"> → </span>
										{#if row.location}<LocationMenu location={row.location} hint={row.worldName ?? ''} disableTooltip />{:else}<span class="text-muted-foreground">{row.worldName ?? ''}</span>{/if}
									{:else if showDetail}
										<span>{row.displayName}</span>
										<span class="text-muted-foreground"> {row.type}</span>
										{#if row.data || row.message}<span class="ml-1 text-muted-foreground">: {row.data || row.message}</span>{/if}
									{:else}
										<Tooltip.Root>
											<Tooltip.Trigger><span>{row.displayName}</span><span class="text-muted-foreground"> {row.type}</span></Tooltip.Trigger>
											{#if row.data || row.message}<Tooltip.Content side="top">{row.data || row.message}</Tooltip.Content>{/if}
										</Tooltip.Root>
									{/if}
								</Table.Cell>
							</Table.Row>
						{/each}
					</Table.Body>
				</Table.Root>
			</Tooltip.Provider>
		{:else}
			<div class={CENTERED_NOTE}>{m.dashboard_widget_no_data()}</div>
		{/if}
	</div>
</div>
