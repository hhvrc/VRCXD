import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

const { log, rec, stub, defineComponent, h } = await vi.hoisted(async () => {
    const { defineComponent, h } = await import('vue');
    const log = { calls: [] };
    const rec =
        (name, impl) =>
        (...args) => {
            log.calls.push([name, JSON.parse(JSON.stringify(args ?? null))]);
            return impl?.(...args);
        };
    const stub = (tag, cls) =>
        defineComponent({
            inheritAttrs: true,
            props: ['modelValue', 'variant', 'size', 'disabled', 'placeholder', 'value', 'class'],
            emits: ['update:modelValue', 'click'],
            setup(props, { slots, emit }) {
                return () =>
                    h(
                        tag,
                        {
                            class: [cls, props.class],
                            'data-variant': props.variant,
                            'data-size': props.size,
                            'data-model': props.modelValue === undefined ? undefined : String(props.modelValue),
                            'data-placeholder': props.placeholder,
                            'data-value': props.value,
                            disabled: props.disabled ? '' : undefined,
                            onClick: (e) => emit('click', e)
                        },
                        slots.default?.()
                    );
            }
        });

    return { log, rec, stub, defineComponent, h };
});

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (k) => `t(${k})` }) }));
vi.mock('../../../../plugins', () => ({ i18n: { global: { t: (k) => `t(${k})` } } }));
vi.mock('../../../../shared/utils', async () => ({
    ...(await import('../../../../shared/utils/csv')),
    ...(await import('../../../../shared/utils/base/array'))
}));
vi.mock('vue-sonner', () => ({ toast: { success: () => {}, error: () => {} } }));

vi.mock('@/components/ui/select', () => ({
    Select: stub('div', 'Select'),
    SelectContent: stub('div', 'SelectContent'),
    SelectGroup: stub('div', 'SelectGroup'),
    SelectItem: stub('div', 'SelectItem'),
    SelectTrigger: stub('div', 'SelectTrigger'),
    SelectValue: stub('div', 'SelectValue')
}));
vi.mock('@/components/ui/dialog', () => ({
    Dialog: defineComponent({
        props: ['open'],
        setup:
            (p, { slots }) =>
            () =>
                p.open ? h('div', { class: 'Dialog' }, slots.default?.()) : null
    }),
    DialogContent: stub('div', 'DialogContent'),
    DialogHeader: stub('div', 'DialogHeader'),
    DialogTitle: stub('div', 'DialogTitle')
}));
vi.mock('@/components/ui/button', () => ({ Button: stub('button', 'Button') }));
vi.mock('../../../../components/ui/button', () => ({ Button: stub('button', 'Button') }));
vi.mock('@/components/ui/spinner', () => ({ Spinner: stub('span', 'Spinner') }));
vi.mock('@/components/ui/checkbox', () => ({ Checkbox: stub('input', 'Checkbox') }));
vi.mock('@/components/ui/input-group', () => ({
    InputGroupTextareaField: defineComponent({
        props: ['modelValue', 'rows', 'readonly', 'inputClass'],
        emits: ['update:modelValue', 'click'],
        setup:
            (p, { emit }) =>
            () =>
                h('textarea', {
                    value: p.modelValue,
                    'data-rows': p.rows,
                    'data-ic': p.inputClass,
                    readonly: p.readonly,
                    onInput: (e) => emit('update:modelValue', e.target.value),
                    onClick: (e) => emit('click', e)
                })
    })
}));
vi.mock('@/components/ui/data-table', () => ({
    DataTableLayout: defineComponent({
        props: ['table', 'loading', 'tableStyle', 'showPagination'],
        setup: (p) => () => {
            const o = p.table.opts;
            return h('table', { 'data-loading': String(p.loading) }, [
                h(
                    'tr',
                    o.columns.map((c) =>
                        h('th', { 'data-id': c.id, 'data-size': c.size, 'data-acc': c.accessorKey }, c.header())
                    )
                ),
                ...o.data.map((r) =>
                    h(
                        'tr',
                        { 'data-row': o.getRowId(r) },
                        o.columns.map((c) => h('td', c.cell({ row: { original: r } })))
                    )
                )
            ]);
        }
    })
}));
vi.mock('../../../../lib/table/useVrcxVueTable', () => ({
    useVrcxVueTable: (opts) => {
        log.calls.push(['persistKey', opts.persistKey]);
        return { table: { opts } };
    }
}));

