import { reactive, ref, watch } from 'vue';
import { defineStore } from 'pinia';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import {
    checkVRChatCache,
    computeDisabledContentSettings,
    createDefaultInstanceRef,
    debounce,
    displayLocation,
    evictMapCache,
    formatFileSize,
    getAvailablePlatforms,
    getBundleDateSize,
    getGroupName,
    getWorldName,
    hasGroupPermission,
    isRealInstance,
    parseLocation,
    replaceBioSymbols
} from '../shared/utils';
import { instanceRequest, queryRequest, userRequest, worldRequest } from '../api';
import { accessTypeLocaleKeyMap, instanceContentSettings } from '../shared/constants';
import { database } from '../services/database';
import {
    buildInstanceRooms,
    compareGroupDialogRooms,
    compareWorldDialogRooms,
    createInstanceRoom
} from '../shared/utils/instanceRooms';
import { resolveRef } from '../shared/utils/resolveRef';
import { useAppearanceSettingsStore } from './settings/appearance';
import { useFriendStore } from './friend';
import { useGroupStore } from './group';
import { useLocationStore } from './location';
import { useNotificationStore } from './notification';
import { usePhotonStore } from './photon';
import { useSharedFeedStore } from './sharedFeed';
import { useUiStore } from './ui';
import { useUserStore } from './user';
import { useWorldStore } from './world';
import { watchState } from '../services/watchState';

import configRepository from '../services/config';

