<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import DateCell from '#lib/components/common/date-cell.svelte';
	import LogDeleteButton from '#lib/components/common/log-delete-button.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { userHref } from '#lib/api/links.js';
	import type { PlayerModeration } from '#lib/api/types.js';
	import { LogTable } from '#lib/activity/log-table.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Moderation view over the account's player moderations, as the sync mirrors
	// them. Removing one goes to VRChat first, like the original.
	const TYPES = [
		{ value: 'block', get label() { return m.view_moderation_filters_block(); } },
		{ value: 'unblock', get label() { return m.view_moderation_filters_unblock(); } },
		{ value: 'mute', get label() { return m.view_moderation_filters_mute(); } },
		{ value: 'unmute', get label() { return m.view_moderation_filters_unmute(); } },
		{ value: 'interactOn', get label() { return m.view_moderation_filters_interactOn(); } },
		{ value: 'interactOff', get label() { return m.view_moderation_filters_interactOff(); } },
		{ value: 'muteChat', get label() { return m.view_moderation_filters_muteChat(); } },
		{ value: 'unmuteChat', get label() { return m.view_moderation_filters_unmuteChat(); } }
	];
	const table = new LogTable<PlayerModeration>(
		TYPES,
		'VRCX_playerModerationTableFilters',
		'/moderations',
		(row, term) => !term || row.sourceDisplayName.toLowerCase().includes(term) || row.targetDisplayName.toLowerCase().includes(term)
	);

	async function refresh() {
		table.loading = true;
		try {
			await api.post(ownPath('/moderations/refresh'));
			await table.load();
		} catch (err) {
			table.error = ApiError.from(err).message;
			table.loading = false;
		}
	}

	async function remove(row: PlayerModeration) {
		table.confirming = undefined;
		try {
			await api.delete(ownPath(`/moderations/${encodeURIComponent(row.targetUserId)}/${row.type}`));
			table.rows = table.rows.filter((r) => r.id !== row.id);
			table.error = '';
		} catch (err) {
			table.error = ApiError.from(err).message;
		}
	}

	function clearType(type: string) {
		table.confirming = {
			get title() { return m.view_moderation_clear_confirm_title(); },
			description: `WARNING: This will remove all "${type}" moderations from your account and cannot be undone. Continue?`,
			confirmText: 'Clear All',
			destructive: true,
			run: async () => {
				table.confirming = undefined;
				try {
					await api.delete(ownPath(`/moderations/${type}`));
					await refresh();
				} catch (err) {
					table.error = ApiError.from(err).message;
				}
			}
		};
	}

	$effect(() => {
		if (vrchatAuth.ownerUserId) untrack(table.load);
	});

	const columns: Column<PlayerModeration>[] = [
		{ id: 'spacer', size: 20, minSize: 0, maxSize: 20 },
		{ id: 'created', get label() { return m.table_moderation_date(); }, size: 120, sortable: true, value: (row) => row.createdAt, cell: dateCell },
		{ id: 'type', get label() { return m.table_moderation_type(); }, size: 140, cell: typeCell },
		{ id: 'sourceDisplayName', get label() { return m.table_moderation_source(); }, size: 120, class: 'overflow-hidden', cell: sourceCell },
		{ id: 'targetDisplayName', get label() { return m.table_moderation_target(); }, size: 200, minSize: 80, stretch: true, cell: targetCell },
		{ id: 'action', get label() { return m.table_moderation_action(); }, size: 80, minSize: 80, maxSize: 80, class: 'text-right', cell: actionCell },
		{ id: 'trailing', size: 5, enableResizing: false }
	];
</script>

{#snippet dateCell(row: PlayerModeration)}
	<DateCell value={row.createdAt} />
{/snippet}

{#snippet typeCell(row: PlayerModeration)}
	<Badge variant="outline" class="text-muted-foreground">{table.typeLabel(row.type)}</Badge>
{/snippet}

{#snippet sourceCell(row: PlayerModeration)}
	<a class="block w-full min-w-0 cursor-pointer truncate pr-2.5" href={userHref(row.sourceUserId)}>{row.sourceDisplayName}</a>
{/snippet}

{#snippet targetCell(row: PlayerModeration)}
	<a class="block w-full cursor-pointer wrap-break-word whitespace-normal pr-2.5" href={userHref(row.targetUserId)}>{row.targetDisplayName}</a>
{/snippet}

{#snippet actionCell(row: PlayerModeration)}
	{#if row.sourceUserId === vrchatAuth.ownerUserId}
		<div class="flex justify-end">
			<LogDeleteButton
				label="Delete moderation"
				remove={() => remove(row)}
				confirm={() => (table.confirming = { description: `Continue? Moderation ${row.type}`, run: () => remove(row) })}
			/>
		</div>
	{/if}
{/snippet}

<svelte:head>
	<title>Moderation · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="flex h-full min-h-0 w-full flex-col">
		{#if !vrchatAuth.ownerUserId}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see your moderations.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else}
			<div class="mb-4 flex items-center">
				<MultiSelect class="w-full flex-1" values={table.types} options={TYPES} placeholder={m.view_my_avatars_filter()} onchange={table.setTypes} />
				<Input class="mx-2.5 h-8 w-37.5 flex-[0.4]" placeholder={m.view_favorite_worlds_search()} bind:value={table.search} />
				<DropdownMenu.Root>
					<DropdownMenu.Trigger disabled={table.loading}>
						{#snippet child({ props })}
							<Button {...props} class="mr-2.5 rounded-full" variant="ghost" size="icon-sm" aria-label={m.view_moderation_clear_type_placeholder()}><Trash2 class="size-4" /></Button>
						{/snippet}
					</DropdownMenu.Trigger>
					<DropdownMenu.Content align="end" class="w-55">
						<DropdownMenu.Label>{m.view_moderation_clear_type_placeholder()}</DropdownMenu.Label>
						<DropdownMenu.Separator />
						{#each TYPES as type (type.value)}
							<DropdownMenu.Item onSelect={() => clearType(type.value)}>{type.label}</DropdownMenu.Item>
						{/each}
					</DropdownMenu.Content>
				</DropdownMenu.Root>
				<Tooltip.Root>
					<Tooltip.Trigger>
						{#snippet child({ props })}
							<Button {...props} class="rounded-full" variant="ghost" size="icon-sm" disabled={table.loading} aria-label={m.dialog_note_export_refresh()} onclick={refresh}>
								{#if table.loading}<Spinner />{:else}<RefreshCw />{/if}
							</Button>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="bottom">{m.dialog_note_export_refresh()}</Tooltip.Content>
				</Tooltip.Root>
			</div>
			{#if table.error}<p class="mb-2 text-sm text-destructive">{table.error}</p>{/if}
			<DataTable
				rows={table.shown}
				{columns}
				loading={table.loading}
				rowKey={(row) => row.id}
				persistKey="moderation"
				initialSort={{ id: 'created', desc: true }}
				filtered={table.rows.length > 0}
			/>
		{/if}
	</div>
</Tooltip.Provider>

<ActionDialogs bind:confirming={table.confirming} />
