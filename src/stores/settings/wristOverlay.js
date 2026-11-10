import { defineStore } from 'pinia';
import { ref } from 'vue';

import { definePersistedSettings } from './persistedSettings';
import { useSharedFeedStore } from '../sharedFeed';

import configRepository from '../../services/config';

export const useWristOverlaySettingsStore = defineStore('WristOverlaySettings', () => {
    const sharedFeed = useSharedFeedStore();

    const settings = definePersistedSettings({
        overlayWrist: ['VRCX_overlayWrist', 'bool', false, { initial: true }],
        hidePrivateFromFeed: ['VRCX_hidePrivateFromFeed', 'bool', false, { setter: false }],
        openVRAlways: ['openVRAlways', 'bool', false],
        overlaybutton: ['VRCX_overlaybutton', 'bool', false],
        vrBackgroundEnabled: ['VRCX_vrBackgroundEnabled', 'bool', false],
        minimalFeed: ['VRCX_minimalFeed', 'bool', true],
        hideDevicesFromFeed: ['VRCX_hideDevicesFromFeed', 'bool', false],
        vrOverlayCpuUsage: ['VRCX_vrOverlayCpuUsage', 'bool', false],
        hideUptimeFromFeed: ['VRCX_hideUptimeFromFeed', 'bool', false],
        pcUptimeOnFeed: ['VRCX_pcUptimeOnFeed', 'bool', false]
    });
    const { hidePrivateFromFeed } = settings.refs;

    const overlayHand = ref('0');

    async function initWristOverlaySettings() {
        const [, overlayHandConfig] = await Promise.all([
            settings.load(),
            configRepository.getInt('VRCX_overlayHand', 0)
        ]);

        overlayHand.value = String(overlayHandConfig);
    }

    function setHidePrivateFromFeed() {
        hidePrivateFromFeed.value = !hidePrivateFromFeed.value;
        configRepository.setBool('VRCX_hidePrivateFromFeed', hidePrivateFromFeed.value);
        sharedFeed.loadSharedFeed();
    }
    /**
     * @param {string} value
     */
    function setOverlayHand(value) {
        overlayHand.value = value;
        let overlayHandInt = parseInt(value, 10);
        if (isNaN(overlayHandInt)) {
            overlayHandInt = 0;
        }
        configRepository.setInt('VRCX_overlayHand', overlayHandInt);
    }

    initWristOverlaySettings();

    return {
        ...settings.refs,
        overlayHand,

        ...settings.setters,
        setHidePrivateFromFeed,
        setOverlayHand
    };
});
