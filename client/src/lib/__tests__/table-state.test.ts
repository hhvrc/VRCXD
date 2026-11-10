import { beforeEach, describe, expect, test, vi } from 'vitest';

vi.mock('$app/env', () => ({ browser: true }));

const store = new Map<string, string>();
vi.stubGlobal('localStorage', {
	getItem: (key: string) => store.get(key) ?? null,
	setItem: (key: string, value: string) => void store.set(key, value),
	removeItem: (key: string) => void store.delete(key)
});

const {
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
	writeTableState
} = await import('../settings/table-state');

const columns = (...ids: string[]) => ids.map((id) => ({ id }));
const saved = (key: string) => JSON.parse(store.get(`vrcx:table:${key}`) ?? 'null');

// The original's useVrcxVueTable persistence, as data-table.svelte, Feed and My Avatars use it.
describe('table state persistence', () => {
	beforeEach(() => store.clear());

	test('a sort change is saved under vrcx:table:<key>', () => {
		writeTableState('test-sort', { sorting: filterSortingByColumns([{ id: 'name', desc: false }], columns('name', 'date')) });
		expect(saved('test-sort').sorting).toEqual([{ id: 'name', desc: false }]);
	});

	test('a saved sort wins over the initial one', () => {
		store.set('vrcx:table:test-restore', JSON.stringify({ sorting: [{ id: 'date', desc: true }] }));
		expect(restoredSorting(readTableState('test-restore'), [{ id: 'name', desc: false }])).toEqual([{ id: 'date', desc: true }]);
	});

	test('a saved sort is loaded as is, stale columns included', () => {
		store.set('vrcx:table:test-stale', JSON.stringify({ sorting: [{ id: 'removed_col', desc: false }, { id: 'name', desc: true }] }));
		const sorting = restoredSorting(readTableState('test-stale'), []);
		expect(sorting).toContainEqual({ id: 'name', desc: true });
		// The data table keeps one sort, the first for a column it still has.
		expect(filterSortingByColumns(sorting, columns('name', 'date'))[0]).toEqual({ id: 'name', desc: true });
	});

	test('a saved empty sort still wins over the initial one', () => {
		expect(restoredSorting({ sorting: [] }, [{ id: 'name', desc: false }])).toEqual([]);
	});

	test('writes merge, so sizes stay when the sort is saved later', () => {
		writeTableState('test-both', { columnSizing: { name: 200 } });
		writeTableState('test-both', { sorting: [{ id: 'date', desc: true }] });
		const stored = saved('test-both');
		expect('columnSizing' in stored).toBe(true);
		expect(stored.sorting).toEqual([{ id: 'date', desc: true }]);
		expect(typeof stored.updatedAt).toBe('number');
	});

	test('the initial sort is used when nothing is saved', () => {
		expect(restoredSorting(readTableState('test-initial'), [{ id: 'date', desc: true }])).toEqual([{ id: 'date', desc: true }]);
	});

	test('a column order change is saved', () => {
		writeTableState('test-col-order', { columnOrder: filterOrderByColumns(['date', 'name', 'status'], columns('name', 'date', 'status')) });
		expect(saved('test-col-order').columnOrder).toEqual(['date', 'name', 'status']);
	});

	test('a saved column order is read back', () => {
		store.set('vrcx:table:test-restore-order', JSON.stringify({ columnOrder: ['status', 'name', 'date'] }));
		expect(readTableState('test-restore-order').columnOrder).toEqual(['status', 'name', 'date']);
	});

	test('a saved column order drops columns the table no longer has', () => {
		writeTableState('test-stale-order', { columnOrder: filterOrderByColumns(['removed_col', 'date', 'name'], columns('name', 'date')) });
		expect(saved('test-stale-order').columnOrder).toEqual(['date', 'name']);
	});

	test('a column visibility change is saved', () => {
		writeTableState('test-col-vis', { columnVisibility: filterVisibilityByColumns({ name: false, status: true }, columns('name', 'date', 'status')) });
		expect(saved('test-col-vis').columnVisibility).toEqual({ name: false, status: true });
	});

	test('a saved column visibility is read back', () => {
		store.set('vrcx:table:test-restore-vis', JSON.stringify({ columnVisibility: { date: false } }));
		expect(readTableState('test-restore-vis').columnVisibility).toEqual({ date: false });
	});

	test('a saved column visibility drops columns the table no longer has', () => {
		writeTableState('test-stale-vis', { columnVisibility: filterVisibilityByColumns({ removed_col: false, name: false }, columns('name', 'date')) });
		expect(saved('test-stale-vis').columnVisibility).toEqual({ name: false });
	});

	test('a page size change is saved per table', () => {
		writeTableState('test-page-size', { pageSize: 25 });
		writeTableState('other-table', { pageSize: 50 });
		expect(saved('test-page-size').pageSize).toBe(25);
		expect(saved('other-table').pageSize).toBe(50);
	});

	test('a saved page size wins over the initial one', () => {
		store.set('vrcx:table:test-restore-ps', JSON.stringify({ pageSize: 50 }));
		expect(restoredPageSize(readTableState('test-restore-ps'), 10)).toBe(50);
	});

	test('the initial page size is used when none is saved', () => {
		expect(restoredPageSize(readTableState('test-initial-ps'), 25)).toBe(25);
		expect(restoredPageSize({ pageSize: 0 }, 25)).toBe(25);
		expect(restoredPageSize({ pageSize: '50' as unknown as number }, 25)).toBe(25);
	});

	test('Reset clears sizes, order and visibility and keeps the rest', () => {
		writeTableState('test-reset-all', { columnSizing: { name: 200 }, columnOrder: ['date', 'name'], columnVisibility: { name: false }, pageSize: 50 });
		removeTableState('test-reset-all', ['columnSizing', 'columnOrder', 'columnVisibility']);
		const stored = saved('test-reset-all');
		expect(stored.columnSizing).toBeUndefined();
		expect(stored.columnOrder).toBeUndefined();
		expect(stored.columnVisibility).toBeUndefined();
		expect(stored.pageSize).toBe(50);
	});

	test('Reset leaves the default-hidden columns hidden', () => {
		expect(defaultVisibility([{ id: 'name' }, { id: 'date', defaultHidden: true }])).toEqual({ date: false });
		expect(defaultVisibility(columns('name', 'date'))).toEqual({});
	});

	test('the column order lock is saved', () => {
		writeTableState('test-lock-order', { columnOrderLocked: true });
		expect(saved('test-lock-order').columnOrderLocked).toBe(true);
	});

	test('the column order lock is read back', () => {
		store.set('vrcx:table:test-restore-lock', JSON.stringify({ columnOrderLocked: true }));
		expect(readTableState('test-restore-lock').columnOrderLocked).toBe(true);
		expect(readTableState('missing').columnOrderLocked).toBeUndefined();
	});

	test('a saved value that is not an object reads as nothing saved', () => {
		store.set('vrcx:table:odd', '42');
		store.set('vrcx:table:list', '[1]');
		store.set('vrcx:table:broken', '{oops');
		expect(readTableState('odd')).toEqual({});
		expect(readTableState('list')).toEqual({});
		expect(readTableState('broken')).toEqual({});
	});
});

