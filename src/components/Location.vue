<template>
    <component :is="enableContextMenu ? ContextMenu : Passthrough">
        <component :is="enableContextMenu ? ContextMenuTrigger : Passthrough" as-child>
            <div class="cursor-pointer" v-bind="$attrs">
                <div v-if="!text" class="text-transparent">-</div>
                <div v-show="text" class="flex items-center gap-2">
                    <template v-if="isAgeRestricted">
                        <TooltipWrapper
                            :content="t('dialog.user.info.instance_age_restricted_tooltip')"
                            :delay-duration="300"
                            side="top">
                            <div class="inline-flex items-center gap-1 text-muted-foreground">
                                <Lock class="size-3.5 shrink-0" />
                                <span>{{ t('dialog.user.info.instance_age_restricted') }}</span>
                            </div>
                        </TooltipWrapper>
                    </template>
                    <template v-else>
                        <div v-if="region" :class="cn('flags shrink-0', region)"></div>
                        <TooltipWrapper
                            :content="tooltipContent"
                            :disabled="tooltipDisabled"
                            :delay-duration="300"
                            side="top">
                            <div
                                :class="locationClasses"
                                class="inline-flex min-w-0 flex-nowrap items-center overflow-hidden truncate"
                                @click="handleShowWorldDialog">
                                <Spinner v-if="isTraveling" class="mr-1 shrink-0" />
                                <span class="min-w-0 flex-1 truncate">
                                    <span>{{ text }}</span>
                                    <span v-if="showInstanceIdInLocation && instanceName" class="ml-1">{{
                                        `· #${instanceName}`
                                    }}</span>
                                    <span v-if="groupName" class="cursor-pointer" @click.stop="handleShowGroupDialog">
                                        ({{ groupName }})
                                    </span>
                                </span>
                            </div>
                        </TooltipWrapper>
                        <div v-if="closedAt">
                            <TooltipWrapper side="top">
                                <template #content>
                                    {{ t('dialog.user.info.instance_closed_at') }}:
                                    {{ formatDateFilter(closedAt, 'long') }}
                                </template>
                                <AlertTriangle class="text-orange-500 my-auto" />
                            </TooltipWrapper>
                        </div>
                        <Lock v-if="strict" class="text-muted-foreground" />
                    </template>
                </div>
            </div>
        </component>
        <ContextMenuContent v-if="enableContextMenu && parsedLocation.isRealInstance && parsedLocation.worldId">
            <WorldActionMenuItems
                :can-open-instance-in-game="canOpenInstanceInGame"
                :show-share="true"
                :show-previous-instances="true"
                @view-details="handleShowWorldDialog"
                @share="handleShareLocation"
                @new-instance="handleNewInstance"
                @self-invite="handleNewInstanceSelfInvite"
                @show-previous-instances="handleShowPreviousInstances" />
        </ContextMenuContent>
    </component>
</template>

