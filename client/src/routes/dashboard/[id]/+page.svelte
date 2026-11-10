<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import Plus from '@lucide/svelte/icons/plus';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import DashboardPanel from '#lib/components/dashboard/dashboard-panel.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Resizable from '#lib/components/ui/resizable/index.js';
	import { cloneRows, dashboards, type DashboardRow, type PanelValue } from '#lib/nav/dashboards.svelte.js';

	// The original's Dashboard view: rows of one or two panels, resizable when viewing, edited as a
	// copy that Save writes back.
	const id = $derived(page.params.id ?? '');
	const dashboard = $derived(dashboards.get(id));

	let editing = $state(false);
	let showAddRow = $state(false);
	let editRows = $state<DashboardRow[]>([]);
	let editName = $state('');
	let tip = $state('');
	let confirming = $state<Confirming | undefined>();

	const rows = $derived(editing ? editRows : (dashboard?.rows ?? []));

	$effect(() => {
		void id;
		editing = false;
		showAddRow = false;
	});

	$effect(() => {
		if (!dashboard) goto('/feed', { replaceState: true });
	});

	$effect(() => {
		if (dashboards.editingId && dashboards.editingId === id) {
			dashboards.editingId = null;
			startEditing();
		}
	});

	function startEditing() {
		if (!dashboard) return;
		editRows = cloneRows($state.snapshot(dashboard.rows));
		editName = dashboard.name;
		editing = true;
	}

	function addRow(count: number, direction: 'horizontal' | 'vertical' = 'horizontal') {
		editRows.push({ panels: Array(count).fill(null), direction });
		showAddRow = false;
	}

	function removePanel(rowIndex: number, panelIndex: number) {
		const row = editRows[rowIndex];
		if (!row) return;
		if (row.panels.length <= 1) editRows.splice(rowIndex, 1);
		else row.panels.splice(panelIndex, 1);
	}

	function livePanel(rowIndex: number, panelIndex: number, value: PanelValue) {
		if (!dashboard?.rows[rowIndex]) return;
		const next = cloneRows($state.snapshot(dashboard.rows));
		next[rowIndex].panels[panelIndex] = value;
		dashboards.update(id, { rows: next });
	}

	function save() {
		const firstSave = dashboards.list.length === 1 && !dashboard?.rows.length;
		dashboards.update(id, { name: editName.trim() || dashboard?.name || 'Dashboard', rows: $state.snapshot(editRows) });
		editing = false;
		if (firstSave) {
			tip = 'You can hide the New Dashboard button in Settings > Interface > Navigation.';
			setTimeout(() => (tip = ''), 6000);
		}
	}

	function remove() {
		confirming = {
			get title() { return m.nav_menu_delete_dashboard(); },
			get description() { return m.dashboard_confirmations_delete_description(); },
			destructive: true,
			run: async () => {
				dashboards.remove(id);
				await goto('/feed', { replaceState: true });
			}
		};
	}
</script>

<svelte:head>
	<title>{dashboard?.name ?? 'Dashboard'} · VRCXD</title>
</svelte:head>

