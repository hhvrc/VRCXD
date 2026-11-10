<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import DateCell from '#lib/components/common/date-cell.svelte';
	import LogDeleteButton from '#lib/components/common/log-delete-button.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { userHref } from '#lib/api/links.js';
	import type { FriendLogEntry } from '#lib/api/types.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { LogTable } from '#lib/activity/log-table.svelte.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Friend Log over GET /1/accounts/{id}/friend-log. Deleting a row hides it from
	// this view on the server; the history behind it stays in the database.
	const TYPES = [
		{ value: 'Friend', get label() { return m.view_friend_log_filters_Friend(); } },
		{ value: 'Unfriend', get label() { return m.view_friend_log_filters_Unfriend(); } },
		{ value: 'FriendRequest', get label() { return m.view_friend_log_filters_FriendRequest(); } },
		{ value: 'CancelFriendRequest', get label() { return m.view_friend_log_filters_CancelFriendRequest(); } },
		{ value: 'DisplayName', get label() { return m.view_friend_log_filters_DisplayName(); } },
		{ value: 'TrustLevel', get label() { return m.view_friend_log_filters_TrustLevel(); } }
	];
	const table = new LogTable<FriendLogEntry>(
		TYPES,
		'VRCX_friendLogTableFilters',
		'/friend-log',
		(row, term) => !(tableSettings.hideUnfriends && row.type === 'Unfriend') && (!term || row.displayName.toLowerCase().includes(term))
	);

	async function remove(row: FriendLogEntry) {
		table.rows = table.rows.filter((r) => r.key !== row.key);
		table.confirming = undefined;
		try {
			await api.delete(ownPath(`/friend-log/${encodeURIComponent(row.key)}`));
		} catch (err) {
			table.error = ApiError.from(err).message;
			table.load();
		}
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(table.load);
		return watchLive(['friendship', 'user'], table.load, 750);
	});

	const columns: Column<FriendLogEntry>[] = [
		{ id: 'spacer', size: 20, minSize: 0, maxSize: 20 },
		{ id: 'created_at', get label() { return m.table_feed_date(); }, size: 120, sortable: true, value: (row) => row.createdAt, cell: dateCell },
		{ id: 'type', get label() { return m.table_feed_type(); }, size: 160, cell: typeCell },
		{ id: 'displayName', get label() { return m.table_feed_user(); }, size: 260, minSize: 80, stretch: true, cell: userCell },
		{ id: 'action', get label() { return m.table_import_action(); }, size: 80, maxSize: 80, class: 'w-[80px] max-w-[80px] text-right', cell: actionCell },
		{ id: 'trailing', size: 5, enableResizing: false }
	];
</script>

{#snippet dateCell(row: FriendLogEntry)}
	<DateCell value={row.createdAt} />
{/snippet}

{#snippet typeCell(row: FriendLogEntry)}
	<Badge variant="outline" class="text-muted-foreground">{table.typeLabel(row.type)}</Badge>
{/snippet}

{#snippet userCell(row: FriendLogEntry)}
	<span class="block w-full cursor-pointer wrap-break-word whitespace-normal">
		{#if row.type === 'DisplayName'}
			<span class="mr-1">{row.previousDisplayName}<ArrowRight class="mx-1 inline size-3" /></span>
		{/if}
		<a class="cursor-pointer pr-2.5" href={userHref(row.userId)}>{row.displayName || row.userId}</a>
		{#if row.type === 'TrustLevel'}
			<span class="text-muted-foreground">({row.previousTrustLevel}<ArrowRight class="mx-1 inline size-3" />{row.trustLevel})</span>
		{/if}
	</span>
{/snippet}

{#snippet actionCell(row: FriendLogEntry)}
	<div class="flex justify-end">
		<LogDeleteButton
			class="cursor-pointer"
			label={m.view_notification_actions_delete_log()}
			remove={() => remove(row)}
			confirm={() => (table.confirming = { get description() { return m.confirm_delete_log(); }, run: () => remove(row) })}
		/>
	</div>
{/snippet}

<svelte:head>
	<title>Friend Log · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="flex h-full min-h-0 w-full flex-col">
		{#if !vrchatAuth.ownerUserId}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see your friend log.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else}
			<DataTable
				rows={table.shown}
				{columns}
				loading={table.loading}
				rowKey={(row) => row.key}
				persistKey="friendLog"
				filtered={table.rows.length > 0}
			>
				{#snippet toolbar()}
					<div class="flex items-center">
						<MultiSelect class="w-full flex-1" values={table.types} options={TYPES} placeholder={m.view_my_avatars_filter()} onchange={table.setTypes} />
						<Input class="ml-2 h-8 flex-[0.4]" placeholder={m.view_favorite_worlds_search()} bind:value={table.search} />
					</div>
					{#if table.error}<p class="mt-2 text-sm text-destructive">Couldn't load friend log: {table.error}</p>{/if}
				{/snippet}
			</DataTable>
		{/if}
	</div>
</Tooltip.Provider>

<ActionDialogs bind:confirming={table.confirming} />
