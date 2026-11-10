import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, ref } from 'vue';
import { mount } from '@vue/test-utils';

const mocks = vi.hoisted(() => ({
    appearance: { sortFavorites: false, setSortFavorites: vi.fn() },
    modal: { confirm: vi.fn(), prompt: vi.fn() },
    saveFavoriteGroup: vi.fn(),
    clearFavoriteGroup: vi.fn(),
    deleteFavorite: vi.fn(),
    toastSuccess: vi.fn()
}));

vi.mock('vue-i18n', () => ({
    useI18n: () => ({ t: (key) => key })
}));
vi.mock('vue-sonner', () => ({
    toast: { success: (...args) => mocks.toastSuccess(...args) }
}));
vi.mock('pinia', () => ({
    storeToRefs: (store) => ({ sortFavorites: { value: store.sortFavorites } })
}));
vi.mock('../../../../stores', () => ({
    useAppearanceSettingsStore: () => mocks.appearance,
    useModalStore: () => mocks.modal
}));
vi.mock('../../../../api', () => ({
    favoriteRequest: {
        saveFavoriteGroup: (...args) => mocks.saveFavoriteGroup(...args),
        clearFavoriteGroup: (...args) => mocks.clearFavoriteGroup(...args),
        deleteFavorite: (...args) => mocks.deleteFavorite(...args)
    }
}));
vi.mock('worker-timers', () => ({
    setTimeout: (fn) => setTimeout(fn, 0),
    clearTimeout: (id) => clearTimeout(id)
}));

import { groupFavoritesByGroupKey, useFavoritesPage, useLocalFavoritesRefresh } from '../useFavoritesPage';

function mountPage(overrides = {}) {
    const deps = {
        selected: ref([]),
        activeRemoteGroup: ref({ key: 'avatar:avatars1', name: 'avatars1', type: 'avatar' }),
        currentRemoteFavorites: ref([{ id: 'a' }, { id: 'b' }]),
        importInput: ref(''),
        showImportDialog: vi.fn(),
        handleFavoriteGroup: vi.fn(),
        refreshFavorites: vi.fn(),
        handleGroupMenuVisible: vi.fn(),
        remoteGroupMenuKey: (key) => `remote:${key}`,
        remoteGroupType: () => 'avatar',
        ...overrides
    };
    let api;
    mount(
        defineComponent({
            setup() {
                api = useFavoritesPage(deps);
                return () => h('div');
            }
        })
    );
    return { api, deps };
}

describe('groupFavoritesByGroupKey', () => {
    it('groups by key and skips ungrouped entries', () => {
        const a = { id: 'a', groupKey: 'g1' };
        const b = { id: 'b', groupKey: 'g2' };
        const c = { id: 'c', groupKey: 'g1' };
        expect(groupFavoritesByGroupKey([a, { id: 'x' }, b, c])).toEqual({ g1: [a, c], g2: [b] });
    });
});