describe('dropping stale saved entries', () => {
	test('sizing keeps only the table columns', () => {
		expect(filterSizingByColumns({ name: 200, removed: 100, date: 150 }, columns('name', 'date'))).toEqual({ name: 200, date: 150 });
	});

	test('sizing that is null gives nothing', () => {
		expect(filterSizingByColumns(null, columns('name'))).toEqual({});
	});

	test('sizing that is not an object gives nothing', () => {
		expect(filterSizingByColumns('bad', columns('name'))).toEqual({});
	});

	test('sizing with no columns gives nothing', () => {
		expect(filterSizingByColumns({ name: 200 }, null)).toEqual({});
	});

	test('sorting keeps only the table columns', () => {
		expect(
			filterSortingByColumns(
				[
					{ id: 'name', desc: false },
					{ id: 'removed', desc: true }
				],
				columns('name', 'date')
			)
		).toEqual([{ id: 'name', desc: false }]);
	});

	test('sorting that is not a list gives nothing', () => {
		expect(filterSortingByColumns('bad', columns('name'))).toEqual([]);
	});

	test('sorting with no columns gives nothing', () => {
		expect(filterSortingByColumns([{ id: 'name', desc: false }], null)).toEqual([]);
	});

	test('order keeps only the table columns', () => {
		expect(filterOrderByColumns(['date', 'removed', 'name'], columns('name', 'date'))).toEqual(['date', 'name']);
	});

	test('order that is not a list gives nothing', () => {
		expect(filterOrderByColumns('bad', columns('name'))).toEqual([]);
	});

	test('order with no columns gives nothing', () => {
		expect(filterOrderByColumns(['name'], null)).toEqual([]);
	});

	test('visibility keeps only the table columns', () => {
		expect(filterVisibilityByColumns({ name: false, removed: true }, columns('name', 'date'))).toEqual({ name: false });
	});

	test('visibility that is null gives nothing', () => {
		expect(filterVisibilityByColumns(null, columns('name'))).toEqual({});
	});

	test('visibility that is not an object gives nothing', () => {
		expect(filterVisibilityByColumns(42, columns('name'))).toEqual({});
	});

	test('visibility with no columns gives nothing', () => {
		expect(filterVisibilityByColumns({ name: false }, null)).toEqual({});
	});
});

// The original's DataTableLayout moves a page size that is no longer offered onto the closest one.
describe('page size the pager offers', () => {
	test('an offered size stays', () => {
		expect(nearestPageSize(20, [10, 20, 50])).toBe(20);
	});

	test('a size no longer offered moves to the closest offered one', () => {
		expect(nearestPageSize(30, [10, 25, 100])).toBe(25);
		expect(nearestPageSize(80, [10, 25, 100])).toBe(100);
		expect(nearestPageSize(15, [10, 20])).toBe(10);
	});

	test('nothing offered keeps the size', () => {
		expect(nearestPageSize(30, [])).toBe(30);
	});
});