<div class="flex h-full min-h-0 flex-col gap-3">
	{#if editing}
		<div class="flex items-center gap-2 rounded-md border bg-card px-3 py-2">
			<Input bind:value={editName} placeholder={m.dashboard_toolbar_name_placeholder()} class="mx-2 h-7 max-w-50 text-sm" />
			<div class="flex gap-2">
				<Button variant="secondary" size="sm" onclick={() => (editing = false)}>{m.view_login_qr_cancel()}</Button>
				<Button variant="destructive" size="sm" onclick={remove}>{m.dialog_world_actions_delete()}</Button>
			</div>
			<Button class="ml-auto" size="sm" onclick={save}>{m.dialog_config_json_save()}</Button>
		</div>
	{/if}
	{#if tip}<p class="rounded-md border bg-card px-3 py-2 text-sm">{tip}</p>{/if}

	<div class="flex min-h-0 flex-1 flex-col gap-2 overflow-y-auto">
		{#if rows.length && !editing}
			{#key id}
				<Resizable.PaneGroup direction="vertical" autoSaveId={`dashboard-${id}`} class="min-h-0 flex-1">
					{#each rows as row, rowIndex (rowIndex)}
						<Resizable.Pane defaultSize={100 / rows.length} minSize={10}>
							<div class="relative h-full min-h-45">
								{#if row.panels.length === 2}
									<Resizable.PaneGroup direction={row.direction === 'vertical' ? 'vertical' : 'horizontal'} autoSaveId={`dashboard-${id}-row-${rowIndex}`} class="h-full min-h-45">
										<Resizable.Pane defaultSize={50} minSize={20}>
											<DashboardPanel panel={row.panels[0]} class="h-full" onselect={(v) => livePanel(rowIndex, 0, v)} />
										</Resizable.Pane>
										<Resizable.Handle />
										<Resizable.Pane defaultSize={50} minSize={20}>
											<DashboardPanel panel={row.panels[1]} class="h-full" onselect={(v) => livePanel(rowIndex, 1, v)} />
										</Resizable.Pane>
									</Resizable.PaneGroup>
								{:else}
									<div class="flex h-full"><DashboardPanel panel={row.panels[0]} class="h-full" onselect={(v) => livePanel(rowIndex, 0, v)} /></div>
								{/if}
							</div>
						</Resizable.Pane>
						{#if rowIndex < rows.length - 1}<Resizable.Handle />{/if}
					{/each}
				</Resizable.PaneGroup>
			{/key}
		{:else if editing}
			{#each editRows as row, rowIndex (rowIndex)}
				<div class="relative h-full min-h-45">
					<div class={['flex h-full gap-2', row.direction === 'vertical' ? 'flex-col' : 'flex-row']}>
						{#each row.panels as panel, panelIndex (panelIndex)}
							<DashboardPanel
								{panel}
								editing
								showRemove
								class={row.panels.length === 1 ? 'w-full' : row.direction === 'vertical' ? 'h-1/2' : 'w-1/2'}
								onselect={(v) => (editRows[rowIndex].panels[panelIndex] = v)}
								onremove={() => removePanel(rowIndex, panelIndex)}
							/>
						{/each}
					</div>
				</div>
			{/each}
			<div
				class={[
					'mt-auto flex min-h-20 flex-1 justify-center rounded-md border-2 border-dashed border-muted-foreground/20 text-muted-foreground transition-colors hover:border-primary/40 hover:bg-primary/5',
					showAddRow ? 'items-start p-4' : 'cursor-pointer items-center'
				]}
				role="button"
				tabindex="0"
				onclick={() => (showAddRow = !showAddRow)}
				onkeydown={(e) => e.key === 'Enter' && (showAddRow = !showAddRow)}
			>
				{#if showAddRow}
					<div class="flex flex-wrap items-center gap-3">
						<span class="text-xs text-muted-foreground">Add Row:</span>
						<button type="button" title={m.dashboard_actions_add_full_row()} class="flex h-10 w-16 cursor-pointer items-center justify-center rounded-md border-2 border-dashed border-muted-foreground/30 transition-colors hover:border-primary/50 hover:bg-primary/5" onclick={(e) => (e.stopPropagation(), addRow(1))}>
							<div class="h-6 w-12 rounded bg-muted-foreground/20"></div>
						</button>
						<button type="button" title={m.dashboard_actions_add_split_row()} class="flex h-10 w-16 cursor-pointer items-center justify-center gap-1 rounded-md border-2 border-dashed border-muted-foreground/30 transition-colors hover:border-primary/50 hover:bg-primary/5" onclick={(e) => (e.stopPropagation(), addRow(2))}>
							<div class="h-6 w-5 rounded bg-muted-foreground/20"></div>
							<div class="h-6 w-5 rounded bg-muted-foreground/20"></div>
						</button>
						<button type="button" title={m.dashboard_actions_add_vertical_row()} class="flex h-10 w-16 cursor-pointer items-center justify-center gap-1 rounded-md border-2 border-dashed border-muted-foreground/30 transition-colors hover:border-primary/50 hover:bg-primary/5" onclick={(e) => (e.stopPropagation(), addRow(2, 'vertical'))}>
							<div class="flex flex-col gap-0.5">
								<div class="h-2.5 w-10 rounded bg-muted-foreground/20"></div>
								<div class="h-2.5 w-10 rounded bg-muted-foreground/20"></div>
							</div>
						</button>
					</div>
				{:else}
					<Plus class="size-6 opacity-50" />
				{/if}
			</div>
		{:else}
			<div class="flex flex-1 items-center justify-center rounded-md border border-dashed text-muted-foreground">
				<div class="flex flex-col items-center gap-3">
					<p>{m.dashboard_empty()}</p>
					<Button onclick={startEditing}>{m.dashboard_actions_start_editing()}</Button>
				</div>
			</div>
		{/if}
	</div>
</div>

<ActionDialogs bind:confirming />
