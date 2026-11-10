<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { auditLogTypeName, groupModeration as M, type AuditLog } from '#lib/groups/group-moderation.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';

	// The original's GroupModerationLogsTab: the type filter applies on refresh, the search narrows
	// the loaded entries by description.
	let { onexport }: { onexport: () => void } = $props();

	const typeOptions = $derived(M.auditLogTypes.map((type) => ({ value: type, label: auditLogTypeName(type) })));
	const shown = $derived.by(() => {
		const q = M.logsSearch.trim().toLowerCase();
		return q ? M.logs.filter((r) => (r.description ?? '').toLowerCase().includes(q)) : M.logs;
	});

	function hasData(data: unknown): boolean {
		return !!data && typeof data === 'object' && Object.keys(data).length > 0;
	}

	const columns: Column<AuditLog>[] = [
		{ id: 'created_at', get label() { return m.dialog_group_member_moderation_created_at(); }, size: 170, sortable: true, value: (row) => row.created_at ?? '', cell: createdCell },
		{ id: 'eventType', get label() { return m.dialog_group_member_moderation_type(); }, size: 190, sortable: true, value: (row) => row.eventType ?? '', cell: typeCell },
		{ id: 'actorDisplayName', get label() { return m.dialog_group_member_moderation_display_name(); }, size: 160, sortable: true, value: (row) => row.actorDisplayName ?? '', cell: actorCell },
		{ id: 'description', get label() { return m.dialog_group_member_moderation_description(); }, stretch: true, cell: descriptionCell },
		{ id: 'data', get label() { return m.dialog_group_member_moderation_data(); }, cell: dataCell }
	];
</script>

{#snippet createdCell(row: AuditLog)}
	<span>{formatDateFilter(row.created_at, 'long')}</span>
{/snippet}

{#snippet typeCell(row: AuditLog)}
	<span>{row.eventType}</span>
{/snippet}

{#snippet actorCell(row: AuditLog)}
	<button type="button" class="cursor-pointer truncate text-left" onclick={() => row.actorId && userDialog.open(row.actorId)}>{row.actorDisplayName}</button>
{/snippet}

{#snippet descriptionCell(row: AuditLog)}
	<span>
		{#if row.targetId?.startsWith('wrld_')}<Location location={row.targetId} />{/if}
		<span>{row.description}</span>
	</span>
{/snippet}

{#snippet dataCell(row: AuditLog)}
	<span>{hasData(row.data) ? JSON.stringify(row.data) : ''}</span>
{/snippet}

<div class="flex h-full min-h-0 flex-col gap-2">
	<div class="flex items-center gap-2">
		<Button class="rounded-full" variant="outline" size="icon-sm" aria-label={m.dialog_note_export_refresh()} disabled={M.loading} onclick={() => M.getAllGroupLogs(M.id, M.selectedAuditLogTypes)}>
			{#if M.loading}<Spinner />{:else}<RefreshCw />{/if}
		</Button>
		<span class="shrink-0 text-sm tabular-nums">{M.logs.length}</span>
		<MultiSelect values={M.selectedAuditLogTypes} options={typeOptions} placeholder={m.dialog_group_member_moderation_filter_type()} class="h-8 w-64 shrink-0" onchange={(values) => (M.selectedAuditLogTypes = values)} />
		<ClearableInput bind:value={M.logsSearch} class="flex-1" placeholder={m.view_favorite_worlds_search()} />
		<Button size="sm" variant="outline" onclick={onexport}>{m.dialog_group_member_moderation_export_logs()}</Button>
	</div>
	<DataTable rows={shown} {columns} loading={M.loading} persistKey="group-moderation:logs" pageSize={15} rowKey={(row) => String(row.id ?? `${row.created_at ?? ''}:${row.eventType ?? ''}`)} />
</div>