vi.mock('../../../../api', () => ({
    avatarRequest: {
        getAvatar: rec('getAvatar', async ({ avatarId }) => {
            if (avatarId.endsWith('bad')) throw new Error('nope');
            if (avatarId.includes('0000000000ff')) throw new Error('fail');
            return { json: { id: avatarId, raw: true } };
        })
    },
    worldRequest: {
        getWorld: rec('getWorld', async ({ worldId }) => {
            if (worldId.includes('0000000000ff')) throw new Error('fail');
            return {
                ref: {
                    id: worldId,
                    name: `W ${worldId.slice(-2)}`,
                    authorId: 'usr_a',
                    authorName: 'Auth',
                    releaseStatus: 'private',
                    thumbnailImageUrl: 'thumb',
                    imageUrl: 'full'
                }
            };
        })
    },
    userRequest: {
        getUser: rec('getUser', async ({ userId }) => {
            if (userId.includes('0000000000ff')) throw new Error('fail');
            return { ref: { id: userId, displayName: `U ${userId.slice(-2)}`, img: 'uimg' } };
        })
    },
    favoriteRequest: { addFavorite: rec('addFavorite', async (a) => ({ a })) }
}));
vi.mock('../../../../coordinators/favoriteCoordinator', () => ({
    addLocalAvatarFavorite: rec('addLocalAvatarFavorite'),
    addLocalWorldFavorite: rec('addLocalWorldFavorite'),
    addLocalFriendFavorite: rec('addLocalFriendFavorite')
}));
vi.mock('../../../../coordinators/avatarCoordinator', () => ({
    showAvatarDialog: rec('showAvatarDialog'),
    applyAvatar: rec('applyAvatar', (json) => ({
        id: json.id,
        name: `A ${json.id.slice(-2)}`,
        authorId: 'usr_b',
        authorName: 'AuthB',
        releaseStatus: 'public',
        thumbnailImageUrl: 'th',
        imageUrl: 'im'
    }))
}));
vi.mock('../../../../coordinators/worldCoordinator', () => ({ showWorldDialog: rec('showWorldDialog') }));
vi.mock('../../../../coordinators/userCoordinator', () => ({ showUserDialog: rec('showUserDialog') }));
vi.mock('../../../../composables/useUserDisplay', () => ({
    useUserDisplay: () => ({ userImage: (u) => `ui:${u?.img}`, userImageFull: (u) => `uif:${u?.img}` })
}));

vi.mock('../../../../stores', async () => {
    const { defineStore } = await import('pinia');
    const { ref, reactive, computed } = await import('vue');
    const useFavoriteStore = defineStore('favorite', () => {
        const s = {};
        for (const k of ['avatar', 'world', 'friend']) {
            const C = { avatar: 'Avatar', world: 'World', friend: 'Friend' }[k];
            s[`favorite${C}Groups`] = ref([
                { name: 'g0', key: 'k0', type: k, displayName: 'G0', count: 1, capacity: 5 },
                { name: 'g1', key: 'k1', type: k, displayName: 'G1', count: 5, capacity: 5 }
            ]);
            s[`favorite${C}s`] = ref([
                { id: `${k}_f1`, name: 'F1', groupKey: 'k0', ref: { id: `${k}_f1`, name: 'F,1', authorId: 'a' } },
                { id: `${k}_f2`, name: 'F2', groupKey: 'k1', ref: { id: `${k}_f2`, name: 'F2', authorId: 'b' } }
            ]);
            const local =
                k === 'friend'
                    ? reactive({ L1: ['usr_c1', 'usr_missing'], L2: [] })
                    : reactive({ L1: [{ id: `${k}_l1`, name: 'L1x', thumbnailImageUrl: 't' }], L2: [] });
            s[`local${C}Favorites`] = local;
            s[`local${C}FavoriteGroups`] = ref(['L1', 'L2']);
            s[`local${C}FavoritesList`] = ref(k === 'friend' ? ['usr_c1', 'usr_x'] : [`${k}_c1`, `${k}_x`]);
            s[`local${C}FavGroupLength`] = computed(() => (g) => (local[g] ? local[g].length : 0));
        }
        s.importDialogInput = ref({ friend: '', world: '', avatar: '' });
        s.importDialogVisible = ref({ friend: false, world: false, avatar: false });
        s.getCachedFavoritesByObjectId = (id) => id.includes('000000000002');
        return s;
    });
    const useAvatarStore = defineStore('avatar', () => ({
        cachedAvatars: new Map([['avatar_c1', { id: 'avatar_c1', name: 'C1' }]])
    }));
    const useWorldStore = defineStore('world', () => ({
        cachedWorlds: reactive(new Map([['world_c1', { id: 'world_c1', name: 'C1' }]]))
    }));
    const useUserStore = defineStore('user', () => ({
        cachedUsers: reactive(new Map([['usr_c1', { id: 'usr_c1', displayName: 'Cu,1' }]]))
    }));
    const useGalleryStore = defineStore('gallery', () => ({ showFullscreenImageDialog: () => {} }));
    return { useFavoriteStore, useAvatarStore, useWorldStore, useUserStore, useGalleryStore };
});

