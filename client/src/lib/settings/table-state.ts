import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

// The original's useVrcxVueTable persistence: each table keeps its sorting, column sizes, order,
// visibility, order lock and page size under vrcx:table:<persistKey>. Saved entries for columns
// the table no longer has are dropped when written.

export type TableSort = { id: string; desc: boolean };

export type PersistedTable = {
	sorting?: TableSort[];
	columnSizing?: Record<string, number>;
	columnOrder?: string[];
	columnVisibility?: Record<string, boolean>;
	columnOrderLocked?: boolean;
	pageSize?: number;
	updatedAt?: number;
};

type ColumnLike = { id?: string | null } | null | undefined;

export function tableStorageKey(persistKey: string) {
	return `vrcx:table:${persistKey}`;
}

/** The saved state, or an empty one when nothing (or something that is not an object) is saved. */
export function readTableState(persistKey: string): PersistedTable {
	const value = readSetting<unknown>(tableStorageKey(persistKey), null);
	return value && typeof value === 'object' && !Array.isArray(value) ? (value as PersistedTable) : {};
}

/** Merges the patch into what is saved and stamps the write time. */
export function writeTableState(persistKey: string, patch: PersistedTable) {
	storeSetting(tableStorageKey(persistKey), { ...readTableState(persistKey), ...patch, updatedAt: Date.now() });
}

export function removeTableState(persistKey: string, keys: (keyof PersistedTable)[]) {
	const current: PersistedTable = { ...readTableState(persistKey) };
	for (const key of keys) delete current[key];
	current.updatedAt = Date.now();
	storeSetting(tableStorageKey(persistKey), current);
}

function columnIds(columns: readonly ColumnLike[] | null | undefined) {
	return new Set((columns ?? []).map((c) => c?.id).filter((id): id is string => !!id));
}

function filterRecord<V>(record: unknown, columns: readonly ColumnLike[] | null | undefined): Record<string, V> {
	if (!record || typeof record !== 'object') return {};
	const ids = columnIds(columns);
	return Object.fromEntries(Object.entries(record as Record<string, V>).filter(([key]) => ids.has(key)));
}

export function filterSizingByColumns(sizing: unknown, columns: readonly ColumnLike[] | null | undefined): Record<string, number> {
	return filterRecord<number>(sizing, columns);
}

export function filterVisibilityByColumns(visibility: unknown, columns: readonly ColumnLike[] | null | undefined): Record<string, boolean> {
	return filterRecord<boolean>(visibility, columns);
}

export function filterSortingByColumns(sorting: unknown, columns: readonly ColumnLike[] | null | undefined): TableSort[] {
	if (!Array.isArray(sorting)) return [];
	const ids = columnIds(columns);
	return sorting.filter((s): s is TableSort => !!s && ids.has(s.id));
}

export function filterOrderByColumns(order: unknown, columns: readonly ColumnLike[] | null | undefined): string[] {
	if (!Array.isArray(order)) return [];
	const ids = columnIds(columns);
	return order.filter((id): id is string => ids.has(id));
}

/** A saved sorting wins over the initial one, even when it is empty. */
export function restoredSorting(saved: PersistedTable, initial: TableSort[]): TableSort[] {
	return Array.isArray(saved.sorting) ? saved.sorting : initial;
}

/** A saved page size above zero wins over the table's initial one. */
export function restoredPageSize(saved: PersistedTable, initial: number): number {
	return typeof saved.pageSize === 'number' && saved.pageSize > 0 ? saved.pageSize : initial;
}

/** The offered page size closest to the current one (the earlier one on a tie), or the current one when offered or nothing is. */
export function nearestPageSize(current: number, sizes: readonly number[]): number {
	if (!sizes.length || sizes.includes(current)) return current;
	return sizes.reduce((prev, s) => (Math.abs(s - current) < Math.abs(prev - current) ? s : prev));
}

/** Column visibility with the default-hidden columns off, as Reset leaves it. */
export function defaultVisibility(columns: readonly { id: string; defaultHidden?: boolean }[]): Record<string, boolean> {
	return Object.fromEntries(columns.filter((c) => c.defaultHidden).map((c) => [c.id, false]));
}
