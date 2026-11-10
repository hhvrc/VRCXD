<script lang="ts" module>
	import type { Snippet } from 'svelte';

	export type Column<T> = {
		id: string;
		/** What the header menu calls it. A column without one is a fixed spacer: no menu entry and no dragging. */
		label?: string;
		size?: number;
		minSize?: number;
		maxSize?: number;
		/** Takes the width left over instead of having its own. */
		stretch?: boolean;
		/** Gets the sort button in its header and sorts on `value`. */
		sortable?: boolean;
		value?: (row: T) => unknown;
		/** Own sort order, for when comparing `value` isn't enough. */
		compare?: (a: T, b: T) => number;
		/** The first click sorts descending. */
		descFirst?: boolean;
		cell?: Snippet<[T]>;
		header?: Snippet;
		class?: string;
		headerClass?: string;
		defaultHidden?: boolean;
		disableVisibilityToggle?: boolean;
		disableReorder?: boolean;
		enableResizing?: boolean;
	};

	export type { TableSort } from '#lib/settings/table-state.js';

	const DEFAULT_MIN_SIZE = 20;

	function compare(a: unknown, b: unknown): number {
		if (a === b) return 0;
		if (a === null || a === undefined || a === '') return 1;
		if (b === null || b === undefined || b === '') return -1;
		if (typeof a === 'number' && typeof b === 'number') return a - b;
		return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
	}
</script>

