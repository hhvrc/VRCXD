import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';

const mocks = vi.hoisted(() => ({
    log: [],
    behavior: {},
    tableOpts: [],
    stores: {}
}));

vi.mock('pinia', async (i) => {
    const { toRefs } = await import('vue');
    return { ...(await i()), storeToRefs: (s) => toRefs(s) };
});
vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (k) => `t(${k})` }) }));
vi.mock('../../../../plugins', () => ({ i18n: { global: { t: (k) => `t(${k})` } } }));
vi.mock('vue-sonner', () => {
    const toast = (...a) => mocks.log.push(['toast', ...a]);
    toast.success = (...a) => mocks.log.push(['toast.success', ...a]);
    toast.error = (...a) => mocks.log.push(['toast.error', ...a]);
    return { toast };
});
vi.mock('../../../../composables/useRecentActions', () => ({
    recordRecentAction: (...a) => mocks.log.push(['recordRecentAction', ...a])
}));
vi.mock('../../../../shared/utils', () => ({
    parseLocation: (tag) => ({ instanceId: `${tag}#i`, worldId: `${tag}#w` })
}));
function apiFn(name) {
    return (...a) => {
        mocks.log.push([name, ...JSON.parse(JSON.stringify(a))]);
        const b = mocks.behavior[name];
        if (b === 'reject') return Promise.reject(new Error(name));
        if (typeof b === 'function') return Promise.resolve(b(...a));
        return Promise.resolve({ json: {} });
    };
}
vi.mock('../../../../api', () => ({
    instanceRequest: { selfInvite: apiFn('selfInvite') },
    inviteMessagesRequest: { editInviteMessage: apiFn('editInviteMessage') },
    notificationRequest: {
        sendInvite: apiFn('sendInvite'),
        sendInvitePhoto: apiFn('sendInvitePhoto'),
        sendRequestInvite: apiFn('sendRequestInvite'),
        sendRequestInvitePhoto: apiFn('sendRequestInvitePhoto'),
        sendInviteResponse: apiFn('sendInviteResponse'),
        sendInviteResponsePhoto: apiFn('sendInviteResponsePhoto'),
        hideNotification: apiFn('hideNotification')
    }
}));
vi.mock('../../../../stores', () => ({
    useGalleryStore: () => mocks.stores.gallery,
    useUserStore: () => mocks.stores.user,
    useInviteStore: () => mocks.stores.invite,
    useNotificationStore: () => mocks.stores.notification
}));
vi.mock('@/lib/table/useVrcxVueTable', () => ({
    useVrcxVueTable: (opts) => {
        mocks.tableOpts.push(opts);
        return { table: { persistKey: opts.persistKey } };
    }
}));
vi.mock('@/components/CountdownTimer.vue', () => ({ default: { template: '<span />' } }));
vi.mock('@/components/ui/dialog', () => ({
    Dialog: { props: ['open'], template: '<div data-dialog :data-open="String(open)"><slot /></div>' },
    DialogContent: { props: ['class'], template: '<div :data-class="$props.class"><slot /></div>' },
    DialogHeader: { template: '<div><slot /></div>' },
    DialogTitle: { template: '<h1><slot /></h1>' },
    DialogFooter: { template: '<div><slot /></div>' }
}));
vi.mock('@/components/ui/button', () => ({
    Button: {
        props: ['variant', 'class', 'disabled', 'size'],
        emits: ['click'],
        template:
            '<button :data-variant="variant" :data-class="$props.class" @click="$emit(\'click\')"><slot /></button>'
    }
}));
vi.mock('@/components/ui/data-table', () => ({
    DataTableLayout: { props: ['table', 'onRowClick'], template: '<div data-table :data-key="table.persistKey" />' }
}));
vi.mock('@/components/ui/input-group', () => ({
    InputGroupTextareaField: {
        props: ['modelValue'],
        emits: ['update:modelValue'],
        template: '<textarea data-textarea />'
    }
}));

import InviteMessageDialog from '../InviteMessageDialog.vue';

const row = (type) => ({ slot: 3, message: 'hello', messageType: type, updatedAt: '' });

