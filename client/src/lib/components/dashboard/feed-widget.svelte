<script lang="ts">
	import { CENTERED_NOTE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { untrack } from 'svelte';
	import Box from '@lucide/svelte/icons/box';
	import MapPin from '@lucide/svelte/icons/map-pin';
	import Pencil from '@lucide/svelte/icons/pencil';
	import LocationMenu from '#lib/components/instance/location-menu.svelte';
	import UserContextMenu from '#lib/components/common/user-context-menu.svelte';
	import * as Table from '#lib/components/ui/table/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { FEED_TYPES, type FeedEntry } from '#lib/api/types.js';
	import { formatDateFilter, statusClass } from '#lib/helpers/format.js';
	import { live, watchLive } from '#lib/api/live.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import WidgetHeader from './widget-header.svelte';
	import { filterActive } from './widget-filters';
	import WidgetSettingsMenu from './widget-settings-menu.svelte';

	// The original's FeedWidget: the newest 100 feed rows of the chosen types.
	let { config = {}, configUpdater }: { config?: Record<string, unknown>; configUpdater?: (config: Record<string, unknown>) => void } = $props();

	const LABELS: Record<string, string> = { GPS: 'GPS', Online: 'Online', Offline: 'Offline', Status: 'Status', Avatar: 'Avatar', Bio: 'Bio' };

	let rows = $state.raw<FeedEntry[]>([]);
	const showType = $derived(config.showType === true);
	const filtered = $derived(rows.filter((row) => filterActive(config.filters, row.type)).slice(0, 100));

	async function load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		try {
			rows = await api.get<FeedEntry[]>(accountPath(account, '/feed'), { types: '', limit: 500 });
		} catch {
			rows = [];
		}
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(load);
		return watchLive(['presence', 'user'], load, 750);
	});

	const presenceState = (id: string) => live.presence.get(id)?.state ?? '';
	const where = (id: string) => live.presence.get(id)?.location ?? '';
	const favorites = $derived(sidebarData.vipIds);
</script>

{#snippet who(row: FeedEntry)}
	<UserContextMenu userId={row.userId} name={row.displayName} state={presenceState(row.userId)} location={where(row.userId)}>
		<span class="shrink-0 cursor-pointer" role="button" tabindex="0" onclick={() => userDialog.open(row.userId)} onkeydown={(e) => e.key === 'Enter' && userDialog.open(row.userId)}>{row.displayName}</span>
	</UserContextMenu>
{/snippet}

<div class="flex h-full min-h-0 flex-col">
	<WidgetHeader title={m.dashboard_widget_feed()} icon="ri-rss-line" href="/feed">
		<WidgetSettingsMenu options={FEED_TYPES} labels={LABELS} {config} {configUpdater} toggles={[{ key: 'showType', label: m.dashboard_widget_config_show_type(), checked: showType }]} />
	</WidgetHeader>

	<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto">
		{#if filtered.length}
			<Tooltip.Provider>
				<Table.Root>
					<Table.Body>
						{#each filtered as row, index (`${row.type}-${row.rowId}-${index}`)}
							<!-- The original's favourite stripe, for friends in the favorite groups the sidebar shows. -->
							<Table.Row class={['cursor-default', favorites.has(row.userId) && 'border-l-2 border-l-chart-4']}>
								<Table.Cell class="w-28 text-2xs text-muted-foreground tabular-nums">
									<Tooltip.Root>
										<Tooltip.Trigger>{formatDateFilter(row.createdAt, 'short')}</Tooltip.Trigger>
										<Tooltip.Content side="top">{formatDateFilter(row.createdAt, 'long')}</Tooltip.Content>
									</Tooltip.Root>
								</Table.Cell>
								{#if showType}<Table.Cell class="w-16 text-2xs text-muted-foreground">{row.type}</Table.Cell>{/if}
								<Table.Cell class="max-w-0 truncate">
									{#if row.type === 'GPS'}
										<div class="flex min-w-0 items-center">
											<MapPin class="mr-1 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
											{@render who(row)}
											<span class="mx-1 shrink-0 text-muted-foreground"> → </span>
											<div class="min-w-0 flex-1 truncate"><LocationMenu location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} disableTooltip /></div>
										</div>
									{:else if row.type === 'Online'}
										<div class="flex min-w-0 items-center">
											<i class="x-user-status online mr-1 shrink-0"></i>
											{@render who(row)}
											{#if row.location}
												<span class="shrink-0 text-muted-foreground"> → </span>
												<div class="min-w-0 flex-1 truncate"><LocationMenu location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} disableTooltip /></div>
											{/if}
										</div>
									{:else if row.type === 'Offline'}
										<i class="x-user-status mr-1"></i>{@render who(row)}
									{:else if row.type === 'Status'}
										<i class={['x-user-status mr-1', statusClass(row.status)]}></i>{@render who(row)}
										<span class="ml-1 text-muted-foreground"> {row.statusDescription}</span>
									{:else if row.type === 'Avatar'}
										<Box class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground" />{@render who(row)}
										<span class="text-muted-foreground"> → {row.avatarName}</span>
									{:else if row.type === 'Bio'}
										<Pencil class="mr-1 inline-block h-3.5 w-3.5 shrink-0 text-muted-foreground" />{@render who(row)}
										<span class="ml-1 text-muted-foreground">{m.dashboard_widget_feed_bio()}</span>
									{:else}
										{@render who(row)}<span class="text-muted-foreground"> {row.type}</span>
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
