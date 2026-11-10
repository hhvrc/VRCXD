import { Checkbox } from '@/components/ui/checkbox';
import { i18n } from '@/plugins';
import { formatDateFilter } from '@/shared/utils';
import { Button } from '@/components/ui/button';
import { ArrowUpDown } from 'lucide-vue-next';
import { TooltipWrapper } from '@/components/ui/tooltip';

const { t } = i18n.global;

const sortButton = ({ column, label, descFirst = false }) => (
    <Button
        variant="ghost"
        size="sm"
        class="-ml-2 h-8 px-2"
        onClick={() => {
            const sorted = column.getIsSorted();
            if (!sorted && descFirst) {
                column.toggleSorting(true);
                return;
            }
            column.toggleSorting(sorted === 'asc');
        }}
    >
        {label}
        <ArrowUpDown class="ml-1 h-4 w-4" />
    </Button>
);

const selectedColumn = ({ onSelectionChange }) => ({
    id: 'selected',
    header: () => null,
    size: 55,
    enableSorting: false,
    cell: ({ row }) => {
        const original = row.original;
        return (
            <div class="flex items-center justify-center" onClick={(e) => e.stopPropagation()}>
                <Checkbox
                    modelValue={!!original?.$selected}
                    onUpdate:modelValue={(value) => {
                        original.$selected = value;
                        onSelectionChange?.(original);
                    }}
                />
            </div>
        );
    }
});

const avatarColumn = ({ userImage, userImageFull, onShowFullscreenImage }) => ({
    id: 'avatar',
    header: () => t('dialog.group_member_moderation.avatar'),
    size: 70,
    enableSorting: false,
    cell: ({ row }) => {
        const original = row.original;
        const thumb = userImage?.(original?.user);
        const full = userImageFull?.(original?.user);

        return (
            <img
                src={thumb}
                class="friends-list-avatar"
                loading="lazy"
                onClick={(e) => {
                    e.stopPropagation();
                    if (full) {
                        onShowFullscreenImage?.(full);
                    }
                }}
            />
        );
    }
});

/**
 * @param {object} context
 * @param {boolean} sortByFallbackName - Sort rows without a user object by row.$displayName
 *   (used by every table except members, matching the original per-table column files)
 */
const displayNameColumn = ({ randomUserColours, onShowUser }, sortByFallbackName) => ({
    id: 'displayName',
    accessorFn: sortByFallbackName
        ? (row) => row?.user?.displayName ?? row?.$displayName ?? ''
        : (row) => row?.user?.displayName ?? '',
    header: ({ column }) =>
        sortButton({
            column,
            label: t('dialog.group_member_moderation.display_name')
        }),
    size: 160,
    cell: ({ row }) => {
        const original = row.original;
        const useColors = !!(randomUserColours?.value ?? randomUserColours);
        const colorStyle = useColors ? { color: original?.user?.$userColour } : null;

        return (
            <span
                style="cursor: pointer"
                onClick={(e) => {
                    e.stopPropagation();
                    onShowUser?.(original?.userId);
                }}
            >
                <span style={colorStyle}>{original?.user?.displayName}</span>
            </span>
        );
    }
});

const rolesColumn = ({ rolesText }) => ({
    id: 'roles',
    accessorFn: (row) => rolesText?.(row?.roleIds) ?? '',
    header: ({ column }) =>
        sortButton({
            column,
            label: t('dialog.group_member_moderation.roles')
        }),
    cell: ({ row }) => {
        const original = row.original;
        return <span>{rolesText?.(original?.roleIds) ?? ''}</span>;
    }
});

const managerNotesColumn = () => ({
    accessorKey: 'managerNotes',
    header: ({ column }) =>
        sortButton({
            column,
            label: t('dialog.group_member_moderation.notes')
        }),
    cell: ({ row }) => {
        const managerNotes = row.original?.managerNotes ?? '';
        return (
            <TooltipWrapper content={managerNotes} disabled={!managerNotes}>
                <span class="inline-block max-w-full truncate align-middle" onClick={(e) => e.stopPropagation()}>
                    {managerNotes}
                </span>
            </TooltipWrapper>
        );
    }
});

const dateColumn = (accessorKey, labelKey) => ({
    accessorKey,
    header: ({ column }) =>
        sortButton({
            column,
            label: t(labelKey)
        }),
    size: 170,
    cell: ({ row }) => <span>{formatDateFilter(row.original?.[accessorKey], 'long')}</span>
});

const visibilityColumn = () => ({
    accessorKey: 'visibility',
    header: ({ column }) =>
        sortButton({
            column,
            label: t('dialog.group_member_moderation.visibility')
        }),
    size: 120,
    cell: ({ row }) => <span>{row.original?.visibility}</span>
});

/**
 * Columns for the members tab.
 *
 * @param {{
 *     randomUserColours?: any;
 *     rolesText?: Function;
 *     userImage?: Function;
 *     userImageFull?: Function;
 *     onShowFullscreenImage?: Function;
 *     onShowUser?: Function;
 *     onSelectionChange?: Function;
 * }} context
 */
export const createMembersColumns = (context) => [
    selectedColumn(context),
    avatarColumn(context),
    displayNameColumn(context, false),
    rolesColumn(context),
    managerNotesColumn(),
    dateColumn('joinedAt', 'dialog.group_member_moderation.joined_at'),
    visibilityColumn()
];

/**
 * Columns for the bans tab.
 *
 * @param {{
 *     randomUserColours?: any;
 *     rolesText?: Function;
 *     userImage?: Function;
 *     userImageFull?: Function;
 *     onShowFullscreenImage?: Function;
 *     onShowUser?: Function;
 *     onSelectionChange?: Function;
 * }} context
 */
export const createBansColumns = (context) => [
    selectedColumn(context),
    avatarColumn(context),
    displayNameColumn(context, true),
    rolesColumn(context),
    managerNotesColumn(),
    dateColumn('joinedAt', 'dialog.group_member_moderation.joined_at'),
    dateColumn('bannedAt', 'dialog.group_member_moderation.banned_at')
];

/**
 * Columns for the invites, join requests and blocked requests tables.
 *
 * @param {{
 *     randomUserColours?: any;
 *     userImage?: Function;
 *     userImageFull?: Function;
 *     onShowFullscreenImage?: Function;
 *     onShowUser?: Function;
 *     onSelectionChange?: Function;
 * }} context
 */
export const createInvitesColumns = (context) => [
    selectedColumn(context),
    avatarColumn(context),
    displayNameColumn(context, true),
    managerNotesColumn()
];