export const useInstanceStore = defineStore('Instance', () => {
    const locationStore = useLocationStore();
    const worldStore = useWorldStore();
    const friendStore = useFriendStore();
    const appearanceSettingsStore = useAppearanceSettingsStore();
    const groupStore = useGroupStore();
    const notificationStore = useNotificationStore();
    const uiStore = useUiStore();
    const userStore = useUserStore();
    const sharedFeedStore = useSharedFeedStore();
    const photonStore = usePhotonStore();
    const { t } = useI18n();

    const state = reactive({
        updatePlayerListTimer: null,
        updatePlayerListPending: false
    });

    let cachedInstances = new Map();

    function cleanInstanceCache() {
        const friendLocationTags = new Set(
            [...friendStore.friends.values()].map((f) => f.$location?.tag).filter(Boolean)
        );
        evictMapCache(cachedInstances, 200, (_value, key) => friendLocationTags.has(key), {
            sortFn: (a, b) => (Date.parse(a.value.$fetchedAt) || 0) - (Date.parse(b.value.$fetchedAt) || 0),
            logLabel: 'Instance cache cleanup'
        });
    }

    const lastInstanceApplied = ref('');

    const instanceCategories = ref([]);
    const instanceVibes = ref([]);

    async function loadInstanceCategoriesAndVibes() {
        if (instanceCategories.value.length === 0) {
            try {
                const args = await instanceRequest.getInstanceCategories();
                instanceCategories.value = (args.json ?? [])
                    .filter((category) => !category.deleted)
                    .sort((a, b) => a.order - b.order);
            } catch (err) {
                console.error(err);
            }
        }
        if (instanceVibes.value.length === 0) {
            try {
                const args = await instanceRequest.getInstanceVibes();
                instanceVibes.value = (args.json ?? []).filter((vibe) => !vibe.deleted);
            } catch (err) {
                console.error(err);
            }
        }
    }

    const currentInstanceWorld = ref({
        ref: {},
        instance: {},
        isPC: false,
        isQuest: false,
        isIos: false,
        avatarScalingDisabled: false,
        focusViewDisabled: false,
        inCache: false,
        cacheSize: '',
        fileAnalysis: {}
    });

    /** @type {import('vue').Ref<any>} */
    const currentInstanceLocation = ref({});

    const queuedInstances = reactive(new Map());

    const previousInstancesInfoDialog = ref({
        instanceId: '',
        lastId: '',
        visible: false
    });

    const previousInstancesListDialog = ref({
        visible: false,
        variant: 'user',
        userRef: {
            id: '',
            displayName: ''
        },
        worldRef: {
            id: '',
            name: ''
        },
        groupRef: {
            id: '',
            name: ''
        },
        lastId: ''
    });

    const previousInstancesInfoState = ref({
        sortBy: [{ id: 'created_at', desc: true }],
        search: '',
        pageSize: 10,
        pageIndex: 0
    });

    const previousInstancesListState = ref({
        user: {
            sortBy: [{ id: 'created_at', desc: true }],
            search: '',
            pageSize: 10,
            pageIndex: 0
        },
        world: {
            sortBy: [{ id: 'created_at', desc: true }],
            search: '',
            pageSize: 10,
            pageIndex: 0
        },
        group: {
            sortBy: [{ id: 'created_at', desc: true }],
            search: '',
            pageSize: 10,
            pageIndex: 0
        }
    });

    const instanceJoinHistory = reactive(new Map());

    const currentInstanceUsersData = ref([]);

    watch(
        () => watchState.isLoggedIn,
        (isLoggedIn) => {
            currentInstanceUsersData.value = [];
            instanceJoinHistory.clear();
            hidePreviousInstancesDialogs();
            cachedInstances.clear();
            queuedInstances.clear();
            if (isLoggedIn) {
                getInstanceJoinHistory();
            }
        },
        { flush: 'sync' }
    );

    async function getInstanceJoinHistory() {
        try {
            const data = await database.getInstanceJoinHistory();
            instanceJoinHistory.clear();
            for (const [key, value] of data) {
                instanceJoinHistory.set(key, value);
            }
        } catch (error) {
            console.error('Failed to get instance join history:', error);
        }
    }

    /**
     * @param location
     * @param dateTime
     */
    function addInstanceJoinHistory(location, dateTime) {
        if (!location || !dateTime) {
            return;
        }

        if (instanceJoinHistory.has(location)) {
            instanceJoinHistory.delete(location);
        }

        const epoch = new Date(dateTime).getTime();
        instanceJoinHistory.set(location, epoch);
    }

    function hidePreviousInstancesDialogs() {
        previousInstancesInfoDialog.value.visible = false;
        previousInstancesListDialog.value.visible = false;
    }

    /**
     * @param {boolean} value
     */
    function setPreviousInstancesInfoDialogVisible(value) {
        previousInstancesInfoDialog.value.visible = value;
    }

    /**
     * @param {boolean} value
     */
    function setPreviousInstancesListDialogVisible(value) {
        previousInstancesListDialog.value.visible = value;
    }

    /**
     * @param input
     */
    function resolveUserRef(input) {
        return resolveRef(input, {
            emptyDefault: { id: '', displayName: '' },
            idAlias: 'userId',
            nameKey: 'displayName',
            fetchFn: (id) => queryRequest.fetch('user.dialog', { userId: id })
        });
    }

    /**
     * @param input
     */
    function resolveWorldRef(input) {
        return resolveRef(input, {
            emptyDefault: { id: '', name: '' },
            idAlias: 'worldId',
            nameKey: 'name',
            fetchFn: (id) => queryRequest.fetch('world.location', { worldId: id })
        });
    }

    /**
     * @param input
     */
    function resolveGroupRef(input) {
        return resolveRef(input, {
            emptyDefault: { id: '', name: '' },
            idAlias: 'groupId',
            nameKey: 'name',
            fetchFn: (id) => queryRequest.fetch('group.dialog', { groupId: id })
        });
    }

    /**
     * @param accessTypeNameRaw
     */
    function translateAccessType(accessTypeNameRaw) {
        const key = accessTypeLocaleKeyMap[accessTypeNameRaw];
        if (!key) {
            return accessTypeNameRaw;
        }
        if (accessTypeNameRaw === 'groupPublic' || accessTypeNameRaw === 'groupPlus') {
            const groupKey = accessTypeLocaleKeyMap.group;
            return `${t(groupKey)} ${t(key)}`;
        }
        return t(key);
    }

    /**
     * @param instanceId
     * @param worldNameOverride
     */
    function formatPreviousInstancesInfoLabel(instanceId, worldNameOverride = '') {
        const location = parseLocation(instanceId);
        const worldId = location.worldId;
        const worldName = worldNameOverride || (worldId ? worldStore.cachedWorlds.get(worldId)?.name : '') || '';
        const baseLabel = worldName || worldId || instanceId || '';
        const accessTypeLabel = translateAccessType(location.accessTypeName || '');
        if (!accessTypeLabel || !location.instanceId) {
            return baseLabel;
        }
        return `${baseLabel} · ${accessTypeLabel}`;
    }

    /**
     * @param instanceId
     */
    function showPreviousInstancesInfoDialog(instanceId) {
        previousInstancesInfoDialog.value.visible = true;
        previousInstancesInfoDialog.value.instanceId = instanceId;
        uiStore.openDialog({
            type: 'previous-instances-info',
            id: instanceId || '',
            label: instanceId ? formatPreviousInstancesInfoLabel(instanceId) : ''
        });
        if (instanceId) {
            const location = parseLocation(instanceId);
            if (location.worldId && !worldStore.cachedWorlds.get(location.worldId)?.name) {
                queryRequest
                    .fetch('world.dialog', { worldId: location.worldId })
                    .then((args) => {
                        uiStore.setDialogCrumbLabel(
                            'previous-instances-info',
                            instanceId,
                            formatPreviousInstancesInfoLabel(instanceId, args?.ref?.name || '')
                        );
                    })
                    .catch(() => {});
            }
        }
    }

    /**
     * @param variant
     * @param targetRef
     */
    async function showPreviousInstancesListDialog(variant, targetRef) {
        previousInstancesListDialog.value.variant = variant;
        let resolved = null;
        if (variant === 'user') {
            resolved = await resolveUserRef(targetRef);
            previousInstancesListDialog.value.userRef = resolved;
        } else if (variant === 'world') {
            resolved = await resolveWorldRef(targetRef);
            previousInstancesListDialog.value.worldRef = resolved;
        } else {
            resolved = await resolveGroupRef(targetRef);
            previousInstancesListDialog.value.groupRef = resolved;
        }
        previousInstancesListDialog.value.visible = true;
        const dialogId = resolved?.id || '';
        const label = resolved?.id
            ? variant === 'user'
                ? resolved.displayName || resolved.id
                : resolved.name || resolved.id
            : '';
        uiStore.openDialog({
            type: `previous-instances-${variant}`,
            id: dialogId,
            label
        });
    }

    function updateCurrentInstanceWorld() {
        let L;
        let instanceId = locationStore.lastLocation.location;
        if (locationStore.lastLocation.location === 'traveling') {
            instanceId = locationStore.lastLocationDestination;
        }
        if (!instanceId) {
            currentInstanceWorld.value = {
                ref: {},
                instance: {},
                isPC: false,
                isQuest: false,
                isIos: false,
                avatarScalingDisabled: false,
                focusViewDisabled: false,
                inCache: false,
                cacheSize: '',
                fileAnalysis: {}
            };
            currentInstanceLocation.value = {};
        } else if (instanceId !== currentInstanceLocation.value.tag) {
            currentInstanceWorld.value = {
                ref: {},
                instance: {},
                isPC: false,
                isQuest: false,
                isIos: false,
                avatarScalingDisabled: false,
                focusViewDisabled: false,
                inCache: false,
                cacheSize: '',
                fileAnalysis: {}
            };
            L = parseLocation(instanceId);
            currentInstanceLocation.value = L;
            worldRequest
                .getWorld({
                    worldId: L.worldId
                })
                .then((args) => {
                    currentInstanceWorld.value.ref = args.ref;
                    const { isPC, isQuest, isIos } = getAvailablePlatforms(args.ref.unityPackages);
                    currentInstanceWorld.value.isPC = isPC;
                    currentInstanceWorld.value.isQuest = isQuest;
                    currentInstanceWorld.value.isIos = isIos;
                    currentInstanceWorld.value.avatarScalingDisabled = args.ref?.tags.includes(
                        'feature_avatar_scaling_disabled'
                    );
                    currentInstanceWorld.value.focusViewDisabled =
                        args.ref?.tags.includes('feature_focus_view_disabled');
                    checkVRChatCache(args.ref)
                        .then((cacheInfo) => {
                            if (cacheInfo.Item1 > 0) {
                                currentInstanceWorld.value.inCache = true;
                                currentInstanceWorld.value.cacheSize = formatFileSize(cacheInfo.Item1);
                            }
                        })
                        .catch((error) => {
                            console.error('Error checking VRChat cache:', error);
                        });
                    getBundleDateSize(args.ref);
                    return args;
                })
                .catch((error) => {
                    console.error('Error fetching world data:', error);
                });
        } else {
            queryRequest
                .fetch('world.location', {
                    worldId: currentInstanceLocation.value.worldId
                })
                .then((args) => {
                    currentInstanceWorld.value.ref = args.ref;
                    const { isPC, isQuest, isIos } = getAvailablePlatforms(args.ref.unityPackages);
                    currentInstanceWorld.value.isPC = isPC;
                    currentInstanceWorld.value.isQuest = isQuest;
                    currentInstanceWorld.value.isIos = isIos;
                    checkVRChatCache(args.ref).then((cacheInfo) => {
                        if (cacheInfo.Item1 > 0) {
                            currentInstanceWorld.value.inCache = true;
                            currentInstanceWorld.value.cacheSize = formatFileSize(cacheInfo.Item1);
                        }
                    });
                });
        }
        if (isRealInstance(instanceId)) {
            const ref = cachedInstances.get(instanceId);
            if (typeof ref !== 'undefined') {
                currentInstanceWorld.value.instance = ref;
            } else {
                L = parseLocation(instanceId);
                if (L.isRealInstance) {
                    instanceRequest
                        .getInstance({
                            worldId: L.worldId,
                            instanceId: L.instanceId
                        })
                        .then((args) => {
                            currentInstanceWorld.value.instance = args.ref;
                        })
                        .catch((error) => {
                            console.error('Error fetching instance data:', error);
                        });
                }
            }
        }
    }

    /**
     * @param {object} json
     * @returns {object} Ref
     */
    function applyInstance(json) {
        if (!json?.id) {
            return null;
        }
        if (!json.$fetchedAt) {
            json.$fetchedAt = new Date().toJSON();
        }
        let ref = cachedInstances.get(json.id);
        if (typeof ref === 'undefined') {
            ref = createDefaultInstanceRef(json);
            cachedInstances.set(ref.id, ref);
            cleanInstanceCache();
        } else {
            Object.assign(ref, json);
        }
        ref.$location = parseLocation(ref.location);
        if (ref.world?.id) {
            queryRequest
                .fetch('world.location', {
                    worldId: ref.world.id
                })
                .then((args) => {
                    ref.world = args.ref;
                    return args;
                });
        }
        if (ref?.world?.name) {
            ref.world.name = replaceBioSymbols(ref.world.name);
        }
        if (ref?.world?.description) {
            ref.world.description = replaceBioSymbols(ref.world.description);
        }
        ref.$disabledContentSettings = computeDisabledContentSettings(json.contentSettings, instanceContentSettings);
        if (ref.displayName) {
            ref.displayName = replaceBioSymbols(ref.displayName);
        }
        if (ref.description) {
            ref.description = replaceBioSymbols(ref.description);
        }
        if (userStore.userDialog.visible && userStore.userDialog.ref?.$location?.tag === ref.id) {
            userStore.applyUserDialogLocation();
        }
        if (worldStore.worldDialog.visible && worldStore.worldDialog.id === ref.worldId) {
            applyWorldDialogInstances();
        }
        if (groupStore.groupDialog.visible && groupStore.groupDialog.id === ref.ownerId) {
            applyGroupDialogInstances();
        }
        for (const groupInstance of groupStore.groupInstances) {
            if (groupInstance.instance.id === ref.id) {
                groupInstance.instance = ref;
            }
        }
        lastInstanceApplied.value = ref.id;
        return ref;
    }

    /**
     * @param location
     */
    async function getInstanceName(location) {
        let instanceName = '';

        const L = parseLocation(location);
        if (L.isRealInstance && L.worldId && L.instanceId) {
            try {
                const args = await instanceRequest.getInstance({
                    worldId: L.worldId,
                    instanceId: L.instanceId
                });
                instanceName = args.ref.displayName;
            } catch (e) {
                console.error('getInstanceName failed location', location, e);
            }
        }

        return instanceName;
    }

    /**
     * @param {string} worldId
     * @param {any} options
     * @returns {Promise<{ json: any; params } | null>}
     */
    async function createNewInstance(worldId = '', options) {
        let D = options;

        if (!D) {
            D = {
                loading: false,
                accessType: await configRepository.getString('instanceDialogAccessType', 'public'),
                region: await configRepository.getString('instanceRegion', 'US West'),
                worldId: worldId,
                groupId: await configRepository.getString('instanceDialogGroupId', ''),
                groupAccessType: await configRepository.getString('instanceDialogGroupAccessType', 'plus'),
                ageGate: await configRepository.getBool('instanceDialogAgeGate', false),
                queueEnabled: await configRepository.getBool('instanceDialogQueueEnabled', true),
                roleIds: [],
                groupRef: {}
            };
        }

        let type = 'public';
        let canRequestInvite = false;
        switch (D.accessType) {
            case 'friends':
                type = 'friends';
                break;
            case 'friends+':
                type = 'hidden';
                break;
            case 'invite':
                type = 'private';
                break;
            case 'invite+':
                type = 'private';
                canRequestInvite = true;
                break;
            case 'group':
                type = 'group';
                break;
        }
        let region = 'us';
        if (D.region === 'US East') {
            region = 'use';
        } else if (D.region === 'Europe') {
            region = 'eu';
        } else if (D.region === 'Japan') {
            region = 'jp';
        }
        const params = {
            type,
            canRequestInvite,
            worldId: D.worldId,
            ownerId: userStore.currentUser.id,
            region
        };
        if (type === 'group') {
            params.groupAccessType = D.groupAccessType;
            params.ownerId = D.groupId;
            params.queueEnabled = D.queueEnabled;
            if (D.groupAccessType === 'members') {
                params.roleIds = D.roleIds;
            }
        }
        if (D.minimumAvatarPerformance) {
            params.minimumAvatarPerformance = D.minimumAvatarPerformance;
        }
        const canAgeGate =
            type === 'group'
                ? hasGroupPermission(D.groupRef, 'group-instance-age-gated-create')
                : userStore.currentUser.ageVerificationStatus === '18+';
        if (D.ageGate && canAgeGate) {
            params.ageGate = true;
        }
        if (D.displayName) {
            params.displayName = D.displayName;
        }
        if (D.description) {
            params.description = D.description;
        }
        if (D.languages?.length) {
            params.languages = D.languages;
        }
        if (D.categoryId) {
            params.categoryId = D.categoryId;
        }
        if (D.vibeIds?.length) {
            params.vibeIds = D.vibeIds;
        }
        try {
            const args = await instanceRequest.createInstance(params);
            return args;
        } catch (err) {
            console.error(err);
            return null;
        }
    }

    const debouncedApplyWorldDialogInstances = debounce(applyWorldDialogInstancesDebounced, 100);

    function applyWorldDialogInstances() {
        debouncedApplyWorldDialogInstances();
    }

    function applyWorldDialogInstancesDebounced() {
        const D = worldStore.worldDialog;
        if (!D.visible) {
            return;
        }
        const instances = {};
        if (D.ref.slimInstances) {
            for (const { instanceId, n_users } of D.ref.slimInstances) {
                instances[instanceId] = createInstanceRoom(instanceId, `${D.id}:${instanceId}`, {
                    ref: {
                        userCount: n_users,
                        capacity: D.ref.capacity
                    }
                });
            }
        }
        const { instanceId, shortName } = D?.$location || {};
        if (instanceId && typeof instances[instanceId] === 'undefined') {
            instances[instanceId] = createInstanceRoom(instanceId, `${D.id}:${instanceId}`, { shortName });
        }
        const cachedCurrentUser = userStore.cachedUsers.get(userStore.currentUser.id);
        const lastLocation$ = cachedCurrentUser?.$location;
        const rooms = buildInstanceRooms({
            rooms: instances,
            matches: (L) => L.worldId === D.id,
            keyOf: (L) => L.instanceId,
            tagOf: (L) => `${D.id}:${L.instanceId}`,
            currentUser: cachedCurrentUser,
            lastLocation: locationStore.lastLocation,
            friends: friendStore.friends.values(),
            cachedUsers: userStore.cachedUsers,
            sortAlphabetical: appearanceSettingsStore.instanceUsersSortAlphabetical
        });
        for (const instance of rooms) {
            // due to references on callback of API.getUser()
            // this should be block scope variable
            const L = parseLocation(`${D.id}:${instance.id}`);
            instance.location = L.tag;
            if (!L.shortName) {
                L.shortName = instance.shortName;
            }
            instance.$location = L;
            L.user = {};
            const cachedOwnerId = cachedInstances.get(L.tag)?.ownerId;
            if (!L.userId && cachedOwnerId && !cachedOwnerId.startsWith('grp_')) {
                L.userId = cachedOwnerId;
            }
            if (L.userId) {
                const ref = userStore.cachedUsers.get(L.userId);
                if (typeof ref === 'undefined') {
                    userRequest
                        .getUser({
                            userId: L.userId
                        })
                        .then((args) => {
                            if (args.ref.id === L.userId) {
                                Object.assign(L.user, args.ref);
                                instance.$location = L;
                                applyWorldDialogInstances();
                            }
                        });
                } else {
                    L.user = ref;
                }
            }
        }
        // get instance from cache
        for (const room of rooms) {
            const ref = cachedInstances.get(room.tag);
            if (typeof ref !== 'undefined') {
                Object.assign(room.ref, ref);
            }
        }
        rooms.sort(compareWorldDialogRooms(D.$location.tag, lastLocation$?.tag));
        D.rooms = rooms;
    }

    /**
     * @param {object} inputInstances
     */
    function applyGroupDialogInstances(inputInstances) {
        const D = groupStore.groupDialog;
        if (!D.visible) {
            return;
        }
        const instances = {};
        for (const instance of D.instances) {
            instances[instance.tag] = {
                ...instance,
                friendCount: 0,
                users: []
            };
        }
        if (typeof inputInstances !== 'undefined') {
            for (const instance of inputInstances) {
                instances[instance.location] = createInstanceRoom(instance.instanceId, instance.location, {
                    shortName: instance.shortName,
                    ref: instance
                });
            }
        }
        const cachedCurrentUser = userStore.cachedUsers.get(userStore.currentUser.id);
        const currentLocation = cachedCurrentUser?.$location?.tag;
        const rooms = buildInstanceRooms({
            rooms: instances,
            matches: (L) => L.groupId === D.id,
            keyOf: (L) => L.tag,
            tagOf: (L) => L.tag,
            currentUser: cachedCurrentUser,
            lastLocation: locationStore.lastLocation,
            friends: friendStore.friends.values(),
            cachedUsers: userStore.cachedUsers,
            sortAlphabetical: appearanceSettingsStore.instanceUsersSortAlphabetical
        });
        for (const instance of rooms) {
            instance.location = instance.tag;
            instance.$location = parseLocation(instance.tag);
        }
        // get instance
        for (const room of rooms) {
            const ref = cachedInstances.get(room.tag);
            if (typeof ref !== 'undefined') {
                Object.assign(room.ref, ref);
            } else if (isRealInstance(room.tag)) {
                instanceRequest.getInstance({
                    worldId: room.$location.worldId,
                    instanceId: room.$location.instanceId
                });
            }
        }
        rooms.sort(compareGroupDialogRooms(currentLocation));
        D.instances = rooms;
    }

    function removeAllQueuedInstances() {
        queuedInstances.forEach((ref) => {
            toast.info(`Removed instance ${ref.$worldName} from queue`);
            toast.dismiss(ref.$msgBox);
        });
        queuedInstances.clear();
    }

    /**
     * @param {string} instanceId
     */
    function removeQueuedInstance(instanceId) {
        const ref = queuedInstances.get(instanceId);
        if (typeof ref !== 'undefined') {
            toast.dismiss(ref.$msgBox);
            queuedInstances.delete(instanceId);
        }
    }

    /**
     * @param {string} instanceId
     */
    function applyQueuedInstance(instanceId) {
        queuedInstances.forEach((ref) => {
            if (ref.location !== instanceId) {
                toast.info(
                    t('message.instance.removed_form_queue', {
                        worldName: ref.$worldName
                    })
                );
                toast.dismiss(ref.$msgBox);
                queuedInstances.delete(ref.location);
            }
        });
        if (!instanceId) {
            return;
        }
        if (!queuedInstances.has(instanceId)) {
            const L = parseLocation(instanceId);
            if (L.isRealInstance) {
                instanceRequest
                    .getInstance({
                        worldId: L.worldId,
                        instanceId: L.instanceId
                    })
                    .then((args) => {
                        if (args.json?.queueSize) {
                            instanceQueueUpdate(instanceId, args.json?.queueSize, args.json?.queueSize);
                        }
                    })
                    .catch((error) => {
                        console.error('Error fetching instance data for queue:', error);
                    });
            }
            instanceQueueUpdate(instanceId, 0, 0);
        }
    }

    /**
     * @param {string} instanceId
     */
    function instanceQueueReady(instanceId) {
        const ref = queuedInstances.get(instanceId);
        if (typeof ref !== 'undefined') {
            toast.dismiss(ref.$msgBox);
            queuedInstances.delete(instanceId);
        }
        const L = parseLocation(instanceId);
        const group = groupStore.cachedGroups.get(L.groupId);
        const groupName = group?.name ?? '';
        const worldName = ref?.$worldName ?? '';
        const location = displayLocation(instanceId, worldName, groupName);
        toast.success(`Instance ready to join ${location}`);
        const noty = {
            created_at: new Date().toJSON(),
            type: 'group.queueReady',
            imageUrl: group?.iconUrl,
            message: `Instance ready to join ${location}`,
            location: instanceId,
            senderUserId: L.groupId,
            groupName,
            worldName
        };
        if (
            notificationStore.notificationTable.filters[0].value.length === 0 ||
            notificationStore.notificationTable.filters[0].value.includes(noty.type)
        ) {
            uiStore.notifyMenu('notification');
        }
        notificationStore.queueNotificationNoty(noty);
        sharedFeedStore.addEntry(noty);
        notificationStore.notificationTable.data.push(noty);
    }

    /**
     * @param {string} instanceId
     * @param {number} position
     * @param {number} queueSize
     * @returns {Promise<void>}
     */
    async function instanceQueueUpdate(instanceId, position, queueSize) {
        let ref = queuedInstances.get(instanceId);
        if (typeof ref === 'undefined') {
            ref = {
                $msgBox: null,
                $groupName: '',
                $worldName: '',
                location: instanceId,
                position: 0,
                queueSize: 0,
                updatedAt: 0
            };
        }
        ref.position = position;
        ref.queueSize = queueSize;
        ref.updatedAt = Date.now();
        if (!ref.$groupName) {
            ref.$groupName = await getGroupName(instanceId);
        }
        if (!ref.$worldName) {
            ref.$worldName = await getWorldName(instanceId);
        }
        const location = displayLocation(instanceId, ref.$worldName, ref.$groupName);
        toast.dismiss(ref.$msgBox ?? undefined);
        ref.$msgBox = toast.info(
            `You are in position ${ref.position} of ${ref.queueSize} in the queue for ${location} `,
            {
                duration: Infinity,
                position: 'bottom-right',
                closeButton: true,
                class: 'vrc-instance-queue-message'
            }
        );
        queuedInstances.set(instanceId, ref);
        // workerTimers.setTimeout(this.instanceQueueTimeout, 3600000);
    }

    function getCurrentInstanceUserList() {
        if (!watchState.isFriendsLoaded) {
            return;
        }
        if (state.updatePlayerListTimer) {
            state.updatePlayerListPending = true;
        } else {
            updatePlayerListExecute();
            state.updatePlayerListTimer = setTimeout(() => {
                if (state.updatePlayerListPending) {
                    updatePlayerListExecute();
                }
                state.updatePlayerListTimer = null;
            }, 150);
        }
    }

    function updatePlayerListExecute() {
        try {
            updatePlayerListDebounce();
        } catch (err) {
            console.error(err);
        }
        state.updatePlayerListTimer = null;
        state.updatePlayerListPending = false;
    }

    function updatePlayerListDebounce() {
        const users = [];
        const pushUser = function (ref, profileRef) {
            let photonId = -1;
            let isFriend = false;
            let isBlocked = false;
            let isMuted = false;
            let isAvatarInteractionDisabled = false;
            let isChatBoxMuted = false;
            let ageVerified = false;
            photonStore.photonLobbyCurrent.forEach((ref1, id) => {
                if (typeof ref1 !== 'undefined') {
                    if (
                        (typeof ref.id !== 'undefined' && typeof ref1.id !== 'undefined' && ref1.id === ref.id) ||
                        (typeof ref.displayName !== 'undefined' &&
                            typeof ref1.displayName !== 'undefined' &&
                            ref1.displayName === ref.displayName)
                    ) {
                        photonId = id;
                    }
                }
            });
            let isMaster = false;
            if (photonStore.photonLobbyMaster !== 0 && photonId === photonStore.photonLobbyMaster) {
                isMaster = true;
            }
            let isModerator = false;
            const lobbyJointime = photonStore.photonLobbyJointime.get(photonId);
            let inVRMode = null;
            let groupOnNameplate = '';
            if (typeof lobbyJointime !== 'undefined') {
                inVRMode = lobbyJointime.inVRMode;
                groupOnNameplate = lobbyJointime.groupOnNameplate;
                isModerator = lobbyJointime.canModerateInstance;
            }
            // if (groupOnNameplate) {
            //     API.getCachedGroup({
            //         groupId: groupOnNameplate
            //     }).then((args) => {
            //         groupOnNameplate = args.ref.name;
            //     });
            // }
            let timeoutTime = 0;
            if (typeof ref.id !== 'undefined') {
                isFriend = ref.isFriend;
                if (
                    photonStore.timeoutHudOverlayFilter === 'VIP' ||
                    photonStore.timeoutHudOverlayFilter === 'Friends'
                ) {
                    photonStore.photonLobbyTimeout.forEach((ref1) => {
                        if (ref1.userId === ref.id) {
                            timeoutTime = ref1.time;
                        }
                    });
                } else {
                    photonStore.photonLobbyTimeout.forEach((ref1) => {
                        if (ref1.displayName === ref.displayName) {
                            timeoutTime = ref1.time;
                        }
                    });
                }
                isBlocked = ref.$moderations.isBlocked;
                isMuted = ref.$moderations.isMuted;
                isAvatarInteractionDisabled = ref.$moderations.isAvatarInteractionDisabled;
                isChatBoxMuted = ref.$moderations.isChatBoxMuted;
                ageVerified = ref.ageVerificationStatus === '18+';
            }
            users.push({
                ref,
                profileRef,
                displayName: ref.displayName,
                timer: ref.$location_at,
                $trustSortNum: ref.$trustSortNum ?? 0,
                photonId,
                isMaster,
                isModerator,
                inVRMode,
                groupOnNameplate,
                isFriend,
                timeoutTime,
                isBlocked,
                isMuted,
                isAvatarInteractionDisabled,
                isChatBoxMuted,
                ageVerified
            });
        };

        const playersInInstance = locationStore.lastLocation.playerList;
        if (playersInInstance.size > 0) {
            let ref = userStore.cachedUsers.get(userStore.currentUser.id);
            let profileRef = userStore.cachedProfiles.get(userStore.currentUser.id);
            if (typeof ref !== 'undefined' && playersInInstance.has(ref.id)) {
                pushUser(ref, profileRef);
            }
            for (const player of playersInInstance.values()) {
                // if friend isn't in instance add them
                if (player.displayName === userStore.currentUser.displayName) {
                    continue;
                }
                const addUser = !users.some(function (user) {
                    return player.displayName === user.displayName;
                });
                if (addUser) {
                    ref = userStore.cachedUsers.get(player.userId);
                    profileRef = userStore.cachedProfiles.get(player.userId);
                    if (typeof ref !== 'undefined') {
                        pushUser(ref, profileRef);
                    } else {
                        let { joinTime } = locationStore.lastLocation.playerList.get(player.userId);
                        if (!joinTime) {
                            joinTime = Date.now();
                        }
                        ref = {
                            // if userId is missing just push displayName
                            displayName: player.displayName,
                            $location_at: joinTime,
                            $online_for: joinTime
                        };
                        pushUser(ref, profileRef);
                    }
                }
            }
        }
        currentInstanceUsersData.value = users;
    }

    // $app.methods.instanceQueueClear = function () {
    //     // remove all instances from queue
    //     queuedInstances.forEach((ref) => {
    //         toast.dismiss(ref.$msgBox);
    //         queuedInstances.delete(ref.location);
    //     });
    // };

    return {
        state,

        cachedInstances,
        lastInstanceApplied,
        currentInstanceWorld,
        currentInstanceLocation,
        queuedInstances,
        previousInstancesInfoDialog,
        previousInstancesInfoState,
        previousInstancesListDialog,
        previousInstancesListState,
        instanceJoinHistory,
        currentInstanceUsersData,
        instanceCategories,
        instanceVibes,

        applyInstance,
        loadInstanceCategoriesAndVibes,
        updateCurrentInstanceWorld,
        createNewInstance,
        applyWorldDialogInstances,
        applyGroupDialogInstances,
        removeAllQueuedInstances,
        removeQueuedInstance,
        applyQueuedInstance,
        instanceQueueReady,
        instanceQueueUpdate,
        hidePreviousInstancesDialogs,
        setPreviousInstancesInfoDialogVisible,
        setPreviousInstancesListDialogVisible,
        showPreviousInstancesInfoDialog,
        showPreviousInstancesListDialog,
        addInstanceJoinHistory,
        getCurrentInstanceUserList,
        getInstanceJoinHistory,
        getInstanceName
    };
});
