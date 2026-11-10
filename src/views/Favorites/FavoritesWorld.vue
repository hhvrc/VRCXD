<template>
    <div class="x-container">
        <div class="flex flex-col h-full min-h-0 pb-0">
            <FavoritesToolbar
                :sort-value="worldSortValue"
                :extra-sort-options="worldExtraSortOptions"
                v-model:search-query="worldFavoriteSearch"
                :search-placeholder="worldSearchPlaceholder"
                v-model:search-mode="worldSearchMode"
                :search-mode-visible="true"
                v-model:toolbar-menu-open="worldToolbarMenuOpen"
                v-model:card-scale-value="worldCardScaleValue"
                :card-scale-percent="worldCardScalePercent"
                :card-scale-slider="worldCardScaleSlider"
                v-model:card-spacing-value="worldCardSpacingValue"
                :card-spacing-percent="worldCardSpacingPercent"
                :card-spacing-slider="worldCardSpacingSlider"
                @update:sort-value="handleSortValueChange"
                @search="searchWorldFavorites"
                @import="handleWorldImportClick"
                @export="handleWorldExportClick" />
            <ResizablePanelGroup
                ref="splitterGroupRef"
                direction="horizontal"
                class="flex-1 min-h-0 favorites-splitter"
                @layout="handleLayout">
                <ResizablePanel
                    ref="splitterPanelRef"
                    :default-size="splitterDefaultSize"
                    :min-size="splitterMinSize"
                    :max-size="splitterMaxSize"
                    :collapsed-size="0"
                    collapsible
                    :order="1">
                    <div class="h-full pr-2 overflow-auto flex flex-col gap-3">
                        <div class="flex flex-col gap-2">
                            <div class="flex items-center justify-between font-semibold text-sm mb-[9px]">
                                <span>{{ t('view.favorite.worlds.vrchat_favorites') }}</span>
                                <TooltipWrapper side="bottom" :content="t('view.favorite.refresh_favorites_tooltip')">
                                    <Button
                                        class="rounded-full"
                                        variant="ghost"
                                        size="icon-sm"
                                        :disabled="isFavoriteLoading"
                                        :ariaLabel="t('view.favorite.refresh_favorites_tooltip')"
                                        @click.stop="handleRefreshFavorites">
                                        <Spinner v-if="isFavoriteLoading" />
                                        <RefreshCw v-else />
                                    </Button>
                                </TooltipWrapper>
                            </div>
                            <div class="flex flex-col gap-2">
                                <template v-if="favoriteWorldGroups.length">
                                    <div
                                        v-for="group in favoriteWorldGroups"
                                        :key="group.key"
                                        :class="[
                                            'group-item x-hover-card hover:shadow-sm',
                                            `group-item--${group.visibility}`,
                                            { 'is-active': !hasSearchInput && isGroupActive('remote', group.key) }
                                        ]"
                                        @click="handleGroupClick('remote', group.key)">
                                        <div class="flex items-start justify-between mb-1 text-[13px]">
                                            <span class="font-semibold">{{ group.displayName }}</span>
                                            <span class="text-xs">{{ group.count }}/{{ group.capacity }}</span>
                                        </div>
                                        <div class="flex items-center justify-between gap-2">
                                            <span class="flex items-center gap-1.5">
                                                <span class="text-[11px] text-muted-foreground">{{
                                                    t(`view.favorite.visibility.${group.visibility}`)
                                                }}</span>
                                            </span>
                                            <DropdownMenu
                                                :open="activeGroupMenu === remoteGroupMenuKey(group.key)"
                                                @update:open="
                                                    handleGroupMenuVisible(remoteGroupMenuKey(group.key), $event)
                                                ">
                                                <DropdownMenuTrigger asChild>
                                                    <Button
                                                        class="rounded-full"
                                                        variant="ghost"
                                                        size="icon-sm"
                                                        :ariaLabel="t('nav_tooltip.manage')"
                                                        @click.stop>
                                                        <MoreHorizontal />
                                                    </Button>
                                                </DropdownMenuTrigger>
                                                <DropdownMenuContent side="right" class="w-50">
                                                    <DropdownMenuItem @click="handleRemoteRename(group)">
                                                        <span>{{ t('view.favorite.rename_tooltip') }}</span>
                                                    </DropdownMenuItem>
                                                    <DropdownMenuSub>
                                                        <DropdownMenuSubTrigger>
                                                            <span>{{ t('view.favorite.visibility_tooltip') }}</span>
                                                        </DropdownMenuSubTrigger>
                                                        <DropdownMenuPortal>
                                                            <DropdownMenuSubContent
                                                                side="right"
                                                                align="start"
                                                                class="w-[200px]">
                                                                <DropdownMenuCheckboxItem
                                                                    v-for="visibility in worldGroupVisibilityOptions"
                                                                    :key="visibility"
                                                                    :model-value="group.visibility === visibility"
                                                                    indicator-position="right"
                                                                    @select="
                                                                        handleVisibilitySelection(group, visibility)
                                                                    ">
                                                                    <span>{{
                                                                        t(`view.favorite.visibility.${visibility}`)
                                                                    }}</span>
                                                                </DropdownMenuCheckboxItem>
                                                            </DropdownMenuSubContent>
                                                        </DropdownMenuPortal>
                                                    </DropdownMenuSub>
                                                    <DropdownMenuItem
                                                        variant="destructive"
                                                        @click="handleRemoteClear(group)">
                                                        <span>{{ t('view.favorite.clear') }}</span>
                                                    </DropdownMenuItem>
                                                </DropdownMenuContent>
                                            </DropdownMenu>
                                        </div>
                                    </div>
                                </template>
                                <template v-else>
                                    <div
                                        v-for="group in worldGroupPlaceholders"
                                        :key="group.key"
                                        :class="[
                                            'group-item x-hover-card hover:shadow-sm',
                                            'pointer-events-none opacity-70',
                                            { 'is-active': !hasSearchInput && isGroupActive('remote', group.key) }
                                        ]">
                                        <div class="flex items-start justify-between mb-1 text-[13px]">
                                            <span class="font-semibold">{{ group.displayName }}</span>
                                            <span class="text-xs">--/--</span>
                                        </div>
                                        <div class="flex items-center justify-between gap-2">
                                            <div class="w-16 h-[18px] rounded-full"></div>
                                        </div>
                                    </div>
                                </template>
                            </div>
                        </div>
                        <div class="flex flex-col gap-2">
                            <div class="flex items-center justify-between font-semibold text-sm mb-[9px]">
                                <span>{{ t('view.favorite.worlds.local_favorites') }}</span>
                                <Button
                                    class="rounded-full"
                                    size="icon-sm"
                                    variant="ghost"
                                    :ariaLabel="t('common.actions.refresh')"
                                    v-if="!refreshingLocalFavorites"
                                    @click.stop="refreshLocalWorldFavorites"
                                    ><RefreshCcw
                                /></Button>
                                <Button size="icon-sm" variant="ghost" v-else @click.stop="cancelLocalWorldRefresh">
                                    <RefreshCcw />
                                    {{ t('view.favorite.worlds.cancel_refresh') }}
                                </Button>
                            </div>
                            <div class="flex flex-col gap-2">
                                <template v-if="localWorldFavoriteGroups.length">
                                    <div
                                        v-for="group in localWorldFavoriteGroups"
                                        :key="group"
                                        :class="[
                                            'group-item x-hover-card hover:shadow-sm',
                                            { 'is-active': !hasSearchInput && isGroupActive('local', group) }
                                        ]"
                                        @click="handleGroupClick('local', group)">
                                        <div class="flex items-start justify-between mb-1 text-[13px]">
                                            <span class="font-semibold">{{ group }}</span>
                                            <div class="flex items-center flex-col">
                                                <span class="text-xs">{{ localWorldFavGroupLength(group) }}</span>
                                                <div class="flex items-center justify-between gap-2">
                                                    <DropdownMenu
                                                        :open="activeGroupMenu === localGroupMenuKey(group)"
                                                        @update:open="
                                                            handleGroupMenuVisible(localGroupMenuKey(group), $event)
                                                        ">
                                                        <DropdownMenuTrigger asChild>
                                                            <Button
                                                                class="rounded-full"
                                                                size="icon-sm"
                                                                variant="ghost"
                                                                @click.stop
                                                                ><Ellipsis
                                                            /></Button>
                                                        </DropdownMenuTrigger>
                                                        <DropdownMenuContent side="right" class="w-50">
                                                            <DropdownMenuItem @click="handleLocalRename(group)">
                                                                <span>{{ t('view.favorite.rename_tooltip') }}</span>
                                                            </DropdownMenuItem>
                                                            <DropdownMenuItem
                                                                variant="destructive"
                                                                @click="handleLocalDelete(group)">
                                                                <span>{{ t('view.favorite.delete_tooltip') }}</span>
                                                            </DropdownMenuItem>
                                                        </DropdownMenuContent>
                                                    </DropdownMenu>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </template>
                                <div v-else class="text-center text-xs py-3">
                                    <DataTableEmpty type="nodata" />
                                </div>
                                <div
                                    v-if="!isCreatingLocalGroup"
                                    class="group-item x-hover-card hover:shadow-sm border-dashed flex items-center justify-center gap-2 text-sm"
                                    @click="startLocalGroupCreation">
                                    <Plus />
                                    <span>{{ t('view.favorite.worlds.new_group') }}</span>
                                </div>
                                <InputGroupField
                                    v-else
                                    ref="newLocalGroupInput"
                                    v-model="newLocalGroupName"
                                    size="sm"
                                    class="w-full"
                                    :placeholder="t('view.favorite.worlds.new_group')"
                                    @keyup.enter="handleLocalGroupCreationConfirm"
                                    @keyup.esc="cancelLocalGroupCreation"
                                    @blur="cancelLocalGroupCreation" />
                            </div>
                        </div>
                    </div>
                </ResizablePanel>
                <ResizableHandle @dragging="splitterSetDragging" />
                <ResizablePanel :order="2">
                    <div class="flex flex-col h-full min-h-0 pl-[26px]">
                        <FavoritesContentHeader
                            v-model:edit-mode="worldEditMode"
                            :edit-mode-disabled="isSearchActive"
                            :edit-mode-visible="worldEditMode && !isSearchActive"
                            :is-all-selected="isAllWorldsSelected"
                            :has-selection="hasWorldSelection"
                            @toggle-select-all="toggleSelectAllWorlds"
                            @clear-selection="clearSelectedWorlds"
                            @copy-selection="copySelectedWorlds"
                            @bulk-unfavorite="showWorldBulkUnfavoriteSelectionConfirm">
                            <template #title>
                                <span v-if="isSearchActive">{{ t('view.favorite.worlds.search') }}</span>
                                <template v-else-if="activeRemoteGroup">
                                    <span
                                        >{{ activeRemoteGroup.displayName }} &nbsp;<small
                                            >{{ activeRemoteGroup.count }}/{{ activeRemoteGroup.capacity }}</small
                                        ></span
                                    >
                                </template>
                                <span v-else-if="activeLocalGroupName">
                                    {{ activeLocalGroupName }}
                                    <small>{{ activeLocalGroupCount }}</small>
                                </span>
                                <span v-else>No Group Selected</span>
                            </template>
                        </FavoritesContentHeader>
                        <div ref="worldFavoritesContainerRef" class="flex-1 min-h-0">
                            <template v-if="isSearchActive">
                                <div class="h-full pr-2 overflow-auto">
                                    <template v-if="worldFavoriteSearchResults.length">
                                        <div
                                            class="favorites-card-list"
                                            :style="worldFavoritesGridStyle(worldFavoriteSearchResults.length)">
                                            <FavoritesWorldItem
                                                v-for="favorite in worldFavoriteSearchResults"
                                                :key="favorite.id"
                                                :favorite="favorite"
                                                is-local-favorite />
                                        </div>
                                    </template>
                                    <div v-else class="flex items-center justify-center text-[13px] h-full">
                                        <DataTableEmpty type="nomatch" />
                                    </div>
                                </div>
                            </template>
                            <template v-else>
                                <div
                                    v-if="activeRemoteGroup && isRemoteGroupSelected"
                                    class="h-full pr-2 overflow-auto">
                                    <template v-if="currentRemoteFavorites.length">
                                        <div
                                            class="favorites-card-list"
                                            :style="worldFavoritesGridStyle(currentRemoteFavorites.length)">
                                            <FavoritesWorldItem
                                                v-for="favorite in currentRemoteFavorites"
                                                :key="favorite.id"
                                                :group="activeRemoteGroup"
                                                :favorite="favorite"
                                                :edit-mode="worldEditMode"
                                                :selected="selectedFavoriteWorlds.includes(favorite.id)"
                                                @toggle-select="toggleWorldSelection(favorite.id, $event)" />
                                        </div>
                                    </template>
                                    <div v-else class="flex items-center justify-center text-[13px] h-full">
                                        <DataTableEmpty type="nodata" />
                                    </div>
                                </div>
                                <div
                                    v-else-if="activeLocalGroupName && isLocalGroupSelected"
                                    ref="localFavoritesViewportRef"
                                    class="h-full pr-2 overflow-auto favorites-content__scroll--local focus-visible:ring-ring/50 size-full rounded-[inherit] transition-[color,box-shadow] outline-none focus-visible:ring-[3px] focus-visible:outline-1"
                                    data-reka-scroll-area-viewport=""
                                    data-slot="scroll-area-viewport"
                                    tabindex="0"
                                    style="overflow: hidden scroll">
                                    <template v-if="currentLocalFavorites.length">
                                        <div class="favorites-card-virtual" :style="localVirtualContainerStyle">
                                            <template
                                                v-for="item in localVirtualItems"
                                                :key="String(item.virtualItem.key)">
                                                <div
                                                    v-if="item.row"
                                                    class="favorites-card-virtual-row"
                                                    :data-index="item.virtualItem.index"
                                                    :ref="localVirtualizer.measureElement"
                                                    :style="{ transform: `translateY(${item.virtualItem.start}px)` }">
                                                    <div class="favorites-card-virtual-row-grid">
                                                        <FavoritesWorldItem
                                                            v-for="favorite in getLocalRowItems(item.row)"
                                                            :key="favorite.key"
                                                            :group="activeLocalGroupName"
                                                            :favorite="favorite.favorite"
                                                            :edit-mode="worldEditMode"
                                                            is-local-favorite />
                                                    </div>
                                                </div>
                                            </template>
                                        </div>
                                    </template>
                                    <div v-else class="flex items-center justify-center text-[13px] h-full">
                                        <DataTableEmpty type="nodata" />
                                    </div>
                                </div>
                                <div v-else class="flex items-center justify-center text-[13px] h-full">
                                    <DataTableEmpty type="nodata" />
                                </div>
                            </template>
                        </div>
                    </div>
                </ResizablePanel>
            </ResizablePanelGroup>
        </div>
        <WorldExportDialog v-model:worldExportDialogVisible="worldExportDialogVisible" />
    </div>