import { useFavoriteStore } from '../../../../stores';
import FavoritesExportDialog from '../FavoritesExportDialog.vue';
import FavoritesImportDialog from '../FavoritesImportDialog.vue';

const idPrefix = { avatar: 'avtr', world: 'wrld', friend: 'usr' };
const id = (kind, n) => `${idPrefix[kind]}_00000000-0000-0000-0000-0000000000${n}`;
const callsOf = (name) => log.calls.filter((c) => c[0] === name).map((c) => c[1]);

async function openImport(kind, input) {
    const store = useFavoriteStore();
    const wrapper = mount(FavoritesImportDialog, { props: { kind } });
    store.importDialogInput[kind] = input;
    store.importDialogVisible[kind] = true;
    await flushPromises();
    return { wrapper, store };
}

async function selectGroup(wrapper, index, value) {
    wrapper.findAllComponents('.Select')[index].vm.$emit('update:modelValue', value);
    await flushPromises();
}

async function clickButton(wrapper, text) {
    await wrapper
        .findAll('button')
        .find((b) => b.text().includes(text))
        .trigger('click');
    await flushPromises();
}

describe('FavoritesImportDialog', () => {
    beforeEach(() => {
        setActivePinia(createPinia());
        log.calls = [];
    });

    it('fetches avatars through applyAvatar and dedupes pasted ids', async () => {
        const { wrapper } = await openImport(
            'avatar',
            `${id('avatar', '01')} ${id('avatar', '01')} ${id('avatar', 'ff')}`
        );
        expect(callsOf('getAvatar')).toEqual([[{ avatarId: id('avatar', '01') }], [{ avatarId: id('avatar', 'ff') }]]);
        expect(callsOf('applyAvatar')).toHaveLength(1);
        expect(wrapper.findAll('tr[data-row]').map((r) => r.attributes('data-row'))).toEqual([id('avatar', '01')]);
        expect(wrapper.find('pre').text()).toContain(`AvatarId: ${id('avatar', 'ff')}`);
        expect(wrapper.findAll('th').map((th) => th.attributes('data-id'))).toEqual([
            'image',
            'name',
            'author',
            'status',
            'action'
        ]);
        expect(callsOf('persistKey')).toEqual(['avatarImportDialog']);
    });

    it('uses args.ref for worlds without applyAvatar', async () => {
        const { wrapper } = await openImport('world', id('world', '01'));
        expect(callsOf('getWorld')).toEqual([[{ worldId: id('world', '01') }]]);
        expect(callsOf('applyAvatar')).toHaveLength(0);
        expect(wrapper.find('tr[data-row] span.cursor-pointer').text()).toBe('W 01');
        expect(wrapper.find('td button').classes()).toContain('w-6');
    });

    it('looks up friends by user id and shows only image, name and action columns', async () => {
        const { wrapper } = await openImport('friend', `${id('friend', '01')} ${id('avatar', '02')}`);
        expect(callsOf('getUser')).toEqual([[{ userId: id('friend', '01') }]]);
        expect(wrapper.findAll('th').map((th) => th.attributes('data-id'))).toEqual(['image', 'name', 'action']);
        expect(wrapper.find('td img').attributes('src')).toBe('ui:uimg');
        await wrapper.find('td span.cursor-pointer').trigger('click');
        expect(callsOf('showUserDialog')).toEqual([[id('friend', '01')]]);
    });

    it('imports into a VRChat group and stops at an already favorited item', async () => {
        const { wrapper } = await openImport('world', `${id('world', '01')} ${id('world', '02')} ${id('world', '03')}`);
        await selectGroup(wrapper, 0, 'g0');
        await clickButton(wrapper, 'world_import.import)');
        expect(callsOf('addFavorite')).toEqual([[{ type: 'world', favoriteId: id('world', '01'), tags: 'g0' }]]);
        expect(wrapper.find('pre').text()).toContain('World is already in favorites');
        expect(wrapper.findAll('tr[data-row]')).toHaveLength(2);
    });

    it('imports into a local group with the per-kind coordinator', async () => {
        const { wrapper } = await openImport('friend', `${id('friend', '01')} ${id('friend', '02')}`);
        await selectGroup(wrapper, 1, 'L1');
        await clickButton(wrapper, 'friend_import.import)');
        expect(callsOf('addLocalFriendFavorite')).toEqual([
            [id('friend', '01'), 'L1'],
            [id('friend', '02'), 'L1']
        ]);
        expect(wrapper.findAll('tr[data-row]')).toHaveLength(0);
    });

    it('keeps the per-kind select placeholders', async () => {
        const placeholders = async (kind) => {
            const { wrapper } = await openImport(kind, '');
            return wrapper.findAll('.SelectValue').map((v) => v.attributes('data-placeholder'));
        };
        expect(await placeholders('avatar')).toEqual([
            't(dialog.avatar_import.select_group_placeholder)',
            't(dialog.avatar_import.select_group_placeholder)'
        ]);
        expect(await placeholders('world')).toEqual([
            't(dialog.world_import.select_vrchat_group_placeholder)',
            't(dialog.world_import.select_local_group_placeholder)'
        ]);
        expect(await placeholders('friend')).toEqual([
            't(dialog.friend_import.select_group_placeholder)',
            't(dialog.world_import.select_local_group_placeholder)'
        ]);
    });
});

