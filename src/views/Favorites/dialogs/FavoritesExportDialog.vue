<template>
    <Dialog v-model:open="isDialogVisible">
        <DialogContent class="sm:max-w-xl">
            <DialogHeader>
                <DialogTitle>{{ t(`${config.i18nPrefix}.header`) }}</DialogTitle>
            </DialogHeader>

            <div v-if="config.csvOptions" class="flex flex-col gap-2 mb-2">
                <label v-for="option in exportSelectOptions" :key="option.value" class="inline-flex items-center gap-2">
                    <Checkbox
                        :model-value="exportSelectedOptions.includes(option.label)"
                        @update:modelValue="(val) => toggleExportOption(option.label, val)" />
                    <span>{{ option.label }}</span>
                </label>
            </div>

            <GroupSelectRow :wrap="config.csvOptions">
                <Select :model-value="exportFavoriteGroupSelection" @update:modelValue="handleExportGroupSelect">
                    <SelectTrigger size="sm">
                        <SelectValue :placeholder="t(`${config.i18nPrefix}.all_favorites`)" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectGroup>
                            <SelectItem :value="EXPORT_ALL_VALUE">{{
                                t(`${config.i18nPrefix}.all_favorites`)
                            }}</SelectItem>
                            <SelectItem v-for="groupAPI in favoriteGroups" :key="groupAPI.name" :value="groupAPI.name">
                                {{ groupAPI.displayName }} ({{ groupAPI.count }}/{{ groupAPI.capacity }})
                            </SelectItem>
                        </SelectGroup>
                    </SelectContent>
                </Select>

                <Select
                    :class="config.localSelectClass"
                    :model-value="exportLocalFavoriteGroupSelection"
                    @update:modelValue="handleExportLocalGroupSelect">
                    <SelectTrigger size="sm">
                        <SelectValue :placeholder="t(`${config.i18nPrefix}.select_group`)" />
                    </SelectTrigger>
                    <SelectContent>
                        <SelectGroup>
                            <SelectItem :value="EXPORT_NONE_VALUE">{{ t('dialog.gallery_select.none') }}</SelectItem>
                            <SelectItem v-for="group in localFavoriteGroups" :key="group" :value="group">
                                {{ group }} ({{ localFavGroupLength(group) }})
                            </SelectItem>
                        </SelectGroup>
                    </SelectContent>
                </Select>
            </GroupSelectRow>
            <br />
            <InputGroupTextareaField
                v-model="exportContent"
                :rows="15"
                readonly
                input-class="resize-none mt-4"
                @click="handleCopyExportData" />
        </DialogContent>
    </Dialog>
</template>

