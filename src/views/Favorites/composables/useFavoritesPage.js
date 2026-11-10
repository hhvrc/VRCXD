import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useAppearanceSettingsStore, useModalStore } from '../../../stores';
import { favoriteRequest } from '../../../api';

import * as workerTimers from 'worker-timers';

/**
 * Groups remote favorites by their group key, skipping ungrouped entries.
 *
 * @param {{ groupKey?: string }[]} favorites
 * @returns {Record<string, Array>}
 */
export function groupFavoritesByGroupKey(favorites) {
    const grouped = {};
    favorites.forEach((favorite) => {
        if (!favorite.groupKey) {
            return;
        }
        if (!grouped[favorite.groupKey]) {
            grouped[favorite.groupKey] = [];
        }
        grouped[favorite.groupKey].push(favorite);
    });
    return grouped;
}

/**
 * State and handlers shared by the world, avatar and friend favorites pages:
 * search/edit/toolbar state, remote selection, remote group rename, visibility
 * and clear, and the import/export toolbar actions.
 *
 * @param {object} options
 * @param {import('vue').Ref<string[]>} options.selected Selected remote favorite ids
 * @param {import('vue').Ref<object | null>} options.activeRemoteGroup
 * @param {import('vue').Ref<{ id: string }[]>} options.currentRemoteFavorites
 * @param {import('vue').Ref<string>} options.importInput Import dialog input
 * @param {() => void} options.showImportDialog
 * @param {(json: object) => void} options.handleFavoriteGroup
 * @param {() => void} options.refreshFavorites
 * @param {(key: string, visible: boolean) => void} options.handleGroupMenuVisible
 * @param {(key: string) => string} options.remoteGroupMenuKey
 * @param {(group: object) => string} options.remoteGroupType Favorite type sent when saving a remote group
 * @param {() => string} [options.visibilityUpdatedMessage]
 * @param {boolean} [options.destructiveClear] Use the destructive confirm button when clearing a remote group
 * @returns {object}
 */
export function useFavoritesPage(options) {
    const {
        selected,
        activeRemoteGroup,
        currentRemoteFavorites,
        importInput,
        showImportDialog,
        handleFavoriteGroup,
        refreshFavorites,
        handleGroupMenuVisible,
        remoteGroupMenuKey,
        remoteGroupType
    } = options;

    const { t } = useI18n();
    const modalStore = useModalStore();
    const { sortFavorites } = storeToRefs(useAppearanceSettingsStore());
    const { setSortFavorites } = useAppearanceSettingsStore();

    const exportDialogVisible = ref(false);
    const search = ref('');
    const searchResults = ref([]);
    const editMode = ref(false);
    const toolbarMenuOpen = ref(false);

    const hasSelection = computed(() => selected.value.length > 0);
    const hasSearchInput = computed(() => search.value.trim().length > 0);
    const isSearchActive = computed(() => search.value.trim().length >= 3);

    const isAllSelected = computed(() => {
        if (!activeRemoteGroup.value || !currentRemoteFavorites.value.length) {
            return false;
        }
        return currentRemoteFavorites.value.map((fav) => fav.id).every((id) => selected.value.includes(id));
    });

    watch(isSearchActive, (active) => {
        if (active && editMode.value) {
            editMode.value = false;
        }
    });

    watch(
        () => editMode.value,
        (value) => {
            if (!value) {
                clearSelection();
            }
        }
    );

    function closeToolbarMenu() {
        toolbarMenuOpen.value = false;
    }

    function showExportDialog() {
        exportDialogVisible.value = true;
    }

    function handleImportClick() {
        closeToolbarMenu();
        showImportDialog();
    }

    function handleExportClick() {
        closeToolbarMenu();
        showExportDialog();
    }

    /**
     * @param {string} value
     */
    function handleSortFavoritesChange(value) {
        const next = value === 'date';
        if (next !== sortFavorites.value) {
            setSortFavorites();
        }
    }

    /**
     * @param {string} id
     * @param {boolean} value
     */
    function toggleSelection(id, value) {
        if (value) {
            if (!selected.value.includes(id)) {
                selected.value.push(id);
            }
        } else {
            selected.value = selected.value.filter((selectedId) => selectedId !== id);
        }
    }

    function clearSelection() {
        selected.value = [];
    }

    function toggleSelectAll() {
        if (!activeRemoteGroup.value) {
            return;
        }
        if (isAllSelected.value) {
            selected.value = [];
        } else {
            selected.value = currentRemoteFavorites.value.map((fav) => fav.id);
        }
    }

    function copySelected() {
        if (!selected.value.length) {
            return;
        }
        const idList = selected.value.map((id) => `${id}\n`).join('');
        importInput.value = idList;
        showImportDialog();
    }

    /**
     * @param {string[]} ids
     */
    function bulkUnfavoriteRemote(ids) {
        ids.forEach((id) => {
            favoriteRequest.deleteFavorite({
                objectId: id
            });
        });
        selected.value = [];
        editMode.value = false;
    }

    /**
     * @param {object} args
     */
    function applySavedFavoriteGroup(args) {
        handleFavoriteGroup({
            json: args.json,
            params: {
                favoriteGroupId: args.json.id
            }
        });
    }

    /**
     * @param {object} group
     */
    function changeFavoriteGroupName(group) {
        const currentName = group.displayName || group.name;
        modalStore
            .prompt({
                title: t('prompt.change_favorite_group_name.header'),
                description: t('prompt.change_favorite_group_name.description'),
                confirmText: t('prompt.change_favorite_group_name.change'),
                cancelText: t('prompt.change_favorite_group_name.cancel'),
                pattern: /\S+/,
                inputValue: currentName,
                errorMessage: t('prompt.change_favorite_group_name.input_error')
            })
            .then(({ ok, value }) => {
                if (!ok) return;
                const newName = value.trim();
                if (!newName || newName === currentName) {
                    return;
                }
                favoriteRequest
                    .saveFavoriteGroup({
                        type: remoteGroupType(group),
                        group: group.name,
                        displayName: newName
                    })
                    .then((args) => {
                        applySavedFavoriteGroup(args);
                        toast.success(t('prompt.change_favorite_group_name.message.success'));
                        refreshFavorites();
                    });
            })
            .catch(() => {});
    }

    /**
     * @param {object} group
     * @param {string} visibility
     * @param {string | null} menuKey
     */
    function changeGroupVisibility(group, visibility, menuKey = null) {
        const params = {
            type: remoteGroupType(group),
            group: group.name,
            visibility
        };
        favoriteRequest.saveFavoriteGroup(params).then((args) => {
            applySavedFavoriteGroup(args);
            toast.success(
                options.visibilityUpdatedMessage
                    ? options.visibilityUpdatedMessage()
                    : t('message.group.visibility_updated')
            );
            if (menuKey) {
                handleGroupMenuVisible(menuKey, false);
            }
            refreshFavorites();
            return args;
        });
    }

    /**
     * @param {object} ctx
     */
    function clearFavoriteGroup(ctx) {
        modalStore
            .confirm({
                description: t('confirm.clear_group'),
                title: t('confirm.title'),
                ...(options.destructiveClear && { destructive: true })
            })
            .then(({ ok }) => {
                if (ok) {
                    favoriteRequest.clearFavoriteGroup({
                        type: ctx.type,
                        group: ctx.name
                    });
                }
            })
            .catch(() => {});
    }

    /**
     * @param {object} group
     * @param {string} visibility
     */
    function handleVisibilitySelection(group, visibility) {
        const menuKey = remoteGroupMenuKey(group.key);
        changeGroupVisibility(group, visibility, menuKey);
    }

    /**
     * @param {object} group
     */
    function handleRemoteRename(group) {
        handleGroupMenuVisible(remoteGroupMenuKey(group.key), false);
        changeFavoriteGroupName(group);
    }

    /**
     * @param {object} group
     */
    function handleRemoteClear(group) {
        handleGroupMenuVisible(remoteGroupMenuKey(group.key), false);
        clearFavoriteGroup(group);
    }

    /**
     * @param {string} value
     * @returns {string}
     */
    function formatVisibility(value) {
        if (!value) {
            return '';
        }
        return value.charAt(0).toUpperCase() + value.slice(1);
    }

    return {
        exportDialogVisible,
        search,
        searchResults,
        editMode,
        toolbarMenuOpen,
        hasSelection,
        hasSearchInput,
        isSearchActive,
        isAllSelected,
        closeToolbarMenu,
        showExportDialog,
        handleImportClick,
        handleExportClick,
        handleSortFavoritesChange,
        toggleSelection,
        clearSelection,
        toggleSelectAll,
        copySelected,
        bulkUnfavoriteRemote,
        handleVisibilitySelection,
        handleRemoteRename,
        handleRemoteClear,
        formatVisibility
    };
}