</template>

<script setup>
    import { computed, nextTick, ref, watch } from 'vue';
    import { Ellipsis, MoreHorizontal, Plus, RefreshCcw, RefreshCw } from 'lucide-vue-next';
    import { Button } from '@/components/ui/button';
    import { DataTableEmpty } from '@/components/ui/data-table';
    import { InputGroupField } from '@/components/ui/input-group';
    import { Spinner } from '@/components/ui/spinner';
    import { storeToRefs } from 'pinia';
    import { useI18n } from 'vue-i18n';
    import { useVirtualizer } from '@tanstack/vue-virtual';

    import {
        DropdownMenu,
        DropdownMenuCheckboxItem,
        DropdownMenuContent,
        DropdownMenuItem,
        DropdownMenuPortal,
        DropdownMenuSub,
        DropdownMenuSubContent,
        DropdownMenuSubTrigger,
        DropdownMenuTrigger
    } from '../../components/ui/dropdown-menu';
    import { useAppearanceSettingsStore, useFavoriteStore, useModalStore } from '../../stores';
    import { ResizableHandle, ResizablePanel, ResizablePanelGroup } from '../../components/ui/resizable';
    import { worldRequest } from '../../api';
    import { debounce } from '../../shared/utils';
    import { useFavoritesCardScaling } from './composables/useFavoritesCardScaling.js';
    import { useFavoritesGroupPanel } from './composables/useFavoritesGroupPanel.js';
    import { useFavoritesLocalGroups } from './composables/useFavoritesLocalGroups.js';
    import {
        groupFavoritesByGroupKey,
        useFavoritesPage,
        useLocalFavoritesRefresh
    } from './composables/useFavoritesPage.js';
    import { useFavoritesSplitter } from './composables/useFavoritesSplitter.js';
    import {
        renameLocalWorldFavoriteGroup,
        removeLocalWorldFavorite,
        newLocalWorldFavoriteGroup,
        refreshFavorites,
        getLocalWorldFavorites
    } from '../../coordinators/favoriteCoordinator';

    import FavoritesContentHeader from './components/FavoritesContentHeader.vue';
    import FavoritesToolbar from './components/FavoritesToolbar.vue';
    import FavoritesWorldItem from './components/FavoritesWorldItem.vue';
    import WorldExportDialog from './dialogs/WorldExportDialog.vue';

    const WORLD_GROUP_PLACEHOLDERS = Array.from({ length: 4 }, (_, index) => ({
        key: `world:worlds${index + 1}`,
        displayName: `Group ${index + 1}`
    }));

    const { t } = useI18n();
    const { sortFavorites } = storeToRefs(useAppearanceSettingsStore());
    const { setSortFavorites } = useAppearanceSettingsStore();
    const favoriteStore = useFavoriteStore();
    const modalStore = useModalStore();
    const {
        favoriteWorlds,
        favoriteWorldGroups,
        localWorldFavorites,
        selectedFavoriteWorlds,
        worldImportDialogInput,
        isFavoriteLoading,
        localWorldFavoriteGroups
    } = storeToRefs(favoriteStore);
    const { showWorldImportDialog, localWorldFavGroupLength, deleteLocalWorldFavoriteGroup, handleFavoriteGroup } =
        favoriteStore;

    const {
        cardScale: worldCardScale,
        cardSpacing: worldCardSpacing,
        scalePercent: worldCardScalePercent,
        spacingPercent: worldCardSpacingPercent,
        scaleValue: worldCardScaleValue,
        spacingValue: worldCardSpacingValue,
        slider: worldCardScaleSlider,
        spacingSlider: worldCardSpacingSlider,
        containerRef: worldFavoritesContainerRef,
        gridStyle: worldFavoritesGridStyle
    } = useFavoritesCardScaling({
        configKey: 'VRCX_FavoritesWorldCardScale',
        spacingConfigKey: 'VRCX_FavoritesWorldCardSpacing'
    });

    const worldGroupVisibilityOptions = ref(['public', 'friends', 'private']);
    const {
        splitterGroupRef,
        splitterPanelRef,
        defaultSize: splitterDefaultSize,
        minSize: splitterMinSize,
        maxSize: splitterMaxSize,
        handleLayout,
        setDragging: splitterSetDragging
    } = useFavoritesSplitter({ configKey: 'VRCX_FavoritesWorldSplitter' });
    const worldGroupPlaceholders = WORLD_GROUP_PLACEHOLDERS;
    const worldSortMode = ref('none');
    const worldSearchMode = ref('name');

    const worldSearchPlaceholder = computed(() =>
        worldSearchMode.value === 'tag' ? t('view.favorite.worlds.search_by_tag') : t('view.favorite.worlds.search')
    );

    const worldExtraSortOptions = computed(() => [
        { value: 'players', label: t('view.settings.appearance.appearance.sort_favorite_by_players') }
    ]);

    const worldSortValue = computed(() => {
        if (worldSortMode.value === 'players') return 'players';
        return sortFavorites.value ? 'date' : 'name';
    });

    const {
        activeGroupMenu,
        hasUserSelectedGroup: hasUserSelectedWorldGroup,
        remoteGroupsResolved,
        isRemoteGroupSelected,
        isLocalGroupSelected,
        remoteGroupMenuKey,
        localGroupMenuKey,
        activeRemoteGroup,
        activeLocalGroupName,
        activeLocalGroupCount,
        handleGroupMenuVisible,
        selectGroup,
        isGroupActive,
        ensureSelectedGroup
    } = useFavoritesGroupPanel({
        remoteGroups: favoriteWorldGroups,
        localGroups: localWorldFavoriteGroups,
        localFavorites: localWorldFavorites,
        clearSelection: () => {
            selectedFavoriteWorlds.value = [];
        },
        placeholders: WORLD_GROUP_PLACEHOLDERS
    });

    const groupedWorldFavorites = computed(() => groupFavoritesByGroupKey(favoriteWorlds.value));

    const flattenedLocalWorldFavorites = computed(() => {
        return Object.values(localWorldFavorites.value).flat();
    });

    const searchableWorldEntries = computed(() => {
        const seen = new Set();
        const entries = [];
        const pushIfNew = (ref) => {
            if (!ref || !ref.id || seen.has(ref.id)) {
                return;
            }
            seen.add(ref.id);
            entries.push(ref);
        };
        flattenedLocalWorldFavorites.value.forEach(pushIfNew);
        favoriteWorlds.value.forEach((favorite) => {
            pushIfNew(favorite.ref);
        });
        return entries;
    });

    const currentRemoteFavorites = computed(() => {
        if (!activeRemoteGroup.value) {
            return [];
        }
        const list = groupedWorldFavorites.value[activeRemoteGroup.value.key] || [];
        if (worldSortMode.value === 'players') {
            return list.toSorted((a, b) => (b.ref?.occupants ?? 0) - (a.ref?.occupants ?? 0));
        }
        return list;
    });

    const currentLocalFavorites = computed(() => {
        if (!activeLocalGroupName.value) {
            return [];
        }
        const list = localWorldFavorites.value[activeLocalGroupName.value] || [];
        if (worldSortMode.value === 'players') {
            return list.toSorted((a, b) => (b.occupants ?? 0) - (a.occupants ?? 0));
        }
        return list;
    });

    const {
        exportDialogVisible: worldExportDialogVisible,
        search: worldFavoriteSearch,
        searchResults: worldFavoriteSearchResults,
        editMode: worldEditMode,
        toolbarMenuOpen: worldToolbarMenuOpen,
        hasSelection: hasWorldSelection,
        hasSearchInput,
        isSearchActive,
        isAllSelected: isAllWorldsSelected,
        handleImportClick: handleWorldImportClick,
        handleExportClick: handleWorldExportClick,
        toggleSelection: toggleWorldSelection,
        clearSelection: clearSelectedWorlds,
        toggleSelectAll: toggleSelectAllWorlds,
        copySelected: copySelectedWorlds,
        bulkUnfavoriteRemote: bulkUnfavoriteSelectedWorlds,
        handleVisibilitySelection,
        handleRemoteRename,
        handleRemoteClear,
        formatVisibility
    } = useFavoritesPage({
        selected: selectedFavoriteWorlds,
        activeRemoteGroup,
        currentRemoteFavorites,
        importInput: worldImportDialogInput,
        showImportDialog: showWorldImportDialog,
        handleFavoriteGroup,
        refreshFavorites,
        handleGroupMenuVisible,
        remoteGroupMenuKey,
        // world groups are either 'world' or 'vrcPlusWorld'
        remoteGroupType: (group) => group.type
    });

    const {
        refreshingLocalFavorites,
        refreshLocalFavorites: refreshLocalWorldFavorites,
        cancelLocalRefresh: cancelLocalWorldRefresh
    } = useLocalFavoritesRefresh(
        () => favoriteStore.localWorldFavoritesList,
        async (worldId) => {
            await worldRequest.getWorld({
                worldId
            });
        }
    );

    const localFavoritesViewportRef = ref(null);

    const getFavoritesGridMetrics = (count = 1, options = {}) => {
        const styleFn = worldFavoritesGridStyle.value;
        const styles = typeof styleFn === 'function' ? styleFn(count, options) : {};
        const columnsRaw = styles['--favorites-grid-columns'] ?? 1;
        const gapRaw = styles['--favorites-card-gap'] ?? 12;
        const columns = Math.max(1, Number(columnsRaw) || 1);
        const gap = Number(String(gapRaw).replace('px', '')) || 0;

        return {
            columns,
            gap,
            styles
        };
    };

    const chunkLocalFavorites = (favorites = []) => {
        const items = Array.isArray(favorites) ? favorites : [];
        if (!items.length) {
            return [];
        }
        const { columns } = getFavoritesGridMetrics(items.length, { matchMaxColumnWidth: true });
        const safeColumns = Math.max(1, columns || 1);
        const rows = [];

        for (let index = 0; index < items.length; index += safeColumns) {
            rows.push({
                type: 'cards',
                key: `local:${activeLocalGroupName.value}:${index}`,
                items: items.slice(index, index + safeColumns).map((favorite) => ({
                    key: favorite.id ?? favorite.worldId ?? favorite.name ?? `${activeLocalGroupName.value}:${index}`,
                    favorite
                }))
            });
        }

        return rows;
    };

    const localVirtualRows = computed(() => chunkLocalFavorites(currentLocalFavorites.value));

    const estimateLocalRowSize = (row) => {
        if (!row) {
            return 120;
        }
        const itemCount = Array.isArray(row.items) ? row.items.length : 0;
        const { columns, gap } = getFavoritesGridMetrics(itemCount, { matchMaxColumnWidth: true });
        const safeColumns = Math.max(1, columns || 1);
        const rows = Math.max(1, Math.ceil(itemCount / safeColumns));
        const baseCardHeight = 220;
        const rowGap = Math.max(0, gap);

        return rows * baseCardHeight + (rows - 1) * rowGap + 8;
    };

    const localVirtualizer = useVirtualizer(
        computed(() => ({
            count: localVirtualRows.value.length,
            getScrollElement: () => localFavoritesViewportRef.value,
            estimateSize: (index) => estimateLocalRowSize(localVirtualRows.value[index]),
            overscan: 8
        }))
    );

    const localVirtualItems = computed(() => {
        const items = localVirtualizer.value?.getVirtualItems?.() ?? [];
        return items.map((virtualItem) => ({
            virtualItem,
            row: localVirtualRows.value[virtualItem.index]
        }));
    });

    const localVirtualContainerStyle = computed(() => ({
        ...getFavoritesGridMetrics(currentLocalFavorites.value.length, { matchMaxColumnWidth: true }).styles,
        height: `${localVirtualizer.value?.getTotalSize?.() ?? 0}px`
    }));

    const getLocalRowItems = (row) => (row && Array.isArray(row.items) ? row.items : []);

    /**
     * @param value
     */
    function handleSortValueChange(value) {
        if (value === 'players') {
            worldSortMode.value = 'players';
            return;
        }
        worldSortMode.value = 'none';
        const next = value === 'date';
        if (next !== sortFavorites.value) {
            setSortFavorites();
        }
    }

    watch(
        () => ({
            remote: favoriteWorldGroups.value.map((group) => group.key),
            local: [...localWorldFavoriteGroups.value]
        }),
        () => {
            remoteGroupsResolved.value = favoriteWorldGroups.value.length > 0;
            if (!hasUserSelectedWorldGroup.value) {
                const preferredKey = favoriteWorldGroups.value[0]?.key || worldGroupPlaceholders[0]?.key || null;
                if (preferredKey) {
                    selectGroup('remote', preferredKey);
                }
            }
            ensureSelectedGroup();
        },
        { immediate: true }
    );

    watch(worldSearchMode, () => {
        if (isSearchActive.value) {
            doSearchWorldFavorites();
        }
    });

    watch([currentLocalFavorites, worldCardScale, worldCardSpacing, activeLocalGroupName], () => {
        nextTick(() => {
            localVirtualizer.value?.measure?.();
        });
    });

    /**
     * @param type
     * @param key
     */
    function handleGroupClick(type, key) {
        if (hasSearchInput.value) {
            worldFavoriteSearch.value = '';
            doSearchWorldFavorites('');
        }
        selectGroup(type, key, { userInitiated: true });
    }

    const {
        isCreatingLocalGroup,
        newLocalGroupName,
        newLocalGroupInput,
        startLocalGroupCreation,
        cancelLocalGroupCreation,
        handleLocalGroupCreationConfirm
    } = useFavoritesLocalGroups({
        createGroup: newLocalWorldFavoriteGroup,
        selectGroup
    });

    function showWorldBulkUnfavoriteSelectionConfirm() {
        if (!selectedFavoriteWorlds.value.length) {
            return;
        }
        const total = selectedFavoriteWorlds.value.length;
        modalStore
            .confirm({
                description: `Are you sure you want to unfavorite ${total} favorites?
                This action cannot be undone.`,
                title: `Delete ${total} favorites?`
            })
            .then(({ ok }) => {
                if (ok) {
                    bulkUnfavoriteSelectedWorlds([...selectedFavoriteWorlds.value]);
                }
            })
            .catch(() => {});
    }

    function handleRefreshFavorites() {
        refreshFavorites();
        getLocalWorldFavorites();
    }

    /**
     * @param group
     */
    function promptLocalWorldFavoriteGroupRename(group) {
        modalStore
            .prompt({
                title: t('prompt.local_favorite_group_rename.header'),
                description: t('prompt.local_favorite_group_rename.description'),
                confirmText: t('prompt.local_favorite_group_rename.save'),
                cancelText: t('prompt.local_favorite_group_rename.cancel'),
                pattern: /\S+/,
                errorMessage: t('prompt.local_favorite_group_rename.input_error'),
                inputValue: group
            })
            .then(({ ok, value }) => {
                if (!ok) return;
                if (value) {
                    renameLocalWorldFavoriteGroup(value, group);
                    nextTick(() => {
                        if (localWorldFavoriteGroups.value.includes(value)) {
                            selectGroup('local', value, { userInitiated: true });
                        }
                    });
                }
            })
            .catch(() => {});
    }

    /**
     * @param group
     */
    function promptLocalWorldFavoriteGroupDelete(group) {
        modalStore
            .confirm({
                description: t('confirm.delete_group', { name: group }),
                title: t('confirm.title')
            })
            .then(({ ok }) => {
                if (ok) {
                    deleteLocalWorldFavoriteGroup(group);
                }
            })
            .catch(() => {});
    }

    /**
     * @param worldFavoriteSearch
     */
    function doSearchWorldFavorites(searchInput) {
        const search = (searchInput ?? worldFavoriteSearch.value).trim().toLowerCase();
        if (search.length < 3) {
            worldFavoriteSearchResults.value = [];
            return;
        }
        const isTagMode = worldSearchMode.value === 'tag';
        const filtered = searchableWorldEntries.value.filter((ref) => {
            if (!ref || typeof ref.id === 'undefined' || typeof ref.name === 'undefined') {
                return false;
            }
            if (isTagMode) {
                if (Array.isArray(ref.tags)) {
                    return ref.tags.some(
                        (tag) => tag.startsWith('author_tag_') && tag.substring(11).toLowerCase().includes(search)
                    );
                }
                return false;
            }
            const authorName = ref.authorName || '';
            return ref.name.toLowerCase().includes(search) || authorName.toLowerCase().includes(search);
        });
        worldFavoriteSearchResults.value = filtered;
    }
    const searchWorldFavorites = debounce(doSearchWorldFavorites, 200);

    /**
     * @param groupName
     */
    function handleLocalRename(groupName) {
        handleGroupMenuVisible(localGroupMenuKey(groupName), false);
        promptLocalWorldFavoriteGroupRename(groupName);
    }

    /**
     * @param groupName
     */
    function handleLocalDelete(groupName) {
        handleGroupMenuVisible(localGroupMenuKey(groupName), false);
        promptLocalWorldFavoriteGroupDelete(groupName);
    }