<script lang="ts" generics="T">
	import { LOADING_OVERLAY } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import TablePager from '#lib/components/common/table-pager.svelte';
	import { untrack } from 'svelte';
	import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import {
		defaultVisibility,
		filterOrderByColumns,
		filterSizingByColumns,
		filterSortingByColumns,
		filterVisibilityByColumns,
		nearestPageSize,
		readTableState,
		removeTableState,
		restoredPageSize,
		restoredSorting,
		writeTableState,
		type PersistedTable,
		type TableSort
	} from '#lib/settings/table-state.js';

	// The original's DataTableLayout and useVrcxVueTable: sorting, paging, a header menu to show and
	// hide columns, dragging and resizing columns, all remembered per table under the same
	// vrcx:table:<key> storage name, page size included.

	let {
		rows,
		columns,
		rowKey,
		persistKey,
		loading = false,
		filtered = false,
		initialSort,
		pageSize: initialPageSize,
		paged = true,
		resetKey,
		class: className = '',
		toolbar,
		rowMenu,
		expandedRow,
		isExpanded,
		onRowClick,
		rowClass,
		empty
	}: {
		rows: T[];
		columns: Column<T>[];
		rowKey: (row: T) => string;
		persistKey: string;
		loading?: boolean;
		/** Filters are narrowing the rows, so an empty table says no match rather than no data. */
		filtered?: boolean;
		initialSort?: TableSort;
		/** The page size until this table has its own saved one; the Table Page Size setting when left out. */
		pageSize?: number;
		/** False shows every row with no page controls, as the Player List does. */
		paged?: boolean;
		/** When this changes (a new search, say), the table goes back to its first page and initial sort. */
		resetKey?: unknown;
		class?: string;
		toolbar?: Snippet;
		rowMenu?: Snippet<[T]>;
		expandedRow?: Snippet<[T]>;
		isExpanded?: (row: T) => boolean;
		onRowClick?: (row: T) => void;
		rowClass?: (row: T) => string;
		empty?: Snippet;
	} = $props();

	const initial = untrack(() => readTableState(persistKey));

	function persist(patch: PersistedTable) {
		writeTableState(persistKey, patch);
	}

	// One sort at a time here, so the first saved one for a column the table still has.
	let sorting = $state<TableSort | undefined>(untrack(() => filterSortingByColumns(restoredSorting(initial, initialSort ? [initialSort] : []), columns)[0]));
	let sizing = $state<Record<string, number>>(initial.columnSizing ?? {});
	let order = $state<string[]>(Array.isArray(initial.columnOrder) ? initial.columnOrder : []);
	let visibility = $state<Record<string, boolean>>(initial.columnVisibility ?? untrack(() => defaultVisibility(columns)));
	let locked = $state(initial.columnOrderLocked === true);
	let pageSize = $state(untrack(() => restoredPageSize(initial, initialPageSize ?? tableSettings.pageSize)));
	let pageNumber = $state(1);
	let lastResetKey = untrack(() => resetKey);
	$effect(() => {
		const key = resetKey;
		if (key === lastResetKey) return;
		lastResetKey = key;
		untrack(() => {
			pageNumber = 1;
			sorting = initialSort;
		});
	});
	let scroller = $state<HTMLElement | null>(null);

	// Saved order first, then any column the saved order doesn't know yet at its own place.
	const ordered = $derived.by(() => {
		const known = order.map((id) => columns.find((c) => c.id === id)).filter((c): c is Column<T> => !!c);
		const result = [...known];
		columns.forEach((column, index) => {
			if (!known.includes(column)) result.splice(Math.min(index, result.length), 0, column);
		});
		return result;
	});
	const visible = $derived(ordered.filter((c) => visibility[c.id] !== false));
	const toggleable = $derived(ordered.filter((c) => c.label && !c.disableVisibilityToggle));
	const totalWidth = $derived(visible.reduce((sum, c) => sum + widthOf(c), 0));

	const sortedRows = $derived.by(() => {
		const column = sorting && columns.find((c) => c.id === sorting?.id);
		const direction = sorting?.desc ? -1 : 1;
		if (column?.compare) {
			const own = column.compare;
			return [...rows].sort((a, b) => direction * own(a, b));
		}
		if (!column?.value) return rows;
		const value = column.value;
		return [...rows].sort((a, b) => direction * compare(value(a), value(b)));
	});
	const pageCount = $derived(Math.max(1, Math.ceil(rows.length / pageSize)));
	const pageRows = $derived(paged ? sortedRows.slice((pageNumber - 1) * pageSize, pageNumber * pageSize) : sortedRows);

	// When the rows shrink under the current page, jump to the last one there is.
	$effect(() => {
		if (pageNumber > pageCount) pageNumber = pageCount;
	});
	$effect(() => {
		void pageNumber;
		void pageSize;
		if (scroller) scroller.scrollTop = 0;
	});

	function widthOf(column: Column<T>): number {
		return sizing[column.id] ?? column.size ?? 150;
	}

	function colStyle(column: Column<T>): string | undefined {
		return column.stretch ? undefined : `width: ${widthOf(column)}px`;
	}

	function canResize(column: Column<T>) {
		return column.enableResizing !== false && !column.stretch && (column.maxSize === undefined || column.maxSize !== column.minSize);
	}

	function canReorder(column: Column<T>) {
		return !locked && !!column.label && !column.disableReorder;
	}

	function toggleSort(column: Column<T>) {
		// Same cycle as TanStack's toggleSorting(isAsc): off, ascending, descending, ascending.
		sorting = { id: column.id, desc: sorting?.id === column.id ? !sorting.desc : !!column.descFirst };
		persist({ sorting: filterSortingByColumns([sorting], columns) });
	}

	function setVisible(column: Column<T>, value: boolean) {
		visibility = { ...visibility, [column.id]: value };
		persist({ columnVisibility: filterVisibilityByColumns(visibility, columns) });
	}

	function setLocked(value: boolean) {
		locked = value;
		persist({ columnOrderLocked: value });
	}

	function resetAll() {
		sizing = {};
		order = [];
		visibility = defaultVisibility(columns);
		removeTableState(persistKey, ['columnSizing', 'columnOrder', 'columnVisibility']);
	}

	function startResize(event: PointerEvent, column: Column<T>) {
		event.preventDefault();
		event.stopPropagation();
		const startX = event.clientX;
		const startWidth = widthOf(column);
		const move = (e: PointerEvent) => {
			const next = Math.max(column.minSize ?? DEFAULT_MIN_SIZE, Math.min(column.maxSize ?? Infinity, startWidth + e.clientX - startX));
			sizing = { ...sizing, [column.id]: next };
		};
		const up = () => {
			window.removeEventListener('pointermove', move);
			window.removeEventListener('pointerup', up);
			persist({ columnSizing: filterSizingByColumns(sizing, columns) });
		};
		window.addEventListener('pointermove', move);
		window.addEventListener('pointerup', up);
	}

	let dragging = $state<string | undefined>();

	// Spacers and other fixed columns keep their places; only draggable ones trade slots.
	function drop(targetId: string) {
		const source = dragging;
		dragging = undefined;
		if (!source || source === targetId) return;
		const all = ordered.map((c) => c.id);
		const slots = ordered.flatMap((c, index) => (canReorder(c) ? [index] : []));
		const movable = slots.map((index) => all[index]);
		const from = movable.indexOf(source);
		const to = movable.indexOf(targetId);
		if (from < 0 || to < 0) return;
		movable.splice(to, 0, movable.splice(from, 1)[0]);
		slots.forEach((slot, index) => (all[slot] = movable[index]));
		order = all;
		persist({ columnOrder: filterOrderByColumns(order, columns) });
	}

	function setPageSize(value: number) {
		pageSize = value;
		pageNumber = 1;
		if (value > 0) persist({ pageSize: value });
	}

	// A page size no longer offered moves to the closest offered one, as the original's pager does.
	$effect(() => {
		if (!paged) return;
		const next = nearestPageSize(pageSize, tableSettings.pageSizes);
		if (next !== pageSize) untrack(() => setPageSize(next));
	});
</script>