/**
 * Re-fetches every locally favorited object one per second so the cached
 * copies stay current. Cancelled on unmount.
 *
 * @param {() => string[]} getIds
 * @param {(id: string) => Promise<void>} refreshOne
 * @returns {object}
 */
export function useLocalFavoritesRefresh(getIds, refreshOne) {
    const refreshingLocalFavorites = ref(false);
    const worker = ref(null);
    const refreshCancelToken = ref(null);

    async function refreshLocalFavorites() {
        if (refreshingLocalFavorites.value) {
            return;
        }
        refreshingLocalFavorites.value = true;
        const token = {
            cancelled: false,
            resolve: null
        };
        refreshCancelToken.value = token;
        try {
            for (const id of getIds()) {
                if (token.cancelled) {
                    break;
                }
                try {
                    await refreshOne(id);
                } catch (err) {
                    console.error(err);
                }
                if (token.cancelled) {
                    break;
                }
                await new Promise((resolve) => {
                    token.resolve = resolve;
                    worker.value = workerTimers.setTimeout(() => {
                        worker.value = null;
                        resolve();
                    }, 1000);
                });
            }
        } finally {
            if (worker.value) {
                workerTimers.clearTimeout(worker.value);
                worker.value = null;
            }
            if (refreshCancelToken.value === token) {
                refreshCancelToken.value = null;
            }
            refreshingLocalFavorites.value = false;
        }
    }

    function cancelLocalRefresh() {
        if (!refreshingLocalFavorites.value) {
            return;
        }
        if (refreshCancelToken.value) {
            refreshCancelToken.value.cancelled = true;
            if (typeof refreshCancelToken.value.resolve === 'function') {
                refreshCancelToken.value.resolve();
            }
        }
        if (worker.value) {
            workerTimers.clearTimeout(worker.value);
            worker.value = null;
        }
        refreshingLocalFavorites.value = false;
    }

    onBeforeUnmount(() => {
        cancelLocalRefresh();
    });

    return {
        refreshingLocalFavorites,
        refreshLocalFavorites,
        cancelLocalRefresh
    };
}