function setup(type, { photo = false, inviteDialog, behavior = {} } = {}) {
    mocks.log.length = 0;
    mocks.tableOpts.length = 0;
    mocks.behavior = behavior;
    mocks.stores.gallery = reactive({
        uploadImage: photo ? 'IMG' : '',
        clearInviteImageUpload: () => mocks.log.push(['clearInviteImageUpload']),
        inviteImageUpload: () => {}
    });
    mocks.stores.user = reactive({ currentUser: { id: 'usr_me' }, isLocalUserVrcPlusSupporter: true });
    mocks.stores.invite = reactive({
        refreshInviteMessageTableData: (...a) => mocks.log.push(['refresh', ...a]),
        inviteMessageTable: { data: [] },
        inviteRequestMessageTable: { data: [] },
        inviteResponseMessageTable: { data: [] },
        inviteRequestResponseMessageTable: { data: [] }
    });
    mocks.stores.notification = {
        handleNotificationHide: (...a) => mocks.log.push(['handleNotificationHide', ...a])
    };
    const response = type === 'response' || type === 'requestResponse';
    const dialog = response
        ? { messageSlot: {}, invite: { id: 'not_1' } }
        : { messageSlot: {}, params: { worldId: 'wrld_1:1' }, userId: 'usr_1' };
    let wrapper;
    const props = {
        type,
        visible: true,
        dialog,
        'onUpdate:visible': (v) => {
            mocks.log.push(['update:visible', v]);
            wrapper.setProps({ visible: v });
        },
        'onUpdate:dialog': (v) => wrapper.setProps({ dialog: v }),
        onCloseInviteDialog: () => mocks.log.push(['closeInviteDialog'])
    };
    if (inviteDialog) props.inviteDialog = inviteDialog;
    wrapper = mount(InviteMessageDialog, { props });
    return wrapper;
}

async function confirmRow(wrapper, type) {
    wrapper.findComponent('[data-table]').props('onRowClick')({ original: row(type) });
    await flushPromises();
    const confirm = wrapper.findAll('button').find((b) => /confirm\)$/.test(b.text()));
    await confirm.trigger('click');
    await flushPromises();
}

async function editRow(wrapper, type, newMessage) {
    const columns = mocks.tableOpts[0].columns.value;
    columns
        .at(-1)
        .cell({ row: { original: row(type) } })
        .props.onClick({ stopPropagation() {} });
    await flushPromises();
    if (newMessage) {
        wrapper.findComponent('[data-textarea]').vm.$emit('update:modelValue', newMessage);
        await flushPromises();
    }
    const send = wrapper.findAll('button').find((b) => b.text().endsWith('.send)'));
    await send.trigger('click');
    await flushPromises();
}

