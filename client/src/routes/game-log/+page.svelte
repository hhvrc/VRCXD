<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	import { lookupUser } from '#lib/people/user-lookup.js';
	import { untrack } from 'svelte';
	import Copy from '@lucide/svelte/icons/copy';
	import ExternalLink from '@lucide/svelte/icons/external-link';
	import FileText from '@lucide/svelte/icons/file-text';
	import Logs from '@lucide/svelte/icons/logs';
	import Table2 from '@lucide/svelte/icons/table-2';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import DateCell from '#lib/components/common/date-cell.svelte';
	import LogDeleteButton from '#lib/components/common/log-delete-button.svelte';
	import FavoritesOnlyToggle from '#lib/components/common/favorites-only-toggle.svelte';
	import GameLogSessions from '#lib/components/activity/game-log-sessions.svelte';
	import LocationMenu from '#lib/components/instance/location-menu.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import PreviousInstanceInfo from '#lib/components/instance/previous-instance-info.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { userHref, worldHref } from '#lib/api/links.js';
	import type { GameLogRow } from '#lib/activity/game-log-sessions.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';

	// The original's Game Log over GET /1/accounts/{id}/game-log. Deleting a row hides it from this
	// view on the server; the log behind it stays in the database.
	const LABELS: Record<string, string> = {
		Location: 'Location',
		OnPlayerJoined: 'Player joined',
		OnPlayerLeft: 'Player left',
		PortalSpawn: 'Portal spawn',
		VideoPlay: 'Video play',
		Event: 'Event',
		External: 'External',
		StringLoad: 'String load',
		ImageLoad: 'Image load'
	};
	const FILTER_TYPES = ['Location', 'OnPlayerJoined', 'OnPlayerLeft', 'VideoPlay', 'Event', 'External', 'StringLoad', 'ImageLoad'].map((value) => ({ value, label: LABELS[value] }));
	const UNACTIONABLE = new Set(['OnPlayerJoined', 'OnPlayerLeft', 'Location', 'PortalSpawn']);
	const FILTERS_KEY = 'VRCX_gameLogTableFilters';
	const VIP_KEY = 'VRCX_gameLogTableVIPFilter';
	const VIEW_KEY = 'VRCX_gameLogViewMode';
	// Settings > Interface > Table Entries Settings.
	const MAX_TABLE_SIZE = $derived(appearance.maxTableSize.value);
	const SEARCH_LIMIT = $derived(appearance.searchLimit.value);

	let viewMode = $state<'sessions' | 'table'>(readSetting<string>(VIEW_KEY, 'table') === 'sessions' ? 'sessions' : 'table');
	let rows = $state<GameLogRow[]>([]);
	let loading = $state(false);
	let loadError = $state('');
	let filters = $state<string[]>(readSetting<string[]>(FILTERS_KEY, []));
	let vip = $state(readSetting(VIP_KEY, false));
	let search = $state('');
	let searched = '';
	let confirming = $state<Confirming | undefined>();
	let infoLocation = $state<string | undefined>();
	let generation = 0;

	async function load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		const mine = ++generation;
		loading = true;
		searched = search;
		try {
			const term = search.trim();
			const found = await api.get<GameLogRow[]>(accountPath(account, '/game-log'), {
				types: filters.join(','),
				vip,
				limit: term ? SEARCH_LIMIT : MAX_TABLE_SIZE,
				...(term ? { search: term } : {})
			});
			if (mine !== generation) return;
			rows = found;
			loadError = '';
		} catch (err) {
			if (mine === generation) loadError = ApiError.from(err).message;
		} finally {
			if (mine === generation) loading = false;
		}
	}

	function setFilters(value: string[]) {
		filters = value;
		storeSetting(FILTERS_KEY, value);
		load();
	}

	function setVip(value: boolean) {
		vip = value;
		storeSetting(VIP_KEY, value);
		load();
	}

	function setView(value: string) {
		if (value !== 'sessions' && value !== 'table') return;
		viewMode = value;
		storeSetting(VIEW_KEY, value);
	}

	function searchChanged() {
		if (search !== searched) load();
	}

	async function remove(row: GameLogRow) {
		rows = rows.filter((r) => r.key !== row.key);
		confirming = undefined;
		try {
			await api.delete(ownPath(`/game-log/${encodeURIComponent(row.key)}`));
		} catch (err) {
			loadError = ApiError.from(err).message;
			load();
		}
	}

	// The original's openExternalLink: asks before leaving.
	const openLink = (url: string) => openExternalLink(url);
	const copy = (text: string) => navigator.clipboard.writeText(text);
	const showPrevious = (location: string) => (infoLocation = location);

	$effect(() => {
		if (!vrchatAuth.ownerUserId || viewMode !== 'table') return;
		untrack(load);
		return watchLive(['visit', 'gameLog'], load, 1000);
	});

	const columns: Column<GameLogRow>[] = [
		{ id: 'spacer', size: 20, minSize: 0, maxSize: 20 },
		{ id: 'created_at', get label() { return m.table_feed_date(); }, size: 140, sortable: true, value: (row) => row.createdAt, cell: dateCell },
		{ id: 'type', get label() { return m.table_feed_type(); }, size: 150, cell: typeCell },
		{ id: 'displayName', get label() { return m.table_feed_user(); }, size: 200, cell: userCell },
		{ id: 'detail', get label() { return m.table_feed_detail(); }, minSize: 150, stretch: true, cell: detailCell },
		{ id: 'action', get label() { return m.table_import_action(); }, size: 90, minSize: 90, maxSize: 90, class: 'text-right', cell: actionCell }
	];
