import { reactive, ref, watch } from 'vue';
import { defineStore } from 'pinia';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { logWebRequest } from '../../services/appConfig';
import { database } from '../../services/database';
import { languageCodes } from '../../localization';
import { useGameStore } from '../game';
import { useModalStore } from '../modal';
import { useUpdateLoopStore } from '../updateLoop';
import { useVRCXUpdaterStore } from '../vrcxUpdater';
import { useVrcxStore } from '../vrcx';
import { watchState } from '../../services/watchState';
import { definePersistedSettings } from './persistedSettings';

import configRepository from '../../services/config';
import webApiService from '../../services/webapi';

export const useAdvancedSettingsStore = defineStore('AdvancedSettings', () => {
    const gameStore = useGameStore();
    const vrcxStore = useVrcxStore();
    const VRCXUpdaterStore = useVRCXUpdaterStore();
    const modalStore = useModalStore();
    const updateLoopStore = useUpdateLoopStore();

    const { t } = useI18n();

    const state = reactive({
        folderSelectorDialogVisible: false
    });

    const settings = definePersistedSettings({
        enablePrimaryPassword: ['enablePrimaryPassword', 'bool', false, { setter: false }],
        relaunchVRChatAfterCrash: ['VRCX_relaunchVRChatAfterCrash', 'bool', false],
        vrcQuitFix: ['VRCX_vrcQuitFix', 'bool', true],
        autoSweepVRChatCache: ['VRCX_autoSweepVRChatCache', 'bool', false],
        selfInviteOverride: ['VRCX_selfInviteOverride', 'bool', false],
        saveInstancePrints: ['VRCX_saveInstancePrints', 'bool', false],
        cropInstancePrints: ['VRCX_cropInstancePrints', 'bool', false, { setter: false }],
        saveInstanceStickers: ['VRCX_saveInstanceStickers', 'bool', false],
        avatarRemoteDatabase: ['VRCX_avatarRemoteDatabase', 'bool', true, { setter: false }],
        enableAppLauncher: ['VRCX_enableAppLauncher', 'bool', true, { setter: false }],
        enableAppLauncherAutoClose: ['VRCX_enableAppLauncherAutoClose', 'bool', true, { setter: false }],
        enableAppLauncherRunProcessOnce: ['VRCX_enableAppLauncherRunProcessOnce', 'bool', true, { setter: false }],
        screenshotHelper: ['VRCX_screenshotHelper', 'bool', true],
        screenshotHelperModifyFilename: ['VRCX_screenshotHelperModifyFilename', 'bool', false],
        screenshotHelperCopyToClipboard: ['VRCX_screenshotHelperCopyToClipboard', 'bool', false],
        youTubeApi: ['VRCX_youtubeAPI', 'bool', false],
        youTubeApiKey: ['VRCX_youtubeAPIKey', 'string', ''],
        translationApi: ['VRCX_translationAPI', 'bool', false],
        translationApiKey: ['VRCX_translationAPIKey', 'string', ''],
        // 'google' | 'openai'
        translationApiType: ['VRCX_translationAPIType', 'string', 'google', { setter: false }],
        translationApiEndpoint: [
            'VRCX_translationAPIEndpoint',
            'string',
            '',
            { initial: 'https://api.openai.com/v1/chat/completions' }
        ],
        translationApiModel: ['VRCX_translationAPIModel', 'string', '', { initial: 'gpt-4o-mini' }],
        translationApiPrompt: ['VRCX_translationAPIPrompt', 'string', ''],
        progressPie: ['VRCX_progressPie', 'bool', false],
        progressPieFilter: ['VRCX_progressPieFilter', 'bool', true],
        showConfirmationOnSwitchAvatar: ['VRCX_showConfirmationOnSwitchAvatar', 'bool', false],
        gameLogDisabled: ['VRCX_gameLogDisabled', 'bool', false],
        avatarAutoCleanup: ['VRCX_avatarAutoCleanup', 'string', 'Off'],
        ugcFolderPath: ['VRCX_userGeneratedContentPath', 'string', '', { setter: false }],
        autoDeleteOldPrints: ['VRCX_autoDeleteOldPrints', 'bool', false],
        notificationOpacity: ['VRCX_notificationOpacity', 'float', 100, { setter: false }],
        saveInstanceEmoji: ['VRCX_saveInstanceEmoji', 'bool', false],
        vrcRegistryAutoBackup: ['VRCX_vrcRegistryAutoBackup', 'bool', true],
        vrcRegistryAskRestore: ['VRCX_vrcRegistryAskRestore', 'bool', true]
    });
    const {
        enablePrimaryPassword,
        cropInstancePrints,
        avatarRemoteDatabase,
        enableAppLauncher,
        enableAppLauncherAutoClose,
        enableAppLauncherRunProcessOnce,
        youTubeApiKey,
        translationApi,
        translationApiKey,
        translationApiType,
        translationApiEndpoint,
        translationApiModel,
        translationApiPrompt,
        ugcFolderPath,
        notificationOpacity
    } = settings.refs;

    const bioLanguage = ref('en');
    const sqliteTableSizes = ref({
        gps: 0,
        status: 0,
        bio: 0,
        avatar: 0,
        onlineOffline: 0,
        friendLogHistory: 0,
        notification: 0,
        location: 0,
        joinLeave: 0,
        portalSpawn: 0,
        videoPlay: 0,
        event: 0,
        external: 0
    });
    const purgeInProgress = ref(false);
    const currentUserInventory = reactive(new Map());
    const isVRChatConfigDialogVisible = ref(false);
    const sentryErrorReporting = ref(false);

    watch(
        () => watchState.isLoggedIn,
        () => {
            currentUserInventory.clear();
            isVRChatConfigDialogVisible.value = false;
        },
        { flush: 'sync' }
    );

    async function initAdvancedSettings() {
        const [, bioLanguageConfig, sentryErrorReportingConfig] = await Promise.all([
            settings.load(),
            configRepository.getString('VRCX_bioLanguage'),
            configRepository.getString('VRCX_SentryEnabled', '')
        ]);

        if (!bioLanguageConfig || !languageCodes.includes(bioLanguageConfig)) {
            bioLanguage.value = 'en';
        } else {
            bioLanguage.value = bioLanguageConfig;
        }

        sentryErrorReporting.value = sentryErrorReportingConfig === 'true';

        handleSetAppLauncherSettings();

        setTimeout(() => {
            if (VRCXUpdaterStore.branch === 'Nightly' && sentryErrorReportingConfig === '') {
                checkSentryConsent();
            }
        }, 2000);
    }

    initAdvancedSettings();

    /**
     * @param {boolean} value
     */
    function setEnablePrimaryPasswordConfigRepository(value) {
        configRepository.setBool('enablePrimaryPassword', value);
    }

    /**
     * @param {boolean} value
     */
    function setEnablePrimaryPassword(value) {
        enablePrimaryPassword.value = value;
    }
    function setCropInstancePrints() {
        cropInstancePrints.value = !cropInstancePrints.value;
        configRepository.setBool('VRCX_cropInstancePrints', cropInstancePrints.value);
        cropPrintsChanged();
    }
    /**
     * @param {boolean} value
     */
    function setAvatarRemoteDatabase(value) {
        avatarRemoteDatabase.value = value;
        configRepository.setBool('VRCX_avatarRemoteDatabase', avatarRemoteDatabase.value);
    }
    async function setEnableAppLauncher() {
        enableAppLauncher.value = !enableAppLauncher.value;
        await configRepository.setBool('VRCX_enableAppLauncher', enableAppLauncher.value);
        handleSetAppLauncherSettings();
    }
    async function setEnableAppLauncherAutoClose() {
        enableAppLauncherAutoClose.value = !enableAppLauncherAutoClose.value;
        await configRepository.setBool('VRCX_enableAppLauncherAutoClose', enableAppLauncherAutoClose.value);
        handleSetAppLauncherSettings();
    }
    async function setEnableAppLauncherRunProcessOnce() {
        enableAppLauncherRunProcessOnce.value = !enableAppLauncherRunProcessOnce.value;
        await configRepository.setBool('VRCX_enableAppLauncherRunProcessOnce', enableAppLauncherRunProcessOnce.value);
        handleSetAppLauncherSettings();
    }
    async function setTranslationApiType(value) {
        translationApiType.value = value || 'google';
        await configRepository.setString('VRCX_translationAPIType', translationApiType.value);
    }

    async function fetchAvailableModels(overrides = {}) {
        const baseURL = overrides.endpoint || translationApiEndpoint.value;

        if (!baseURL) {
            toast.warning('Translation endpoint not configured');
            return [];
        }

        let modelsURL = '';
        try {
            const url = new URL(baseURL);
            const basePath = url.pathname.replace(/\/+$/, '');

            if (basePath.endsWith('/chat/completions')) {
                url.pathname = basePath.replace(/\/chat\/completions$/, '/models');
            } else if (basePath.endsWith('/models')) {
                url.pathname = basePath;
            } else {
                url.pathname = `${basePath}/models`;
            }

            url.search = '';
            url.hash = '';
            modelsURL = url.toString();
        } catch {
            const normalizedBaseURL = baseURL.endsWith('/') ? baseURL.slice(0, -1) : baseURL;

            if (normalizedBaseURL.includes('/chat/completions')) {
                modelsURL = normalizedBaseURL.replace(/\/chat\/completions$/, '/models');
            } else if (normalizedBaseURL.endsWith('/models')) {
                modelsURL = normalizedBaseURL;
            } else {
                modelsURL = `${normalizedBaseURL}/models`;
            }
        }

        /** @type {Record<string, string>} */
        const headers = {};
        const keyToUse = overrides.key ?? translationApiKey.value;
        if (keyToUse) {
            headers.Authorization = `Bearer ${keyToUse}`;
        }

        try {
            const response = await webApiService.execute({
                url: modelsURL,
                method: 'GET',
                headers
            });

            if (response.status !== 200) {
                throw new Error(`Failed to fetch models: ${response.status} - ${response.data}`);
            }

            const data = JSON.parse(response.data);
            logWebRequest('[EXTERNAL GET]', modelsURL, `(${response.status})`, data);

            if (data.data && Array.isArray(data.data)) {
                return data.data
                    .map((model) => model.id)
                    .filter((id) => id && typeof id === 'string')
                    .sort();
            }

            if (Array.isArray(data)) {
                return data
                    .map((model) => model.id || model.name)
                    .filter((id) => id && typeof id === 'string')
                    .sort();
            }

            throw new Error('Unexpected API response format');
        } catch (error) {
            console.error('Failed to fetch models:', error);
            toast.error(`Failed to fetch models: ${error.message}`);
            return [];
        }
    }

    function setBioLanguage(language) {
        bioLanguage.value = language;
        configRepository.setString('VRCX_bioLanguage', language);
    }

    /**
     * @param {number | null} days - Number of days to keep. Null means delete all.
     */
    async function purgeAvatarFeedData(days) {
        let cutoffDate = null;
        if (days !== null) {
            const cutoff = new Date();
            cutoff.setDate(cutoff.getDate() - days);
            cutoffDate = cutoff.toJSON();
        }

        purgeInProgress.value = true;
        const msgBox = toast.warning(t('view.settings.advanced.advanced.database_cleanup.purge_in_progress'), {
            duration: Infinity
        });

        try {
            await database.purgeAvatarFeedData(cutoffDate);
            await database.vacuum();
            toast.dismiss(msgBox);
            toast.success(t('view.settings.advanced.advanced.database_cleanup.purge_complete'));
            // Brief delay before restart to show success message
            await new Promise((resolve) => setTimeout(resolve, 1500));
            VRCXUpdaterStore.restartVRCX(false);
        } catch (err) {
            console.error(err);
            toast.dismiss(msgBox);
            toast.error(t('view.settings.advanced.advanced.database_cleanup.purge_failed', { error: err }));
        } finally {
            purgeInProgress.value = false;
        }
    }

    /**
     * Run auto-cleanup on startup if configured and enough time has passed.
     * Reads config directly from configRepository to avoid race condition
     * with initAdvancedSettings not having completed yet.
     *
     * @param {string} userId - Current user ID for per-user cleanup tracking.
     */
    async function runAvatarAutoCleanup(userId) {
        const cleanupSetting = await configRepository.getString('VRCX_avatarAutoCleanup', 'Off');
        if (cleanupSetting === 'Off') return;

        const configKey = `VRCX_lastAvatarCleanupDate_${userId}`;
        const lastCleanupStr = await configRepository.getString(configKey, '');
        const now = new Date();

        if (lastCleanupStr) {
            const lastCleanup = new Date(lastCleanupStr);
            const daysSinceLastCleanup = (now.getTime() - lastCleanup.getTime()) / (1000 * 60 * 60 * 24);
            if (daysSinceLastCleanup < 7) return;
        }

        const days = parseInt(cleanupSetting, 10);
        if (isNaN(days) || days <= 0) return;

        const cutoff = new Date();
        cutoff.setDate(cutoff.getDate() - days);
        const cutoffDate = cutoff.toJSON();

        try {
            await database.purgeAvatarFeedData(cutoffDate);
            await configRepository.setString(configKey, now.toJSON());
            console.log(`Auto-cleaned avatar feed data older than ${days} days`);
        } catch (err) {
            console.error('Avatar auto-cleanup failed:', err);
        }
    }

    async function setUGCFolderPath(path) {
        if (typeof path !== 'string') {
            path = '';
        }
        ugcFolderPath.value = path;
        await configRepository.setString('VRCX_userGeneratedContentPath', path);
    }

    async function setNotificationOpacity(value) {
        notificationOpacity.value = value;
        await configRepository.setInt('VRCX_notificationOpacity', value);
    }

    async function checkSentryConsent() {
        modalStore
            .confirm({
                description: t('view.settings.advanced.advanced.anonymous_error_reporting.consent_description'),
                title: t('view.settings.advanced.advanced.anonymous_error_reporting.consent_title')
            })
            .then(async ({ ok }) => {
                if (!ok) return;
                modalStore
                    .confirm({
                        description: t(
                            'view.settings.advanced.advanced.anonymous_error_reporting.enabled_restart_description'
                        ),
                        title: t('confirm.restart_required_title'),
                        confirmText: t('confirm.restart_now'),
                        cancelText: t('confirm.restart_later')
                    })
                    .then(async ({ ok }) => {
                        if (!ok) return;

                        sentryErrorReporting.value = true;
                        configRepository.setBool('VRCX_SentryEnabled', true);

                        VRCXUpdaterStore.restartVRCX(false);
                    });
            });
    }

    async function setSentryErrorReporting() {
        if (VRCXUpdaterStore.branch !== 'Nightly') return;

        modalStore
            .confirm({
                description: t(
                    'view.settings.advanced.advanced.anonymous_error_reporting.disabled_restart_description'
                ),
                title: t('confirm.restart_required_title'),
                confirmText: t('confirm.restart_now'),
                cancelText: t('confirm.restart_later')
            })
            .then(async ({ ok }) => {
                if (!ok) return;

                sentryErrorReporting.value = !sentryErrorReporting.value;
                await configRepository.setBool('VRCX_SentryEnabled', sentryErrorReporting.value);
                VRCXUpdaterStore.restartVRCX(false);
            });
    }

    async function getSqliteTableSizes() {
        const [
            gps,
            status,
            bio,
            avatar,
            onlineOffline,
            friendLogHistory,
            notification,
            location,
            joinLeave,
            portalSpawn,
            videoPlay,
            event,
            external
        ] = await Promise.all([
            database.getGpsTableSize(),
            database.getStatusTableSize(),
            database.getBioTableSize(),
            database.getAvatarTableSize(),
            database.getOnlineOfflineTableSize(),
            database.getFriendLogHistoryTableSize(),
            database.getNotificationTableSize(),
            database.getLocationTableSize(),
            database.getJoinLeaveTableSize(),
            database.getPortalSpawnTableSize(),
            database.getVideoPlayTableSize(),
            database.getEventTableSize(),
            database.getExternalTableSize()
        ]);

        sqliteTableSizes.value = {
            gps,
            status,
            bio,
            avatar,
            onlineOffline,
            friendLogHistory,
            notification,
            location,
            joinLeave,
            portalSpawn,
            videoPlay,
            event,
            external
        };
    }

    function handleSetAppLauncherSettings() {
        AppApi.SetAppLauncherSettings(
            enableAppLauncher.value,
            enableAppLauncherAutoClose.value,
            enableAppLauncherRunProcessOnce.value
        );
    }

    /**
     * @param {string} videoId
     */
    async function lookupYouTubeVideo(videoId) {
        if (!youTubeApiKey.value) {
            console.warn('no Youtube API key configured');
            return null;
        }
        let data = null;
        let apiKey = '';
        if (youTubeApiKey.value) {
            apiKey = youTubeApiKey.value;
        }
        try {
            const url = `https://www.googleapis.com/youtube/v3/videos?id=${encodeURIComponent(
                videoId
            )}&part=snippet,contentDetails&key=${apiKey}`;
            const response = await webApiService.execute({
                url,
                method: 'GET',
                headers: {
                    Referer: 'https://vrcx.app'
                }
            });
            const json = JSON.parse(response.data);
            logWebRequest('[EXTERNAL GET]', url, `(${response.status})`, json);
            if (response.status === 200) {
                data = json;
            } else {
                throw new Error(`Error: ${response.data}`);
            }
        } catch {
            console.error(`YouTube video lookup failed for ${videoId}`);
        }
        return data;
    }

    async function translateText(text, targetLang, overrides) {
        if (!translationApi.value) {
            toast.warning('Translation API disabled');
            return null;
        }

        const provider = overrides?.type || translationApiType.value || 'google';

        if (provider === 'google') {
            const keyToUse = overrides?.key ?? translationApiKey.value;
            if (!keyToUse) {
                toast.warning('No Translation API key configured');
                return null;
            }
            try {
                const url = `https://translation.googleapis.com/language/translate/v2?key=${keyToUse}`;
                const response = await webApiService.execute({
                    url,
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        Referer: 'https://vrcx.app'
                    },
                    body: JSON.stringify({
                        q: text,
                        target: targetLang,
                        format: 'text'
                    })
                });
                if (response.status !== 200) {
                    throw new Error(`Translation API error: ${response.status} - ${response.data}`);
                }
                const data = JSON.parse(response.data);
                logWebRequest('[EXTERNAL POST]', url, `(${response.status})`, data);
                return data.data.translations[0].translatedText;
            } catch (err) {
                toast.error(`Translation failed: ${err.message}`);
                return null;
            }
        }

        const endpoint =
            overrides?.endpoint || translationApiEndpoint.value || 'https://api.openai.com/v1/chat/completions';
        const model = overrides?.model || translationApiModel.value || 'gpt-5.1';
        const prompt =
            overrides?.prompt ||
            translationApiPrompt.value ||
            `You are a translation assistant. Translate the user message into ${targetLang}. Only return the translated text.`;

        if (!endpoint || !model) {
            toast.warning('Translation endpoint/model missing');
            return null;
        }

        const headers = {
            'Content-Type': 'application/json',
            Referer: 'https://vrcx.app',
            'HTTP-Referer': 'https://vrcx.app',
            'X-Title': 'VRCX'
        };
        const keyToUse = overrides?.key ?? translationApiKey.value;
        if (keyToUse) {
            headers.Authorization = `Bearer ${keyToUse}`;
        }

        try {
            const response = await webApiService.execute({
                url: endpoint,
                method: 'POST',
                headers,
                body: JSON.stringify({
                    model,
                    messages: [
                        {
                            role: 'system',
                            content: prompt
                        },
                        {
                            role: 'user',
                            content: text
                        }
                    ]
                })
            });

            if (response.status !== 200) {
                throw new Error(`Translation API error: ${response.status} - ${response.data}`);
            }

            const data = JSON.parse(response.data);
            logWebRequest('[EXTERNAL POST]', endpoint, `(${response.status})`, data);

            const translated = data?.choices?.[0]?.message?.content;
            return typeof translated === 'string' ? translated.trim() : null;
        } catch {
            toast.error(`Translation failed`);
            return null;
        }
    }

    function cropPrintsChanged() {
        if (!cropInstancePrints.value) return;
        modalStore
            .confirm({
                description: t('view.settings.advanced.advanced.save_instance_prints_to_file.crop_convert_old'),
                title: '',
                confirmText: t('view.settings.advanced.advanced.save_instance_prints_to_file.crop_convert_old_confirm'),
                cancelText: t('view.settings.advanced.advanced.save_instance_prints_to_file.crop_convert_old_cancel')
            })
            .then(async ({ ok }) => {
                if (!ok) return;
                const msgBox = toast.warning('Batch print cropping in progress...', {
                    duration: Infinity,
                    position: 'bottom-right'
                });
                try {
                    await AppApi.CropAllPrints(ugcFolderPath.value);
                    toast.success('Batch print cropping complete');
                } catch (err) {
                    console.error(err);
                    toast.error(`Batch print cropping failed: ${err}`);
                } finally {
                    toast.dismiss(msgBox);
                }
            })
            .catch(() => {});
    }

    function askDeleteAllScreenshotMetadata() {
        modalStore
            .confirm({
                description: t('view.settings.advanced.advanced.delete_all_screenshot_metadata.ask'),
                title: '',
                confirmText: t('view.settings.advanced.advanced.delete_all_screenshot_metadata.confirm_yes'),
                cancelText: t('view.settings.advanced.advanced.delete_all_screenshot_metadata.confirm_no')
            })
            .then(({ ok }) => {
                if (!ok) return;
                deleteAllScreenshotMetadata();
            })
            .catch(() => {});
    }

    function deleteAllScreenshotMetadata() {
        modalStore
            .confirm({
                description: t('view.settings.advanced.advanced.delete_all_screenshot_metadata.confirm'),
                title: '',
                confirmText: t('view.settings.advanced.advanced.save_instance_prints_to_file.crop_convert_old_confirm'),
                cancelText: t('view.settings.advanced.advanced.save_instance_prints_to_file.crop_convert_old_cancel')
            })
            .then(async ({ ok }) => {
                if (!ok) return;
                const msgBox = toast.warning('Batch metadata removal in progress...', {
                    duration: Infinity,
                    position: 'bottom-right'
                });
                try {
                    await AppApi.DeleteAllScreenshotMetadata();
                    toast.success('Batch metadata removal complete');
                } catch (err) {
                    console.error(err);
                    toast.error(`Batch metadata removal failed: ${err}`);
                } finally {
                    toast.dismiss(msgBox);
                }
            })
            .catch(() => {});
    }

    function resetUGCFolder() {
        setUGCFolderPath('');
    }

    async function openUGCFolder() {
        if (LINUX && ugcFolderPath.value == null) {
            resetUGCFolder();
        }
        await AppApi.OpenUGCPhotosFolder(ugcFolderPath.value);
    }

    async function folderSelectorDialog(oldPath) {
        if (state.folderSelectorDialogVisible) return;
        if (!oldPath) {
            oldPath = '';
        }

        state.folderSelectorDialogVisible = true;
        let newFolder = '';
        if (WINDOWS) {
            newFolder = await AppApi.OpenFolderSelectorDialog(oldPath);
        } else {
            newFolder = await window.electron.openDirectoryDialog();
        }

        state.folderSelectorDialogVisible = false;
        return newFolder;
    }

    async function openUGCFolderSelector() {
        const path = await folderSelectorDialog(ugcFolderPath.value);
        await setUGCFolderPath(path);
    }

    async function showVRChatConfig() {
        isVRChatConfigDialogVisible.value = true;
        if (!gameStore.VRChatUsedCacheSize) {
            gameStore.getVRChatCacheSize();
        }
    }

    function promptAutoClearVRCXCacheFrequency() {
        modalStore
            .prompt({
                title: t('prompt.auto_clear_cache.header'),
                description: t('prompt.auto_clear_cache.description'),
                confirmText: t('prompt.auto_clear_cache.ok'),
                cancelText: t('prompt.auto_clear_cache.cancel'),
                inputValue: (vrcxStore.clearVRCXCacheFrequency / 3600 / 2).toString(),
                pattern: /\d+$/,
                errorMessage: t('prompt.auto_clear_cache.input_error')
            })
            .then(async ({ ok, value }) => {
                if (!ok) return;
                if (value && !isNaN(parseInt(value, 10))) {
                    vrcxStore.setClearVRCXCacheFrequency(parseInt(value, 10) * 3600 * 2);
                    updateLoopStore.setNextClearVRCXCacheCheck(vrcxStore.clearVRCXCacheFrequency / 2);
                    await configRepository.setString(
                        'VRCX_clearVRCXCacheFrequency',
                        vrcxStore.clearVRCXCacheFrequency.toString()
                    );
                }
            })
            .catch(() => {});
    }

    return {
        state,
        ...settings.refs,

        bioLanguage,
        sqliteTableSizes,
        purgeInProgress,
        currentUserInventory,
        isVRChatConfigDialogVisible,
        sentryErrorReporting,

        ...settings.setters,
        setEnablePrimaryPassword,
        setEnablePrimaryPasswordConfigRepository,
        setBioLanguage,
        setCropInstancePrints,
        setAvatarRemoteDatabase,
        setEnableAppLauncher,
        setEnableAppLauncherAutoClose,
        setEnableAppLauncherRunProcessOnce,
        setTranslationApiType,
        purgeAvatarFeedData,
        runAvatarAutoCleanup,
        setUGCFolderPath,
        cropPrintsChanged,
        setNotificationOpacity,
        getSqliteTableSizes,
        handleSetAppLauncherSettings,
        lookupYouTubeVideo,
        translateText,
        fetchAvailableModels,
        resetUGCFolder,
        openUGCFolder,
        openUGCFolderSelector,
        folderSelectorDialog,
        showVRChatConfig,
        promptAutoClearVRCXCacheFrequency,
        setSentryErrorReporting,
        checkSentryConsent,
        askDeleteAllScreenshotMetadata
    };
});
