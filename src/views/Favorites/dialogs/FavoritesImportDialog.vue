<template>
    <Dialog v-model:open="isVisible">
        <DialogContent class="sm:max-w-xl">
            <DialogHeader>
                <DialogTitle>{{ t(`${config.i18nPrefix}.header`) }}</DialogTitle>
            </DialogHeader>
            <div style="display: flex; align-items: center; justify-content: space-between">
                <div class="text-xs">{{ t(`${config.i18nPrefix}.description`) }}</div>
                <div style="display: flex; align-items: center">
                    <div v-if="importDialog.progress">
                        {{ t(`${config.i18nPrefix}.process_progress`) }} {{ importDialog.progress }} /
                        {{ importDialog.progressTotal }}
                        <Spinner :class="config.progressSpinnerClass" />
                    </div>
                    <Button v-if="importDialog.loading" size="sm" :variant="config.cancelVariant" @click="cancelImport">
                        {{ t(`${config.i18nPrefix}.cancel`) }}
                    </Button>
                    <Button size="sm" v-else :disabled="!importDialog.input" @click="processImportList">
                        {{ t(`${config.i18nPrefix}.process_list`) }}
                    </Button>
                </div>
            </div>
            <InputGroupTextareaField v-model="importDialog.input" :rows="10" input-class="resize-none mt-2" />
            <div v-bind="config.inlineGroupRow ? inlineRowAttrs : {}">
                <div v-bind="config.inlineGroupRow ? {} : { class: 'mb-2' }">
                    <div class="flex items-center gap-2">
                        <Select
                            :class="config.inlineGroupRow ? 'mr-1.5' : undefined"
                            :model-value="importFavoriteGroupSelection"
                            @update:modelValue="handleImportGroupSelect">
                            <SelectTrigger size="sm">
                                <SelectValue :placeholder="t(config.groupPlaceholderKey)" />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectGroup>
                                    <SelectItem
                                        v-for="groupAPI in favoriteGroups"
                                        :key="groupAPI.name"
                                        :value="groupAPI.name"
                                        :disabled="groupAPI.count >= groupAPI.capacity">
                                        {{ groupAPI.displayName }} ({{ groupAPI.count }}/{{ groupAPI.capacity }})
                                    </SelectItem>
                                </SelectGroup>
                            </SelectContent>
                        </Select>

                        <Select
                            class="ml-2"
                            :model-value="importLocalFavoriteGroupSelection"
                            @update:modelValue="handleImportLocalGroupSelect">
                            <SelectTrigger size="sm">
                                <SelectValue :placeholder="t(config.localGroupPlaceholderKey)" />
                            </SelectTrigger>
                            <SelectContent>
                                <SelectGroup>
                                    <SelectItem v-for="group in localFavoriteGroups" :key="group" :value="group">
                                        {{ group }} ({{ localFavGroupLength(group) }})
                                    </SelectItem>
                                </SelectGroup>
                            </SelectContent>
                        </Select>
                    </div>
                    <span class="ml-1.5" v-if="importDialog.importFavoriteGroup">
                        {{ importTable.data.length }} /
                        {{ importDialog.importFavoriteGroup.capacity - importDialog.importFavoriteGroup.count }}
                    </span>
                </div>
                <div>
                    <Button
                        size="sm"
                        variant="secondary"
                        class="mr-2"
                        :disabled="config.disableClearWhenEmpty && importTable.data.length === 0"
                        @click="clearImportTable">
                        {{ t(`${config.i18nPrefix}.clear_table`) }}
                    </Button>
                    <Button
                        size="sm"
                        :disabled="
                            importTable.data.length === 0 ||
                            (!importDialog.importFavoriteGroup && !importDialog.importLocalFavoriteGroup)
                        "
                        @click="importImportTable">
                        {{ t(`${config.i18nPrefix}.import`) }}
                    </Button>
                </div>
            </div>
            <span class="m-2" v-if="importDialog.importProgress">
                <Spinner class="inline-block ml-2 mr-2" />
                {{ t(`${config.i18nPrefix}.import_progress`) }}
                {{ importDialog.importProgress }}/{{ importDialog.importProgressTotal }}
            </span>
            <br />
            <template v-if="importDialog.errors">
                <Button size="sm" variant="secondary" @click="importDialog.errors = ''">
                    {{ t(`${config.i18nPrefix}.clear_errors`) }}
                </Button>
                <h2 class="my-1.5 mx-0" style="font-weight: bold">
                    {{ t(`${config.i18nPrefix}.errors`) }}
                </h2>
                <pre class="whitespace-pre-wrap text-xs" v-text="importDialog.errors"></pre>
            </template>
            <DataTableLayout
                class="min-w-0 w-full"
                :table="table"
                :loading="importDialog.loading"
                :table-style="tableStyle"
                :show-pagination="false"
                style="margin-top: 8px" />
        </DialogContent>
    </Dialog>