describe('FavoritesExportDialog', () => {
    beforeEach(() => {
        setActivePinia(createPinia());
        log.calls = [];
    });

    async function openExport(kind) {
        const wrapper = mount(FavoritesExportDialog, { props: { kind, visible: false } });
        await wrapper.setProps({ visible: true });
        await flushPromises();
        return wrapper;
    }
    const content = (wrapper) => wrapper.find('textarea').element.value;

    it('exports avatars with the selected columns, newest first', async () => {
        const wrapper = await openExport('avatar');
        expect(content(wrapper)).toBe('avatar_c1,C1\navatar_f2,F2\navatar_f1,"F,1"\nID,Name');
        await selectGroup(wrapper, 0, 'g0');
        expect(content(wrapper)).toBe('avatar_f1,"F,1"\nID,Name');
        wrapper.findAllComponents('.Checkbox')[2].vm.$emit('update:modelValue', true);
        await flushPromises();
        expect(content(wrapper)).toBe('avatar_f1,"F,1",a\nID,Name,Author ID');
        await selectGroup(wrapper, 1, 'L1');
        expect(content(wrapper)).toBe('avatar_l1,L1x,\nID,Name,Author ID');
    });

    it('exports worlds filtered by the selected VRChat group', async () => {
        const wrapper = await openExport('world');
        await selectGroup(wrapper, 0, 'g1');
        expect(content(wrapper)).toBe('world_f2,F2\nID,Name');
    });

    it('exports friends as UserID,Name only and resolves local ids from the user cache', async () => {
        const wrapper = await openExport('friend');
        expect(wrapper.findAllComponents('.Checkbox')).toHaveLength(0);
        expect(content(wrapper)).toBe('usr_c1,"Cu,1"\nfriend_f2,F2\nfriend_f1,F1\nUserID,Name');
        await selectGroup(wrapper, 1, 'L1');
        expect(content(wrapper)).toBe('usr_c1,"Cu,1"\nUserID,Name');
    });
});