</script>

{#snippet dateCell(row: GameLogRow)}
	<DateCell value={row.createdAt} />
{/snippet}

{#snippet typeCell(row: GameLogRow)}
	<Badge variant="outline" class="text-muted-foreground">
		{#if row.location && row.type !== 'Location'}
			<a class="cursor-pointer" href={worldHref(row.location)}>{LABELS[row.type] ?? row.type}</a>
		{:else}
			<span>{LABELS[row.type] ?? row.type}</span>
		{/if}
	</Badge>
{/snippet}

{#snippet userCell(row: GameLogRow)}
	<span class="cursor-pointer">
		{#if row.displayName}
			{#if row.userId}
				<a class="table-user mr-1 cursor-pointer" href={userHref(row.userId)}>{row.displayName}</a>
			{:else}
				<button type="button" class="table-user mr-1 cursor-pointer" onclick={() => lookupUser(row)}>{row.displayName}</button>
			{/if}
		{/if}
		{#if row.isFriend}<span>{row.isFavorite ? '⭐' : '💚'}</span>{/if}
	</span>
{/snippet}

{#snippet tip(text: string | null | undefined)}
	<Tooltip.Root>
		<Tooltip.Trigger class="block w-full min-w-0 truncate text-left">{text}</Tooltip.Trigger>
		<Tooltip.Content side="bottom">{text}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet detailCell(row: GameLogRow)}
	{#if row.type === 'Location'}
		<div class="w-full min-w-0 truncate">
			<LocationMenu location={row.location} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} onpreviousinstances={showPrevious} />
		</div>
	{:else if row.type === 'PortalSpawn'}
		<div class="w-full min-w-0 truncate">
			<LocationMenu location={row.instanceId} hint={row.worldName ?? ''} grouphint={row.groupName ?? ''} onpreviousinstances={showPrevious} />
		</div>
	{:else if row.type === 'Event'}
		<ContextMenu.Root>
			<ContextMenu.Trigger class="block min-w-0">{@render tip(row.data)}</ContextMenu.Trigger>
			<ContextMenu.Content>
				<ContextMenu.Item onclick={() => copy(row.data ?? '')}><Copy class="size-4" />{m.view_favorite_copy()}</ContextMenu.Item>
			</ContextMenu.Content>
		</ContextMenu.Root>
	{:else if row.type === 'External'}
		{@render tip(row.message)}
	{:else if row.type === 'VideoPlay'}
		{@const showLink = row.videoId !== 'PopcornPalace' && !!row.videoUrl}
		{@const label = row.videoName || row.videoUrl}
		<ContextMenu.Root>
			<ContextMenu.Trigger class="block min-w-0">
				<Tooltip.Root>
					<Tooltip.Trigger class="block w-full min-w-0 cursor-pointer truncate text-left">
						{#snippet child({ props })}
							<span {...props}>
								{#if row.videoId}<span class="mr-1.5">{row.videoId}:</span>{/if}
								{#if showLink}
									<button type="button" class="cursor-pointer" onclick={() => openLink(row.videoUrl!)}>{label}</button>
								{:else}
									<span>{row.videoName}</span>
								{/if}
							</span>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="bottom">{row.videoId ? `${row.videoId}: ${label}` : label}</Tooltip.Content>
				</Tooltip.Root>
			</ContextMenu.Trigger>
			<ContextMenu.Content>
				{#if showLink}
					<ContextMenu.Item onclick={() => openLink(row.videoUrl!)}><ExternalLink class="size-4" />{m.common_actions_open_link()}</ContextMenu.Item>
					<ContextMenu.Separator />
				{/if}
				<ContextMenu.Item onclick={() => copy(row.videoUrl || row.videoName || '')}><Copy class="size-4" />{m.view_favorite_copy()}</ContextMenu.Item>
			</ContextMenu.Content>
		</ContextMenu.Root>
	{:else if row.type === 'ImageLoad' || row.type === 'StringLoad'}
		<ContextMenu.Root>
			<ContextMenu.Trigger class="block min-w-0">
				<Tooltip.Root>
					<Tooltip.Trigger>
						{#snippet child({ props })}
							<button {...props} type="button" class="block w-full min-w-0 cursor-pointer truncate text-left" onclick={() => openLink(row.resourceUrl ?? '')}>{row.resourceUrl}</button>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="bottom">{row.resourceUrl}</Tooltip.Content>
				</Tooltip.Root>
			</ContextMenu.Trigger>
			<ContextMenu.Content>
				<ContextMenu.Item onclick={() => openLink(row.resourceUrl ?? '')}><ExternalLink class="size-4" />{m.common_actions_open_link()}</ContextMenu.Item>
				<ContextMenu.Separator />
				<ContextMenu.Item onclick={() => copy(row.resourceUrl ?? '')}><Copy class="size-4" />{m.view_favorite_copy()}</ContextMenu.Item>
			</ContextMenu.Content>
		</ContextMenu.Root>
	{/if}
{/snippet}

{#snippet actionCell(row: GameLogRow)}
	<div class="flex items-center justify-end gap-2">
		{#if !UNACTIONABLE.has(row.type)}
			<LogDeleteButton
				class="cursor-pointer"
				label={m.view_notification_actions_delete_log()}
				remove={() => remove(row)}
				confirm={() => (confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_delete_log(); }, run: () => remove(row) })}
			/>
		{/if}
		{#if row.type === 'Location'}
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<button {...props} type="button" aria-label={m.dialog_user_info_header()} class="inline-flex h-6 cursor-pointer items-center justify-center text-muted-foreground hover:text-foreground" onclick={() => showPrevious(row.location ?? '')}>
							<FileText class="size-4" />
						</button>
					{/snippet}
				</Tooltip.Trigger>
				<Tooltip.Content side="top">{m.dialog_user_info_header()}</Tooltip.Content>
			</Tooltip.Root>
		{/if}
	</div>
{/snippet}

{#snippet viewToggle()}
	<ToggleGroup.Root type="single" variant="outline" size="sm" value={viewMode} onValueChange={setView} class="shrink-0">
		<ToggleGroup.Item value="sessions" class="px-2" aria-label={m.view_game_log_sessions_switch_to_sessions()} title={m.view_game_log_sessions_switch_to_sessions()}><Logs class="size-4" /></ToggleGroup.Item>
		<ToggleGroup.Item value="table" class="px-2" aria-label={m.view_game_log_sessions_switch_to_table()} title={m.view_game_log_sessions_switch_to_table()}><Table2 class="size-4" /></ToggleGroup.Item>
	</ToggleGroup.Root>
{/snippet}

<svelte:head>
	<title>Game Log · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="flex h-full min-h-0 w-full flex-col">
		{#if !vrchatAuth.ownerUserId}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see your game log.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else if viewMode === 'sessions'}
			<GameLogSessions leading={viewToggle} onpreviousinstances={showPrevious} />
		{:else}
			<DataTable
				{rows}
				{columns}
				{loading}
				rowKey={(row) => row.key}
				persistKey="gameLog"
				filtered={filters.length > 0 || vip || !!search.trim()}
				initialSort={{ id: 'created_at', desc: true }}
			>
				{#snippet toolbar()}
					<div class="flex items-center">
						<div class="mr-2 flex flex-none items-center gap-2">
							{@render viewToggle()}
							<FavoritesOnlyToggle pressed={vip} onPressedChange={setVip} />
						</div>
						<MultiSelect class="w-full flex-1" values={filters} options={FILTER_TYPES} placeholder={m.view_my_avatars_filter()} onchange={setFilters} />
						<Input
							class="ml-2 h-8 flex-[0.4]"
							placeholder={m.view_favorite_worlds_search()}
							bind:value={search}
							onchange={searchChanged}
							onkeyup={(event) => {
								if (event.key === 'Enter') searchChanged();
							}}
						/>
					</div>
					{#if loadError}<p class="mt-2 text-sm text-destructive">Couldn't load game log: {loadError}</p>{/if}
				{/snippet}
			</DataTable>
		{/if}
	</div>
</Tooltip.Provider>

<ActionDialogs bind:confirming />

{#if infoLocation}
	<PreviousInstanceInfo bind:location={infoLocation} />
{/if}
