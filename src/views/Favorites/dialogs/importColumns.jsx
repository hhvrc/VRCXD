import { Trash2 } from 'lucide-vue-next';

import { Button } from '../../../components/ui/button';
import { i18n } from '../../../plugins';

const { t } = i18n.global;

/**
 * Columns for the favorites import table. Avatars and worlds show author and
 * release status; friends only show image and name.
 *
 * @param {object} options
 * @param {string} options.nameKey - Row field holding the display name
 * @param {(row: any) => string} options.getImage - Thumbnail url for a row
 * @param {(row: any) => string} options.getFullImage - Full size image url for a row
 * @param {boolean} options.showAuthorAndStatus - Include author and release status columns
 * @param {string} [options.deleteButtonClass] - Extra class for the delete button
 * @param {(id: string) => void} options.onShowName - Opens the dialog for the row itself
 * @param {(id: string) => void} [options.onShowUser] - Opens the author's user dialog
 * @param {(url: string) => void} options.onShowFullscreenImage
 * @param {(row: any) => void} options.onDelete
 * @returns {object[]}
 */
export const createImportColumns = ({
    nameKey,
    getImage,
    getFullImage,
    showAuthorAndStatus,
    deleteButtonClass,
    onShowName,
    onShowUser,
    onShowFullscreenImage,
    onDelete
}) => [
    {
        id: 'image',
        header: () => t('table.import.image'),
        enableSorting: false,
        size: 70,
        cell: ({ row }) => {
            const original = row.original;
            const thumb = getImage?.(original);
            const full = getFullImage?.(original);

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
    },
    {
        id: 'name',
        accessorKey: nameKey,
        header: () => t('table.import.name'),
        meta: {
            stretch: true
        },
        cell: ({ row }) => {
            const original = row.original;
            return (
                <span
                    class="cursor-pointer"
                    title={original?.[nameKey]}
                    onClick={(e) => {
                        e.stopPropagation();
                        onShowName?.(original?.id);
                    }}
                >
                    {original?.[nameKey]}
                </span>
            );
        }
    },
    ...(showAuthorAndStatus
        ? [
              {
                  id: 'author',
                  header: () => t('table.import.author'),
                  size: 120,
                  enableSorting: false,
                  cell: ({ row }) => {
                      const original = row.original;
                      return (
                          <span
                              class="cursor-pointer"
                              title={original?.authorName}
                              onClick={(e) => {
                                  e.stopPropagation();
                                  onShowUser?.(original?.authorId);
                              }}
                          >
                              {original?.authorName}
                          </span>
                      );
                  }
              },
              {
                  id: 'status',
                  header: () => t('table.import.status'),
                  size: 70,
                  enableSorting: false,
                  cell: ({ row }) => {
                      const status = String(row.original?.releaseStatus ?? '');
                      const label = status ? status.charAt(0).toUpperCase() + status.slice(1) : '';
                      const colorClass =
                          status === 'public' ? 'text-status-online' : status === 'private' ? 'text-destructive' : '';
                      return <span class={colorClass}>{label}</span>;
                  }
              }
          ]
        : []),
    {
        id: 'action',
        header: () => t('table.import.action'),
        size: 90,
        enableSorting: false,
        meta: {
            tdClass: 'text-right'
        },
        cell: ({ row }) => {
            const original = row.original;
            return (
                <Button
                    size="icon-sm"
                    variant="ghost"
                    class={deleteButtonClass}
                    onClick={(e) => {
                        e.stopPropagation();
                        onDelete?.(original);
                    }}
                >
                    <Trash2 />
                </Button>
            );
        }
    }
];