{#snippet headerCells()}
	<Table.Row>
		{#each visible as column (column.id)}
			<Table.Head
				class="group sticky top-0 z-10 border-b border-border bg-background dark:bg-sidebar {column.label ? '' : 'p-0'} {column.class ?? ''} {column.headerClass ?? ''}"
				draggable={canReorder(column)}
				ondragstart={() => (dragging = column.id)}
				ondragover={(event) => {
					if (dragging && canReorder(column)) event.preventDefault();
				}}
				ondrop={() => drop(column.id)}
				ondragend={() => (dragging = undefined)}
			>
				{#if column.header}
					{@render column.header()}
				{:else if column.sortable && column.label}
					<Button variant="ghost" onclick={() => toggleSort(column)}>
						{column.label}
						<ArrowUpDown class="ml-1 size-4" />
					</Button>
				{:else if column.label}
					{column.label}
				{/if}
				{#if canResize(column) && column.label}
					<!-- svelte-ignore a11y_no_static_element_interactions -->
					<div
						class="absolute top-0 right-0 h-full w-2 cursor-col-resize touch-none opacity-0 transition-opacity select-none group-hover:opacity-100"
						draggable="false"
						ondragstart={(event) => event.preventDefault()}
						onpointerdown={(event) => startResize(event, column)}
					>
						<div class="absolute top-0 right-0 h-full w-px bg-border dark:brightness-[2]"></div>
					</div>
				{/if}
			</Table.Head>
		{/each}
	</Table.Row>
{/snippet}

{#snippet rowCells(row: T)}
	{#each visible as column (column.id)}
		<Table.Cell class="{column.label ? '' : 'p-0'} {column.class ?? ''}">
			{#if column.cell}{@render column.cell(row)}{/if}
		</Table.Cell>
	{/each}
{/snippet}

<div class="data-table flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden {className}">
	{#if toolbar}
		<div class="mb-2">{@render toolbar()}</div>
	{/if}

	<div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-md border">
		<div bind:this={scroller} class="relative max-w-full min-h-0 flex-1 overflow-auto">
			<Table.Root class="w-full table-fixed" style={totalWidth > 0 ? `min-width: ${totalWidth}px` : undefined}>
				<colgroup>
					{#each visible as column (column.id)}
						<col style={colStyle(column)} />
					{/each}
				</colgroup>
				<ContextMenu.Root>
					<ContextMenu.Trigger>
						{#snippet child({ props })}
							<Table.Header {...props}>
								{@render headerCells()}
							</Table.Header>
						{/snippet}
					</ContextMenu.Trigger>
					<ContextMenu.Content class="w-48">
						{#each toggleable as column (column.id)}
							<ContextMenu.CheckboxItem closeOnSelect={false} checked={visibility[column.id] !== false} onCheckedChange={(value) => setVisible(column, value)}>
								{column.label}
							</ContextMenu.CheckboxItem>
						{/each}
						<ContextMenu.Separator />
						<ContextMenu.CheckboxItem closeOnSelect={false} checked={locked} onCheckedChange={setLocked}>{m.table_header_menu_lock_column_order()}</ContextMenu.CheckboxItem>
						<ContextMenu.Separator />
						<ContextMenu.Item inset onSelect={resetAll}>{m.table_header_menu_reset_all()}</ContextMenu.Item>
					</ContextMenu.Content>
				</ContextMenu.Root>
				<Table.Body>
					{#each pageRows as row (rowKey(row))}
						{#if rowMenu}
							<ContextMenu.Root>
								<ContextMenu.Trigger>
									{#snippet child({ props })}
										<Table.Row {...props} class="group/row {tableSettings.striped ? 'even:bg-muted/20' : ''} {rowClass?.(row) ?? ''}" onclick={() => onRowClick?.(row)}>
											{@render rowCells(row)}
										</Table.Row>
									{/snippet}
								</ContextMenu.Trigger>
								{@render rowMenu(row)}
							</ContextMenu.Root>
						{:else}
							<Table.Row class="group/row {tableSettings.striped ? 'even:bg-muted/20' : ''} {rowClass?.(row) ?? ''}" onclick={() => onRowClick?.(row)}>
								{@render rowCells(row)}
							</Table.Row>
						{/if}
						{#if expandedRow && isExpanded?.(row)}
							<Table.Row>
								<Table.Cell colspan={visible.length} class="whitespace-normal">{@render expandedRow(row)}</Table.Cell>
							</Table.Row>
						{/if}
					{:else}
						<Table.Row>
							<Table.Cell class="h-24 text-center" colspan={visible.length}>
								{#if empty}
									{@render empty()}
								{:else if !loading}
									<DataTableEmpty type={filtered ? 'nomatch' : 'nodata'} />
								{/if}
							</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
			{#if loading}
				<div class={LOADING_OVERLAY}>
					<Spinner class="size-6" />
				</div>
			{/if}
		</div>
	</div>

	{#if paged}
	<TablePager count={rows.length} {pageSize} pageSizes={tableSettings.pageSizes} onPageSizeChange={(value) => setPageSize(Number(value))} bind:page={pageNumber} />
	{/if}
</div>