describe('useFavoritesPage', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        mocks.saveFavoriteGroup.mockResolvedValue({ json: { id: 'fvgrp_1' } });
    });

    it('selects, copies and clears remote favorites', async () => {
        const { api, deps } = mountPage();
        api.toggleSelection('a', true);
        api.toggleSelection('a', true);
        expect(deps.selected.value).toEqual(['a']);
        expect(api.hasSelection.value).toBe(true);
        expect(api.isAllSelected.value).toBe(false);

        api.toggleSelectAll();
        expect(deps.selected.value).toEqual(['a', 'b']);
        expect(api.isAllSelected.value).toBe(true);

        api.copySelected();
        expect(deps.importInput.value).toBe('a\nb\n');
        expect(deps.showImportDialog).toHaveBeenCalled();

        api.toggleSelectAll();
        expect(deps.selected.value).toEqual([]);
    });

    it('leaves edit mode when a search starts and clears selection on exit', async () => {
        const { api, deps } = mountPage();
        api.editMode.value = true;
        await nextTick();
        deps.selected.value = ['a'];
        api.search.value = 'abc';
        expect(api.isSearchActive.value).toBe(true);
        await nextTick();
        expect(api.editMode.value).toBe(false);
        await nextTick();
        expect(deps.selected.value).toEqual([]);
    });

    it('bulk unfavorites remote ids', () => {
        const { api, deps } = mountPage();
        deps.selected.value = ['a', 'b'];
        api.editMode.value = true;
        api.bulkUnfavoriteRemote(['a', 'b']);
        expect(mocks.deleteFavorite.mock.calls).toEqual([[{ objectId: 'a' }], [{ objectId: 'b' }]]);
        expect(deps.selected.value).toEqual([]);
        expect(api.editMode.value).toBe(false);
    });

    it('saves visibility with the kind specific group type and message', async () => {
        const { api, deps } = mountPage({
            remoteGroupType: (group) => group.type,
            visibilityUpdatedMessage: () => 'custom'
        });
        api.handleVisibilitySelection({ key: 'vrcPlusWorld:x', name: 'x', type: 'vrcPlusWorld' }, 'public');
        expect(mocks.saveFavoriteGroup).toHaveBeenCalledWith({
            type: 'vrcPlusWorld',
            group: 'x',
            visibility: 'public'
        });
        await Promise.resolve();
        await Promise.resolve();
        expect(deps.handleFavoriteGroup).toHaveBeenCalledWith({
            json: { id: 'fvgrp_1' },
            params: { favoriteGroupId: 'fvgrp_1' }
        });
        expect(mocks.toastSuccess).toHaveBeenCalledWith('custom');
        expect(deps.handleGroupMenuVisible).toHaveBeenCalledWith('remote:vrcPlusWorld:x', false);
        expect(deps.refreshFavorites).toHaveBeenCalled();
    });

    it('renames a remote group', async () => {
        mocks.modal.prompt.mockResolvedValue({ ok: true, value: ' New ' });
        const { api, deps } = mountPage({ remoteGroupType: () => 'friend' });
        api.handleRemoteRename({ key: 'friend:group_0', name: 'group_0', displayName: 'Old', type: 'friend' });
        expect(deps.handleGroupMenuVisible).toHaveBeenCalledWith('remote:friend:group_0', false);
        await vi.waitFor(() => expect(deps.refreshFavorites).toHaveBeenCalled());
        expect(mocks.saveFavoriteGroup).toHaveBeenCalledWith({
            type: 'friend',
            group: 'group_0',
            displayName: 'New'
        });
    });

    it('only passes destructive when asked to', async () => {
        mocks.modal.confirm.mockResolvedValue({ ok: true });
        const group = { key: 'world:worlds1', name: 'worlds1', type: 'world' };

        mountPage().api.handleRemoteClear(group);
        expect(mocks.modal.confirm).toHaveBeenLastCalledWith({
            description: 'confirm.clear_group',
            title: 'confirm.title'
        });

        mountPage({ destructiveClear: true }).api.handleRemoteClear(group);
        expect(mocks.modal.confirm).toHaveBeenLastCalledWith({
            description: 'confirm.clear_group',
            title: 'confirm.title',
            destructive: true
        });
        await vi.waitFor(() => expect(mocks.clearFavoriteGroup).toHaveBeenCalledTimes(2));
        expect(mocks.clearFavoriteGroup).toHaveBeenCalledWith({ type: 'world', group: 'worlds1' });
    });

    it('formats visibility and toggles sort order', () => {
        const { api } = mountPage();
        expect(api.formatVisibility('friends')).toBe('Friends');
        expect(api.formatVisibility('')).toBe('');
        api.handleSortFavoritesChange('name');
        expect(mocks.appearance.setSortFavorites).not.toHaveBeenCalled();
        api.handleSortFavoritesChange('date');
        expect(mocks.appearance.setSortFavorites).toHaveBeenCalledTimes(1);
    });
});

describe('useLocalFavoritesRefresh', () => {
    function mountRefresh(getIds, refreshOne) {
        let api;
        const wrapper = mount(
            defineComponent({
                setup() {
                    api = useLocalFavoritesRefresh(getIds, refreshOne);
                    return () => h('div');
                }
            })
        );
        return { api, wrapper };
    }

    it('refreshes every id and keeps going after errors', async () => {
        const refreshOne = vi.fn(async (id) => {
            if (id === 'b') throw new Error('gone');
        });
        const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
        const { api } = mountRefresh(() => ['a', 'b', 'c'], refreshOne);
        await api.refreshLocalFavorites();
        expect(refreshOne.mock.calls).toEqual([['a'], ['b'], ['c']]);
        expect(api.refreshingLocalFavorites.value).toBe(false);
        consoleError.mockRestore();
    });

    it('stops when cancelled or unmounted', async () => {
        const refreshOne = vi.fn(async () => {});
        const { api, wrapper } = mountRefresh(() => ['a', 'b', 'c'], refreshOne);
        const running = api.refreshLocalFavorites();
        expect(api.refreshingLocalFavorites.value).toBe(true);
        await Promise.resolve();
        wrapper.unmount();
        await running;
        expect(refreshOne).toHaveBeenCalledTimes(1);
        expect(api.refreshingLocalFavorites.value).toBe(false);
    });
});
