import { beforeEach, describe, expect, test, vi } from 'vitest';
import { isRef } from 'vue';

vi.mock('../../../services/config', () => ({
    default: {
        getBool: vi.fn(),
        setBool: vi.fn(),
        getString: vi.fn(),
        setString: vi.fn(),
        getInt: vi.fn(),
        setInt: vi.fn(),
        getFloat: vi.fn(),
        setFloat: vi.fn()
    }
}));

import configRepository from '../../../services/config';
import { definePersistedSettings } from '../persistedSettings';

function define() {
    return definePersistedSettings({
        hideNicknames: ['VRCX_hideNicknames', 'bool', false],
        overlayWrist: ['VRCX_overlayWrist', 'bool', false, { initial: true }],
        desktopToast: ['VRCX_desktopToast', 'string', 'Never'],
        weekStartsOn: ['VRCX_weekStartsOn', 'int', 1],
        opacity: ['VRCX_opacity', 'float', 0.5],
        groups: ['VRCX_groups', 'json', []],
        isDataTableStriped: ['VRCX_dataTableStriped', 'bool', false, { setter: 'toggleStripedDataTable' }],
        notificationIconDot: ['VRCX_notificationIconDot', 'bool', true, { setter: false }]
    });
}

describe('definePersistedSettings', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        configRepository.getBool.mockImplementation(async (key, defaultValue) => defaultValue);
        configRepository.getString.mockImplementation(async (key, defaultValue) => defaultValue);
        configRepository.getInt.mockImplementation(async (key, defaultValue) => defaultValue);
        configRepository.getFloat.mockImplementation(async (key, defaultValue) => defaultValue);
    });

    test('creates refs holding the initial value, or the default when none is given', () => {
        const { refs } = define();

        expect(Object.keys(refs)).toEqual([
            'hideNicknames',
            'overlayWrist',
            'desktopToast',
            'weekStartsOn',
            'opacity',
            'groups',
            'isDataTableStriped',
            'notificationIconDot'
        ]);
        expect(Object.values(refs).every(isRef)).toBe(true);
        expect(refs.hideNicknames.value).toBe(false);
        expect(refs.overlayWrist.value).toBe(true);
        expect(refs.desktopToast.value).toBe('Never');
        expect(refs.weekStartsOn.value).toBe(1);
        expect(refs.groups.value).toEqual([]);
    });

    test('names setters after the setting unless renamed or disabled', () => {
        const { setters } = define();

        expect(Object.keys(setters)).toEqual([
            'setHideNicknames',
            'setOverlayWrist',
            'setDesktopToast',
            'setWeekStartsOn',
            'setOpacity',
            'setGroups',
            'toggleStripedDataTable'
        ]);
    });

    test('load reads each key with its type and default and assigns the refs', async () => {
        configRepository.getBool.mockImplementation(async (key) => key === 'VRCX_hideNicknames');
        configRepository.getString.mockImplementation(async (key) => (key === 'VRCX_groups' ? '["a","b"]' : 'Always'));
        configRepository.getInt.mockResolvedValue(6);
        configRepository.getFloat.mockResolvedValue(0.25);
        const { refs, load } = define();

        await load();

        expect(configRepository.getBool).toHaveBeenCalledWith('VRCX_hideNicknames', false);
        expect(configRepository.getBool).toHaveBeenCalledWith('VRCX_overlayWrist', false);
        expect(configRepository.getBool).toHaveBeenCalledWith('VRCX_notificationIconDot', true);
        expect(configRepository.getString).toHaveBeenCalledWith('VRCX_desktopToast', 'Never');
        expect(configRepository.getString).toHaveBeenCalledWith('VRCX_groups', '[]');
        expect(configRepository.getInt).toHaveBeenCalledWith('VRCX_weekStartsOn', 1);
        expect(configRepository.getFloat).toHaveBeenCalledWith('VRCX_opacity', 0.5);
        expect(refs.hideNicknames.value).toBe(true);
        expect(refs.overlayWrist.value).toBe(false);
        expect(refs.desktopToast.value).toBe('Always');
        expect(refs.weekStartsOn.value).toBe(6);
        expect(refs.opacity.value).toBe(0.25);
        expect(refs.groups.value).toEqual(['a', 'b']);
    });

    test('json settings parse the stringified default when nothing is stored', async () => {
        const { refs, load } = define();

        await load();

        expect(refs.groups.value).toEqual([]);
    });

    test('bool setters toggle and ignore their arguments', () => {
        const { refs, setters } = define();

        setters.setHideNicknames('ignored');
        expect(refs.hideNicknames.value).toBe(true);
        expect(configRepository.setBool).toHaveBeenLastCalledWith('VRCX_hideNicknames', true);

        setters.setHideNicknames(true);
        expect(refs.hideNicknames.value).toBe(false);
        expect(configRepository.setBool).toHaveBeenLastCalledWith('VRCX_hideNicknames', false);

        setters.toggleStripedDataTable();
        expect(refs.isDataTableStriped.value).toBe(true);
        expect(configRepository.setBool).toHaveBeenLastCalledWith('VRCX_dataTableStriped', true);
    });

    test('value setters assign and write with the matching method', () => {
        const { refs, setters } = define();

        setters.setDesktopToast('Always');
        setters.setWeekStartsOn(6);
        setters.setOpacity(0.75);
        setters.setGroups(['x']);

        expect(refs.desktopToast.value).toBe('Always');
        expect(refs.weekStartsOn.value).toBe(6);
        expect(refs.opacity.value).toBe(0.75);
        expect(refs.groups.value).toEqual(['x']);
        expect(configRepository.setString).toHaveBeenCalledWith('VRCX_desktopToast', 'Always');
        expect(configRepository.setInt).toHaveBeenCalledWith('VRCX_weekStartsOn', 6);
        expect(configRepository.setFloat).toHaveBeenCalledWith('VRCX_opacity', 0.75);
        expect(configRepository.setString).toHaveBeenCalledWith('VRCX_groups', '["x"]');
    });

    test('setters return the write promise', async () => {
        let resolveWrite;
        configRepository.setBool.mockReturnValue(
            new Promise((resolve) => {
                resolveWrite = resolve;
            })
        );
        const { setters } = define();
        let done = false;

        const pending = setters.setHideNicknames().then(() => {
            done = true;
        });
        await Promise.resolve();
        expect(done).toBe(false);

        resolveWrite();
        await pending;
        expect(done).toBe(true);
    });

    test('rejects unknown types', () => {
        expect(() => definePersistedSettings({ x: ['VRCX_x', 'date', null] })).toThrow('Unknown setting type');
    });
});
