import { describe, expect, test, vi } from 'vitest';

vi.mock('@/plugins', () => ({ i18n: { global: { t: (key) => key } } }));
vi.mock('@/shared/utils', () => ({ formatDateFilter: (value) => String(value) }));

import { createBansColumns, createInvitesColumns, createMembersColumns } from '../groupMemberModerationColumns.jsx';

const columnKeys = (columns) => columns.map((column) => column.id ?? column.accessorKey);
const displayNameColumn = (columns) => columns.find((column) => column.id === 'displayName');

describe('groupMemberModerationColumns', () => {
    test('members columns', () => {
        expect(columnKeys(createMembersColumns({}))).toEqual([
            'selected',
            'avatar',
            'displayName',
            'roles',
            'managerNotes',
            'joinedAt',
            'visibility'
        ]);
    });

    test('bans columns', () => {
        expect(columnKeys(createBansColumns({}))).toEqual([
            'selected',
            'avatar',
            'displayName',
            'roles',
            'managerNotes',
            'joinedAt',
            'bannedAt'
        ]);
    });

    test('invites columns', () => {
        expect(columnKeys(createInvitesColumns({}))).toEqual(['selected', 'avatar', 'displayName', 'managerNotes']);
    });

    test('display name sorting falls back to $displayName except for members', () => {
        const row = { $displayName: 'Fallback' };
        expect(displayNameColumn(createMembersColumns({})).accessorFn(row)).toBe('');
        expect(displayNameColumn(createBansColumns({})).accessorFn(row)).toBe('Fallback');
        expect(displayNameColumn(createInvitesColumns({})).accessorFn(row)).toBe('Fallback');
        expect(displayNameColumn(createBansColumns({})).accessorFn({ user: { displayName: 'User' }, ...row })).toBe(
            'User'
        );
    });

    test('roles column uses rolesText from context', () => {
        const rolesText = vi.fn(() => 'Admin');
        const roles = createMembersColumns({ rolesText }).find((column) => column.id === 'roles');
        expect(roles.accessorFn({ roleIds: ['grol_1'] })).toBe('Admin');
        expect(rolesText).toHaveBeenCalledWith(['grol_1']);
    });

    test('selection checkbox updates the row and notifies', () => {
        const onSelectionChange = vi.fn();
        const selected = createInvitesColumns({ onSelectionChange })[0];
        const original = { userId: 'usr_1', $selected: false };
        const wrapper = selected.cell({ row: { original } });
        const checkbox = wrapper.children[0];
        checkbox.props['onUpdate:modelValue'](true);
        expect(original.$selected).toBe(true);
        expect(onSelectionChange).toHaveBeenCalledWith(original);
    });
});