<script setup>
    import { computed, reactive, ref, watch } from 'vue';
    import { AlertTriangle, Lock } from 'lucide-vue-next';
    import { storeToRefs } from 'pinia';
    import { useI18n } from 'vue-i18n';

    import { ContextMenu, ContextMenuContent, ContextMenuTrigger } from './ui/context-menu';

    import {
        getGroupName,
        getLocationText,
        getWorldName,
        copyToClipboard,
        formatDateFilter,
        parseLocation,
        resolveRegion,
        translateAccessType
    } from '../shared/utils';
    import {
        useAppearanceSettingsStore,
        useInstanceStore,
        useInviteStore,
        useSearchStore,
        useWorldStore
    } from '../stores';
    import { showGroupDialog } from '../coordinators/groupCoordinator';
    import { showWorldDialog } from '../coordinators/worldCoordinator';
    import { runNewInstanceSelfInviteFlow } from '../coordinators/inviteCoordinator';
    import { Spinner } from './ui/spinner';
    import WorldActionMenuItems from './WorldActionMenuItems.vue';
    import { accessTypeLocaleKeyMap } from '../shared/constants';
    import { cn } from '@/lib/utils';

    defineOptions({
        inheritAttrs: false
    });

    const Passthrough = (_, { slots }) => slots.default?.();

    const { t } = useI18n();

    const { cachedWorlds } = useWorldStore();
    const { showPreviousInstancesInfoDialog } = useInstanceStore();
    const { verifyShortName } = useSearchStore();
    const { cachedInstances } = useInstanceStore();
    const { lastInstanceApplied } = storeToRefs(useInstanceStore());
    const { canOpenInstanceInGame } = useInviteStore();
    const { showInstanceIdInLocation, isAgeGatedInstancesVisible } = storeToRefs(useAppearanceSettingsStore());

    const props = defineProps({
        location: String,
        traveling: String,
        hint: {
            type: String,
            default: ''
        },
        grouphint: {
            type: String,
            default: ''
        },
        excludeGroupName: {
            type: Boolean,
            default: false
        },
        link: {
            type: Boolean,
            default: true
        },
        disableTooltip: {
            type: Boolean,
            default: false
        },
        isOpenPreviousInstanceInfoDialog: {
            type: Boolean,
            default: false
        },
        enableContextMenu: {
            type: Boolean,
            default: false
        }
    });

    const isTraveling = computed(() => typeof props.traveling !== 'undefined' && props.location === 'traveling');
    const instanceId = computed(() => (isTraveling.value ? props.traveling : props.location));
    const parsedLocation = computed(() => parseLocation(instanceId.value));

    // Names fetched for worlds and groups that are not cached, keyed by id. A late result for an
    // id the component no longer shows is stored but never read, so it cannot overwrite a newer one.
    const fetchedNames = reactive(new Map());

    // The instance store writes cachedInstances without triggering reactivity, then sets
    // lastInstanceApplied to the id it changed, so re-read the cache when that id is ours.
    const instanceRevision = ref(0);
    watch(lastInstanceApplied, (id) => {
        if (id === instanceId.value) {
            instanceRevision.value++;
        }
    });
    const cachedInstance = computed(() => {
        instanceRevision.value;
        const L = parsedLocation.value;
        const cachedRef = L.isRealInstance ? cachedInstances.get(L.tag) : undefined;
        return { displayName: cachedRef?.displayName || '', closedAt: cachedRef?.closedAt || '' };
    });

    const worldNameToFetch = computed(() => {
        const { worldId } = parsedLocation.value;
        return worldId && typeof cachedWorlds.get(worldId) === 'undefined' ? worldId : '';
    });
    const groupIdToFetch = computed(() => {
        const L = parsedLocation.value;
        return L.isRealInstance && !props.excludeGroupName && !props.grouphint && L.groupId ? L.groupId : '';
    });
    watch(
        worldNameToFetch,
        (worldId) => {
            if (worldId) {
                fetchName(worldId, getWorldName(worldId));
            }
        },
        { immediate: true }
    );
    watch(
        groupIdToFetch,
        (groupId) => {
            if (groupId) {
                fetchName(groupId, getGroupName(instanceId.value));
            }
        },
        { immediate: true }
    );

    /**
     * @param {string} id
     * @param {Promise<string>} request
     */
    function fetchName(id, request) {
        request.then((name) => {
            if (name) {
                fetchedNames.set(id, name);
            }
        });
    }

    const text = computed(() => {
        const L = parsedLocation.value;
        const cachedRef = L.worldId ? cachedWorlds.get(L.worldId) : undefined;
        return getLocationText(L, {
            hint: props.hint,
            worldName: typeof cachedRef !== 'undefined' ? cachedRef.name : fetchedNames.get(L.worldId),
            accessTypeLabel: translateAccessType(L.accessTypeName, t, accessTypeLocaleKeyMap),
            t
        });
    });
    const region = computed(() => (parsedLocation.value.isRealInstance ? resolveRegion(parsedLocation.value) : ''));
    const strict = computed(() => parsedLocation.value.strict);
    const instanceName = computed(() => cachedInstance.value.displayName || parsedLocation.value.instanceName);
    const closedAt = computed(() => cachedInstance.value.closedAt);
    const groupName = computed(() => {
        const L = parsedLocation.value;
        if (!L.isRealInstance || props.excludeGroupName) {
            return '';
        }
        if (props.grouphint) {
            return props.grouphint;
        }
        return L.groupId ? fetchedNames.get(L.groupId) || L.groupId : '';
    });

    const isAgeRestricted = computed(() => !isAgeGatedInstancesVisible.value && parsedLocation.value.ageGate);
    const isLocationLink = computed(() => props.link && props.location !== 'private' && props.location !== 'offline');
    const locationClasses = computed(() => [
        'x-location',
        {
            'cursor-pointer': isLocationLink.value
        }
    ]);
    const tooltipContent = computed(() => `${t('dialog.new_instance.instance_id')}: #${instanceName.value}`);
    const tooltipDisabled = computed(
        () => props.disableTooltip || !instanceName.value || showInstanceIdInLocation.value
    );

    function handleShowWorldDialog() {
        if (props.link) {
            if (!instanceId.value && props.hint.length === 8) {
                verifyShortName('', props.hint);
                return;
            }
            if (props.isOpenPreviousInstanceInfoDialog) {
                showPreviousInstancesInfoDialog(instanceId.value);
            } else {
                showWorldDialog(instanceId.value);
            }
        }
    }

    function handleShowGroupDialog() {
        const { groupId } = parsedLocation.value;
        if (!groupId) {
            return;
        }
        showGroupDialog(groupId);
    }

    function handleShareLocation() {
        const L = parsedLocation.value;
        if (!L.worldId) return;
        copyToClipboard(`https://vrchat.com/home/world/${L.worldId}`, t('message.world.url_copied'));
    }

    function handleNewInstance() {
        const L = parsedLocation.value;
        if (!L.worldId) return;
        showWorldDialog(L.tag, L.shortName);
    }

    function handleNewInstanceSelfInvite() {
        const L = parsedLocation.value;
        if (!L.worldId) return;
        runNewInstanceSelfInviteFlow(L.worldId);
    }

    function handleShowPreviousInstances() {
        if (!instanceId.value) return;
        showPreviousInstancesInfoDialog(instanceId.value);
    }
</script>