describe('InviteMessageDialog.vue', () => {
    beforeEach(() => {
        mocks.log.length = 0;
    });

    it('uses the persist key and labels of each type', () => {
        const expected = {
            message: ['invite-message', 'invite_message'],
            request: ['invite-request-message', 'invite_request_message'],
            response: ['invite-response-message', 'invite_response_message'],
            requestResponse: ['invite-request-response-message', 'invite_request_response_message']
        };
        for (const [type, [persistKey, prefix]] of Object.entries(expected)) {
            const wrapper = setup(type);
            expect(mocks.tableOpts[0].persistKey).toBe(persistKey);
            expect(wrapper.find('h1').text()).toBe(`t(dialog.${prefix}.header)`);
            const refresh = wrapper.findAll('button').find((b) => b.text().endsWith('.refresh)'));
            refresh.trigger('click');
            expect(mocks.log).toContainEqual(['refresh', type]);
            wrapper.unmount();
        }
    });

    it('gives only the invite picker the outline refresh button and small edit buttons', () => {
        const wrapper = setup('message');
        const refresh = wrapper.findAll('button').find((b) => b.text().endsWith('.refresh)'));
        expect(refresh.attributes('data-variant')).toBe('outline');
        const cell = mocks.tableOpts[0].columns.value.at(-1).cell({ row: { original: row('message') } });
        expect(cell.props.class).toBe('w-6 h-6');
        wrapper.unmount();

        const request = setup('request');
        const requestCell = mocks.tableOpts[0].columns.value.at(-1).cell({ row: { original: row('request') } });
        expect(requestCell.props.class).toBeUndefined();
        request.unmount();
    });

    it('sends an invite message to a single user and closes', async () => {
        const wrapper = setup('message');
        await confirmRow(wrapper, 'message');
        expect(mocks.log).toEqual([
            ['sendInvite', { worldId: 'wrld_1:1', messageSlot: 3 }, 'usr_1'],
            ['update:visible', false],
            ['closeInviteDialog'],
            ['recordRecentAction', 'usr_1', 'Invite Message'],
            ['toast.success', 'Invite message sent']
        ]);
    });

    it('sends an invite request photo and only clears the image when that fails', async () => {
        let wrapper = setup('request', { photo: true });
        await confirmRow(wrapper, 'request');
        expect(mocks.log).toEqual([
            ['sendRequestInvitePhoto', { worldId: 'wrld_1:1', requestSlot: 3 }, 'usr_1'],
            ['update:visible', false],
            ['recordRecentAction', 'usr_1', 'Request Invite Photo'],
            ['toast.success', 'Request invite photo message sent']
        ]);
        wrapper.unmount();

        wrapper = setup('request', { photo: true, behavior: { sendRequestInvitePhoto: 'reject' } });
        await confirmRow(wrapper, 'request');
        expect(mocks.log).toContainEqual(['clearInviteImageUpload']);
        wrapper.unmount();

        wrapper = setup('message', { photo: true, behavior: { sendInvitePhoto: 'reject' } });
        await confirmRow(wrapper, 'message');
        expect(mocks.log).not.toContainEqual(['clearInviteImageUpload']);
    });

    it('invites every user of the invite dialog, self-inviting the current user', async () => {
        const inviteDialog = reactive({
            visible: true,
            loading: true,
            userIds: ['usr_a', 'usr_me'],
            worldId: 'wrld_x:9',
            worldName: 'World'
        });
        const wrapper = setup('message', { inviteDialog });
        await confirmRow(wrapper, 'message');
        expect(mocks.log).toEqual([
            [
                'sendInvite',
                { instanceId: 'wrld_x:9', worldId: 'wrld_x:9', worldName: 'World', messageSlot: 3 },
                'usr_a'
            ],
            ['update:visible', false],
            ['closeInviteDialog'],
            ['recordRecentAction', 'usr_a', 'Invite Message'],
            ['selfInvite', { instanceId: 'wrld_x:9#i', worldId: 'wrld_x:9#w' }],
            ['recordRecentAction', 'usr_me', 'Invite'],
            ['toast.success', 'Invite message sent']
        ]);
        expect(inviteDialog.visible).toBe(false);
        expect(inviteDialog.loading).toBe(false);
    });

    it('answers a notification, hides it and closes once sent', async () => {
        const wrapper = setup('requestResponse', { photo: true });
        await confirmRow(wrapper, 'requestResponse');
        expect(mocks.log).toEqual([
            ['sendInviteResponsePhoto', { responseSlot: 3, rsvp: true }, 'not_1'],
            ['hideNotification', { notificationId: 'not_1' }],
            ['toast.success', 't(message.invite.response_photo_sent)'],
            ['handleNotificationHide', 'not_1'],
            ['update:visible', false]
        ]);
    });

    it('saves an edited message before sending it', async () => {
        const wrapper = setup('response', {
            behavior: { editInviteMessage: () => ({ json: { 3: { message: 'changed' } } }) }
        });
        await editRow(wrapper, 'response', 'changed');
        expect(mocks.log.slice(0, 3)).toEqual([
            ['editInviteMessage', { message: 'changed' }, 'response', 3],
            ['toast', 't(message.invite.message_updated)'],
            ['sendInviteResponse', { responseSlot: 3, rsvp: true }, 'not_1']
        ]);
        expect(mocks.log.at(-1)).toEqual(['update:visible', false]);
    });

    it('does not send when saving the edited message fails', async () => {
        let wrapper = setup('message', { behavior: { editInviteMessage: 'reject' } });
        await editRow(wrapper, 'message', 'changed');
        expect(mocks.log).toEqual([['editInviteMessage', { message: 'changed' }, 'message', 3]]);
        wrapper.unmount();

        wrapper = setup('response', { behavior: { editInviteMessage: 'reject' } });
        await editRow(wrapper, 'response', 'changed');
        expect(mocks.log).toEqual([
            ['editInviteMessage', { message: 'changed' }, 'response', 3],
            ['toast.error', 't(message.error)']
        ]);
        wrapper.unmount();

        wrapper = setup('request', { behavior: { editInviteMessage: () => ({ json: { 3: { message: 'hello' } } }) } });
        await editRow(wrapper, 'request', 'changed');
        expect(mocks.log).toEqual([
            ['editInviteMessage', { message: 'changed' }, 'request', 3],
            ['toast.error', 't(message.invite.message_update_failed)']
        ]);
    });

    it('sends an unchanged message without saving it', async () => {
        const wrapper = setup('request');
        await editRow(wrapper, 'request');
        expect(mocks.log).toEqual([
            ['sendRequestInvite', { worldId: 'wrld_1:1', requestSlot: 3 }, 'usr_1'],
            ['update:visible', false],
            ['recordRecentAction', 'usr_1', 'Request Invite Message'],
            ['toast.success', 'Request invite message sent']
        ]);
    });
});