<script setup>
    import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
    import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog';
    import { computed, h, ref, watch } from 'vue';
    import { Checkbox } from '@/components/ui/checkbox';
    import { InputGroupTextareaField } from '@/components/ui/input-group';
    import { toast } from 'vue-sonner';
    import { useI18n } from 'vue-i18n';

    import { formatCsvField, formatCsvRow } from '../../../shared/utils';
    import { useAvatarStore, useFavoriteStore, useUserStore, useWorldStore } from '../../../stores';

    const { t } = useI18n();

    const props = defineProps({
        kind: {
            type: String,
            required: true,
            validator: (value) => ['avatar', 'world', 'friend'].includes(value)
        },
        visible: {
            type: Boolean,
            required: true
        }
    });

    const emit = defineEmits(['update:visible']);

    const favoriteStore = useFavoriteStore();
    const avatarStore = useAvatarStore();
    const worldStore = useWorldStore();
    const userStore = useUserStore();

    /**
     * Wraps the two group selects in a flex row when wrap is set, otherwise
     * renders them directly (the friend dialog has no wrapper element).
     */
    const GroupSelectRow = (rowProps, { slots }) =>
        rowProps.wrap ? h('div', { class: 'flex items-center gap-2' }, slots.default?.()) : slots.default?.();
    GroupSelectRow.props = ['wrap'];

    // Avatars and worlds export the columns picked with the checkboxes.
    const selectableCsv = {
        csvOptions: true,
        localSelectClass: 'ml-2',
        resetLocalGroupOnOpen: true,
        localEntryToRef: (ref) => ref,
        formatFavorite: (favorite, fields) => formatCsvRow(favorite.ref, fields),
        formatRef: (ref, fields) => formatCsvRow(ref, fields)
    };

    /**
     * Everything that differs between the avatar, world and friend export dialogs.
     */
    const exportKinds = {
        avatar: {
            ...selectableCsv,
            i18nPrefix: 'dialog.avatar_export',
            favoritesKey: 'favoriteAvatars',
            groupsKey: 'favoriteAvatarGroups',
            localFavoritesKey: 'localAvatarFavorites',
            localListKey: 'localAvatarFavoritesList',
            localGroupsKey: 'localAvatarFavoriteGroups',
            localGroupLengthKey: 'localAvatarFavGroupLength',
            lookupCached: (id) => avatarStore.cachedAvatars.get(id)
        },
        world: {
            ...selectableCsv,
            i18nPrefix: 'dialog.world_export',
            favoritesKey: 'favoriteWorlds',
            groupsKey: 'favoriteWorldGroups',
            localFavoritesKey: 'localWorldFavorites',
            localListKey: 'localWorldFavoritesList',
            localGroupsKey: 'localWorldFavoriteGroups',
            localGroupLengthKey: 'localWorldFavGroupLength',
            lookupCached: (id) => worldStore.cachedWorlds.get(id)
        },
        friend: {
            // Friends always export "UserID,Name"; local groups hold user ids.
            csvOptions: false,
            csvHeader: 'UserID,Name',
            localSelectClass: 'mt-4',
            resetLocalGroupOnOpen: false,
            localEntryToRef: (userId) => userStore.cachedUsers.get(userId),
            formatFavorite: (favorite) => `${formatCsvField(favorite.id)},${formatCsvField(favorite.name)}`,
            formatRef: (ref) => `${formatCsvField(ref.id)},${formatCsvField(ref.displayName)}`,
            i18nPrefix: 'dialog.friend_export',
            favoritesKey: 'favoriteFriends',
            groupsKey: 'favoriteFriendGroups',
            localFavoritesKey: 'localFriendFavorites',
            localListKey: 'localFriendFavoritesList',
            localGroupsKey: 'localFriendFavoriteGroups',
            localGroupLengthKey: 'localFriendFavGroupLength',
            lookupCached: (id) => userStore.cachedUsers.get(id)
        }
    };

    const config = exportKinds[props.kind];

    const favoriteGroups = computed(() => favoriteStore[config.groupsKey]);
    const localFavoriteGroups = computed(() => favoriteStore[config.localGroupsKey]);
    const localFavGroupLength = (group) => favoriteStore[config.localGroupLengthKey](group);

    const exportContent = ref('');
    const exportFavoriteGroup = ref(null);
    const exportLocalFavoriteGroup = ref(null);

    const EXPORT_ALL_VALUE = '__all__';
    const EXPORT_NONE_VALUE = '__none__';

    const exportFavoriteGroupSelection = ref(EXPORT_ALL_VALUE);
    const exportLocalFavoriteGroupSelection = ref(EXPORT_NONE_VALUE);
    const exportSelectedOptions = ref(['ID', 'Name']);
    const exportSelectOptions = ref([
        { label: 'ID', value: 'id' },
        { label: 'Name', value: 'name' },
        { label: 'Author ID', value: 'authorId' },
        { label: 'Author Name', value: 'authorName' },
        { label: 'Thumbnail', value: 'thumbnailImageUrl' }
    ]);

    /**
     * @param label
     * @param checked
     */
    function toggleExportOption(label, checked) {
        const selection = exportSelectedOptions.value;
        const index = selection.indexOf(label);
        if (checked && index === -1) {
            selection.push(label);
        } else if (!checked && index !== -1) {
            selection.splice(index, 1);
        }
        updateExportContent();
    }

    const isDialogVisible = computed({
        get() {
            return props.visible;
        },
        set(value) {
            emit('update:visible', value);
        }
    });

    watch(
        () => props.visible,
        (value) => {
            if (value) {
                showExportDialog();
            }
        }
    );

    function showExportDialog() {
        exportFavoriteGroup.value = null;
        exportFavoriteGroupSelection.value = EXPORT_ALL_VALUE;
        if (config.resetLocalGroupOnOpen) {
            exportLocalFavoriteGroup.value = null;
            exportLocalFavoriteGroupSelection.value = EXPORT_NONE_VALUE;
        }
        updateExportContent();
    }

    /**
     * @param value
     */
    function handleExportGroupSelect(value) {
        exportFavoriteGroupSelection.value = value;
        if (value === EXPORT_ALL_VALUE) {
            selectExportGroup(null);
            return;
        }
        const group = favoriteGroups.value.find((g) => g.name === value) || null;
        selectExportGroup(group);
    }

    /**
     * @param value
     */
    function handleExportLocalGroupSelect(value) {
        exportLocalFavoriteGroupSelection.value = value;
        if (value === EXPORT_NONE_VALUE) {
            selectExportLocalGroup(null);
            return;
        }
        selectExportLocalGroup(value);
    }

    /**
     * @param event
     */
    function handleCopyExportData(event) {
        if (event.target.tagName === 'TEXTAREA') {
            event.target.select();
        }
        navigator.clipboard
            .writeText(exportContent.value)
            .then(() => {
                toast.success('Copied successfully!', { duration: 2000 });
            })
            .catch((err) => {
                console.error('Copy failed:', err);
                toast.error('Copy failed!');
            });
    }

    function updateExportContent() {
        const fields = exportSelectOptions.value
            .filter((option) => exportSelectedOptions.value.includes(option.label))
            .map((option) => option.value);

        const lines = [config.csvOptions ? exportSelectedOptions.value.join(',') : config.csvHeader];
        const favorites = favoriteStore[config.favoritesKey];

        if (exportFavoriteGroup.value) {
            favoriteStore[config.groupsKey].forEach((group) => {
                if (exportFavoriteGroup.value === group) {
                    favorites.forEach((favorite) => {
                        if (group.key === favorite.groupKey) {
                            lines.push(config.formatFavorite(favorite, fields));
                        }
                    });
                }
            });
        } else if (exportLocalFavoriteGroup.value) {
            const favoriteGroup = favoriteStore[config.localFavoritesKey][exportLocalFavoriteGroup.value];
            if (!favoriteGroup) {
                return;
            }
            favoriteGroup.forEach((entry) => {
                const ref = config.localEntryToRef(entry);
                if (typeof ref !== 'undefined') {
                    lines.push(config.formatRef(ref, fields));
                }
            });
        } else {
            // export all
            favorites.forEach((favorite) => {
                lines.push(config.formatFavorite(favorite, fields));
            });
            favoriteStore[config.localListKey].forEach((id) => {
                const ref = config.lookupCached(id);
                if (typeof ref !== 'undefined') {
                    lines.push(config.formatRef(ref, fields));
                }
            });
        }
        exportContent.value = lines.reverse().join('\n');
    }

    /**
     * @param group
     */
    function selectExportGroup(group) {
        exportFavoriteGroup.value = group;
        exportLocalFavoriteGroup.value = null;
        exportFavoriteGroupSelection.value = group?.name ?? EXPORT_ALL_VALUE;
        exportLocalFavoriteGroupSelection.value = EXPORT_NONE_VALUE;
        updateExportContent();
    }

    /**
     * @param group
     */
    function selectExportLocalGroup(group) {
        exportLocalFavoriteGroup.value = group;
        exportFavoriteGroup.value = null;
        exportLocalFavoriteGroupSelection.value = group ?? EXPORT_NONE_VALUE;
        exportFavoriteGroupSelection.value = EXPORT_ALL_VALUE;
        updateExportContent();
    }
</script>