</template>

<script setup>
    import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
    import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
    import { computed, ref, watch } from 'vue';
    import { Button } from '@/components/ui/button';
    import { DataTableLayout } from '@/components/ui/data-table';
    import { InputGroupTextareaField } from '@/components/ui/input-group';
    import { Spinner } from '@/components/ui/spinner';
    import { storeToRefs } from 'pinia';
    import { useI18n } from 'vue-i18n';

    import {
        addLocalAvatarFavorite,
        addLocalFriendFavorite,
        addLocalWorldFavorite
    } from '../../../coordinators/favoriteCoordinator';
    import { applyAvatar, showAvatarDialog } from '../../../coordinators/avatarCoordinator';
    import { avatarRequest, favoriteRequest, userRequest, worldRequest } from '../../../api';
    import { useFavoriteStore, useGalleryStore } from '../../../stores';
    import { createImportColumns } from './importColumns.jsx';
    import { removeFromArray } from '../../../shared/utils';
    import { showUserDialog } from '../../../coordinators/userCoordinator';
    import { showWorldDialog } from '../../../coordinators/worldCoordinator';
    import { useUserDisplay } from '../../../composables/useUserDisplay';
    import { useVrcxVueTable } from '../../../lib/table/useVrcxVueTable';

    const props = defineProps({
        kind: {
            type: String,
            required: true,
            validator: (value) => ['avatar', 'world', 'friend'].includes(value)
        }
    });

    const { t } = useI18n();

    const favoriteStore = useFavoriteStore();
    const { importDialogInput, importDialogVisible } = storeToRefs(favoriteStore);
    const { getCachedFavoritesByObjectId } = favoriteStore;
    const { showFullscreenImageDialog } = useGalleryStore();
    const { userImage, userImageFull } = useUserDisplay();

    /**
     * Everything that differs between the avatar, world and friend import dialogs.
     * The template-related flags keep each kind's existing markup unchanged.
     */
    const importKinds = {
        avatar: {
            i18nPrefix: 'dialog.avatar_import',
            groupPlaceholderKey: 'dialog.avatar_import.select_group_placeholder',
            localGroupPlaceholderKey: 'dialog.avatar_import.select_group_placeholder',
            persistKey: 'avatarImportDialog',
            idPrefix: 'avtr',
            idLabel: 'AvatarId',
            nameKey: 'name',
            alreadyFavoritedMessage: 'Avatar is already in favorites',
            groupsKey: 'favoriteAvatarGroups',
            localGroupsKey: 'localAvatarFavoriteGroups',
            localGroupLengthKey: 'localAvatarFavGroupLength',
            fetch: async (avatarId) => {
                const args = await avatarRequest.getAvatar({ avatarId });
                return applyAvatar(args.json);
            },
            addLocal: addLocalAvatarFavorite,
            inlineGroupRow: true,
            cancelVariant: 'secondary',
            progressSpinnerClass: 'inline-block ml-2 mr-2',
            disableClearWhenEmpty: false,
            columns: {
                getImage: (row) => row?.thumbnailImageUrl,
                getFullImage: (row) => row?.imageUrl,
                showAuthorAndStatus: true,
                onShowName: showAvatarDialog
            }
        },
        world: {
            i18nPrefix: 'dialog.world_import',
            groupPlaceholderKey: 'dialog.world_import.select_vrchat_group_placeholder',
            localGroupPlaceholderKey: 'dialog.world_import.select_local_group_placeholder',
            persistKey: 'worldImportDialog',
            idPrefix: 'wrld',
            idLabel: 'WorldId',
            nameKey: 'name',
            alreadyFavoritedMessage: 'World is already in favorites',
            groupsKey: 'favoriteWorldGroups',
            localGroupsKey: 'localWorldFavoriteGroups',
            localGroupLengthKey: 'localWorldFavGroupLength',
            fetch: async (worldId) => {
                const args = await worldRequest.getWorld({ worldId });
                return args.ref;
            },
            addLocal: addLocalWorldFavorite,
            inlineGroupRow: false,
            cancelVariant: 'outline',
            progressSpinnerClass: 'inline-block ml-2 mr-2',
            disableClearWhenEmpty: true,
            columns: {
                getImage: (row) => row?.thumbnailImageUrl,
                getFullImage: (row) => row?.imageUrl,
                showAuthorAndStatus: true,
                deleteButtonClass: 'w-6 h-6',
                onShowName: showWorldDialog
            }
        },
        friend: {
            i18nPrefix: 'dialog.friend_import',
            groupPlaceholderKey: 'dialog.friend_import.select_group_placeholder',
            // friend_import has no local placeholder key of its own
            localGroupPlaceholderKey: 'dialog.world_import.select_local_group_placeholder',
            persistKey: 'friendImportDialog',
            idPrefix: 'usr',
            idLabel: 'UserId',
            nameKey: 'displayName',
            alreadyFavoritedMessage: 'Friend is already in favorites',
            groupsKey: 'favoriteFriendGroups',
            localGroupsKey: 'localFriendFavoriteGroups',
            localGroupLengthKey: 'localFriendFavGroupLength',
            fetch: async (userId) => {
                const args = await userRequest.getUser({ userId });
                return args.ref;
            },
            addLocal: addLocalFriendFavorite,
            inlineGroupRow: false,
            cancelVariant: 'secondary',
            progressSpinnerClass: 'inline-block ml-1 mr-1',
            disableClearWhenEmpty: true,
            columns: {
                getImage: (row) => userImage(row),
                getFullImage: (row) => userImageFull(row),
                showAuthorAndStatus: false,
                onShowName: showUserDialog
            }
        }
    };

    const config = importKinds[props.kind];

    // Avatar puts the group selects and the table buttons on one row; the others stack them.
    const inlineRowAttrs = {
        class: 'mt-1.5',
        style: 'display: flex; align-items: center; justify-content: space-between'
    };

    const favoriteGroups = computed(() => favoriteStore[config.groupsKey]);
    const localFavoriteGroups = computed(() => favoriteStore[config.localGroupsKey]);
    const localFavGroupLength = (group) => favoriteStore[config.localGroupLengthKey](group);

    const importDialog = ref({
        loading: false,
        progress: 0,
        progressTotal: 0,
        input: '',
        idList: new Set(),
        errors: '',
        importFavoriteGroup: null,
        importLocalFavoriteGroup: null,
        importProgress: 0,
        importProgressTotal: 0
    });

    const importFavoriteGroupSelection = ref('');
    const importLocalFavoriteGroupSelection = ref('');

    const importTable = ref({
        data: [],
        layout: 'table'
    });

    const tableStyle = { maxHeight: '400px' };

    const rows = computed(() => (Array.isArray(importTable.value?.data) ? importTable.value.data.slice() : []));

    const { table } = useVrcxVueTable({
        persistKey: config.persistKey,
        get data() {
            return rows.value;
        },
        columns: createImportColumns({
            ...config.columns,
            nameKey: config.nameKey,
            onShowUser: showUserDialog,
            onShowFullscreenImage: showFullscreenImageDialog,
            onDelete: deleteItemImport
        }),
        getRowId: (row) => String(row?.id ?? ''),
        enablePagination: false,
        enableSorting: false
    });

    const isVisible = computed({
        get() {
            return importDialogVisible.value[props.kind];
        },
        set(value) {
            importDialogVisible.value[props.kind] = value;
        }
    });

    watch(
        () => importDialogVisible.value[props.kind],
        (value) => {
            if (value) {
                clearImportTable();
                resetImport();
                if (importDialogInput.value[props.kind]) {
                    importDialog.value.input = importDialogInput.value[props.kind];
                    importDialogInput.value[props.kind] = '';
                    processImportList();
                }
            }
        }
    );

    async function processImportList() {
        const D = importDialog.value;
        D.loading = true;
        const regexId = new RegExp(`${config.idPrefix}_[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}`, 'g');
        let match = [];
        const idList = new Set();
        while ((match = regexId.exec(D.input)) !== null) {
            idList.add(match[0]);
        }
        D.input = '';
        D.errors = '';
        D.progress = 0;
        D.progressTotal = idList.size;
        const data = Array.from(idList);
        for (let i = 0; i < data.length; ++i) {
            if (!isVisible.value) {
                resetImport();
            }
            if (!D.loading || !isVisible.value) {
                break;
            }
            const id = data[i];
            if (!D.idList.has(id)) {
                try {
                    const ref = await config.fetch(id);
                    importTable.value.data.push(ref);
                    D.idList.add(id);
                } catch (err) {
                    D.errors = D.errors.concat(`${config.idLabel}: ${id}\n${err}\n\n`);
                }
            }
            D.progress++;
        }
        D.loading = false;
        D.progress = 0;
        D.progressTotal = 0;
    }

    /**
     * @param ref
     */
    function deleteItemImport(ref) {
        removeFromArray(importTable.value.data, ref);
        importDialog.value.idList.delete(ref.id);
    }

    function resetImport() {
        importDialog.value.input = '';
        importDialog.value.errors = '';
    }

    function clearImportTable() {
        importTable.value.data = [];
        importDialog.value.idList = new Set();
    }

    /**
     * @param group
     */
    function selectImportGroup(group) {
        importDialog.value.importLocalFavoriteGroup = null;
        importDialog.value.importFavoriteGroup = group;
        importFavoriteGroupSelection.value = group?.name ?? '';
        importLocalFavoriteGroupSelection.value = '';
    }

    /**
     * @param group
     */
    function selectImportLocalGroup(group) {
        importDialog.value.importFavoriteGroup = null;
        importDialog.value.importLocalFavoriteGroup = group;
        importFavoriteGroupSelection.value = '';
        importLocalFavoriteGroupSelection.value = group ?? '';
    }

    /**
     * @param value
     */
    function handleImportGroupSelect(value) {
        importFavoriteGroupSelection.value = value;
        const group = favoriteGroups.value.find((g) => g.name === value) ?? null;
        selectImportGroup(group);
    }

    /**
     * @param value
     */
    function handleImportLocalGroupSelect(value) {
        importLocalFavoriteGroupSelection.value = value;
        selectImportLocalGroup(value || null);
    }

    function cancelImport() {
        importDialog.value.loading = false;
    }

    async function importImportTable() {
        const D = importDialog.value;
        if (!D.importFavoriteGroup && !D.importLocalFavoriteGroup) {
            return;
        }
        D.loading = true;
        const data = [...importTable.value.data].reverse();
        D.importProgressTotal = data.length;
        let ref = null;
        try {
            for (let i = data.length - 1; i >= 0; i--) {
                if (!D.loading || !isVisible.value) {
                    break;
                }
                ref = data[i];
                if (D.importFavoriteGroup) {
                    if (getCachedFavoritesByObjectId(ref.id)) {
                        throw new Error(config.alreadyFavoritedMessage);
                    }
                    await favoriteRequest.addFavorite({
                        type: D.importFavoriteGroup.type,
                        favoriteId: ref.id,
                        tags: D.importFavoriteGroup.name
                    });
                } else if (D.importLocalFavoriteGroup) {
                    config.addLocal(ref.id, D.importLocalFavoriteGroup);
                }
                removeFromArray(importTable.value.data, ref);
                D.idList.delete(ref.id);
                D.importProgress++;
            }
        } catch (err) {
            D.errors = `Name: ${ref?.[config.nameKey]}\n${config.idLabel}: ${ref?.id}\n${err}\n\n`;
        } finally {
            D.importProgress = 0;
            D.importProgressTotal = 0;
            D.loading = false;
        }
    }
</script>