</script>

<style>
    @import './favorites-layout.css';

    /* World-specific: scrollbar for local favorites */
    .favorites-content__scroll--local {
        scrollbar-width: thin;
        scrollbar-color: var(--border) transparent;
    }

    .favorites-content__scroll--local::-webkit-scrollbar {
        width: 10px;
    }

    .favorites-content__scroll--local::-webkit-scrollbar-track {
        background: transparent;
    }

    .favorites-content__scroll--local::-webkit-scrollbar-thumb {
        background-color: var(--border);
        border-radius: var(--rounded-full);
        border: 2px solid transparent;
        background-clip: content-box;
    }

    /* World-specific: virtual row layout */
    .favorites-card-virtual {
        width: 100%;
        position: relative;
        box-sizing: border-box;
    }

    .favorites-card-virtual-row {
        width: 100%;
        position: absolute;
        left: 0;
        top: 0;
        box-sizing: border-box;
        padding-bottom: var(--favorites-card-gap, 12px);
    }

    .favorites-card-virtual-row-grid {
        display: grid;
        grid-template-columns: repeat(
            var(--favorites-grid-columns, 1),
            minmax(var(--favorites-card-min-width, 260px), var(--favorites-card-target-width, 1fr))
        );
        gap: var(--favorites-card-gap, 12px);
        justify-content: start;
        padding: 4px 2px 0 2px;
        box-sizing: border-box;
    }
</style>
