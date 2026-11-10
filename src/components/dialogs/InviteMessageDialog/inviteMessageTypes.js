import { toast } from 'vue-sonner';

import { instanceRequest, notificationRequest } from '../../../api';
import { useGalleryStore, useNotificationStore, useUserStore } from '../../../stores';
import { i18n } from '../../../plugins';
import { parseLocation } from '../../../shared/utils';
import { recordRecentAction } from '../../../composables/useRecentActions';

const { t } = i18n.global;

/**
 * Sends an invite or invite request with the chosen message slot. When the
 * invite dialog is open, invites every user picked there instead.
 *
 * @param {object} type Entry of INVITE_MESSAGE_TYPES
 * @param {object} options
 * @param {number} options.slot
 * @param {object} options.dialog { params, userId, messageSlot }
 * @param {object} [options.inviteDialog]
 */
function sendToUser(type, { slot, dialog: D, inviteDialog: J }) {
    const galleryStore = useGalleryStore();
    if (J?.visible) {
        const userStore = useUserStore();
        const inviteLoop = () => {
            if (J.userIds.length > 0) {
                const receiverUserId = J.userIds.shift();
                if (receiverUserId === userStore.currentUser.id) {
                    // can't invite self!?
                    const L = parseLocation(J.worldId);
                    instanceRequest
                        .selfInvite({
                            instanceId: L.instanceId,
                            worldId: L.worldId
                        })
                        .then(() => {
                            recordRecentAction(receiverUserId, 'Invite');
                        })
                        .finally(inviteLoop);
                } else {
                    const photo = Boolean(galleryStore.uploadImage);
                    notificationRequest[photo ? 'sendInvitePhoto' : 'sendInvite'](
                        {
                            instanceId: J.worldId,
                            worldId: J.worldId,
                            worldName: J.worldName,
                            messageSlot: slot
                        },
                        receiverUserId
                    )
                        .then(() => {
                            recordRecentAction(receiverUserId, photo ? 'Invite Photo' : 'Invite Message');
                        })
                        .finally(inviteLoop);
                }
            } else {
                J.loading = false;
                J.visible = false;
                toast.success('Invite message sent');
            }
        };
        inviteLoop();
        return;
    }
    const photo = Boolean(galleryStore.uploadImage);
    D.params[type.slotParam] = slot;
    notificationRequest[photo ? type.sendPhoto : type.send](D.params, D.userId)
        .then((args) => {
            recordRecentAction(D.userId, photo ? type.recentActionPhotoLabel : type.recentActionLabel);
            toast.success(photo ? type.sentPhotoToast() : type.sentToast());
            return args;
        })
        .catch((err) => {
            if (photo && type.clearImageOnPhotoError) {
                galleryStore.clearInviteImageUpload();
            }
            console.error(err);
        });
}

/**
 * Answers an invite or invite request notification with the chosen message
 * slot, then hides the notification.
 *
 * @param {object} type Entry of INVITE_MESSAGE_TYPES
 * @param {object} options
 * @param {number} options.slot
 * @param {object} options.dialog { invite, messageSlot }
 * @returns {Promise<unknown>} Settles once the response was sent or failed
 */
function sendResponse(type, { slot, dialog: D }) {
    const photo = Boolean(useGalleryStore().uploadImage);
    const params = {
        responseSlot: slot,
        rsvp: true
    };
    return notificationRequest[photo ? type.sendPhoto : type.send](params, D.invite.id)
        .then((args) => {
            notificationRequest
                .hideNotification({
                    notificationId: D.invite.id
                })
                .then(() => {
                    useNotificationStore().handleNotificationHide(D.invite.id);
                });
            toast.success(photo ? type.sentPhotoToast() : type.sentToast());
            return args;
        })
        .catch((err) => {
            console.error(photo ? 'Invite response photo failed' : 'Invite response failed', err);
            toast.error(t('message.error'));
        });
}

// Shared by invites and invite requests sent to a user.
const toUser = {
    sendSlot: sendToUser,
    // close the message picker right away instead of waiting for the send
    closeAfterSend: false,
    stepDialogClass: 'x-dialog sm:max-w-100',
    confirmCancelClass: undefined,
    confirmLabel: () => t('common.actions.confirm'),
    editI18nPrefix: 'edit_send_invite_message',
    editFailedLog: 'Invite message update failed',
    editFailedToast: false,
    editUpdatedToast: () => 'Invite message updated',
    pickerCancelClass: 'mr-2',
    refreshVariant: undefined,
    actionButtonClass: undefined,
    emitCloseInviteDialog: false
};

// Shared by responses to invites and to invite requests.
const response = {
    sendSlot: sendResponse,
    send: 'sendInviteResponse',
    sendPhoto: 'sendInviteResponsePhoto',
    sentToast: () => t('message.invite.response_sent'),
    sentPhotoToast: () => t('message.invite.response_photo_sent'),
    closeAfterSend: true,
    stepDialogClass: undefined,
    confirmI18nPrefix: 'invite_response_message',
    confirmCancelClass: 'mr-2',
    confirmLabel: () => t('dialog.invite_response_message.confirm'),
    editI18nPrefix: 'edit_send_invite_response_message',
    editFailedLog: 'Invite response message update failed',
    editFailedToast: true,
    editUpdatedToast: () => t('message.invite.message_updated'),
    pickerCancelClass: 'mr-2',
    refreshVariant: undefined,
    actionButtonClass: undefined,
    emitCloseInviteDialog: false
};

/**
 * The four kinds of VRChat invite message slots, keyed by messageType, and
 * how InviteMessageDialog lists, edits and sends each of them.
 */
export const INVITE_MESSAGE_TYPES = {
    message: {
        ...toUser,
        i18nPrefix: 'invite_message',
        confirmI18nPrefix: 'invite_message',
        persistKey: 'invite-message',
        tableKey: 'inviteMessageTable',
        slotParam: 'messageSlot',
        send: 'sendInvite',
        sendPhoto: 'sendInvitePhoto',
        recentActionLabel: 'Invite Message',
        recentActionPhotoLabel: 'Invite Photo',
        sentToast: () => 'Invite message sent',
        sentPhotoToast: () => 'Invite photo message sent',
        clearImageOnPhotoError: false,
        pickerCancelClass: undefined,
        refreshVariant: 'outline',
        actionButtonClass: 'w-6 h-6',
        emitCloseInviteDialog: true
    },
    request: {
        ...toUser,
        i18nPrefix: 'invite_request_message',
        confirmI18nPrefix: 'invite_request_message',
        persistKey: 'invite-request-message',
        tableKey: 'inviteRequestMessageTable',
        slotParam: 'requestSlot',
        send: 'sendRequestInvite',
        sendPhoto: 'sendRequestInvitePhoto',
        recentActionLabel: 'Request Invite Message',
        recentActionPhotoLabel: 'Request Invite Photo',
        sentToast: () => 'Request invite message sent',
        sentPhotoToast: () => 'Request invite photo message sent',
        clearImageOnPhotoError: true
    },
    response: {
        ...response,
        i18nPrefix: 'invite_response_message',
        persistKey: 'invite-response-message',
        tableKey: 'inviteResponseMessageTable'
    },
    requestResponse: {
        ...response,
        i18nPrefix: 'invite_request_response_message',
        persistKey: 'invite-request-response-message',
        tableKey: 'inviteRequestResponseMessageTable'
    }
};

/**
 * Sends the given message slot as the given kind of invite message.
 *
 * @param {'message' | 'request' | 'response' | 'requestResponse'} messageType
 * @param {object} options
 * @param {number} options.slot
 * @param {object} options.dialog
 * @param {object} [options.inviteDialog]
 * @returns {Promise<unknown> | undefined} For responses, settles once sent or failed
 */
export function sendInviteMessage(messageType, options) {
    const type = INVITE_MESSAGE_TYPES[messageType];
    return type.sendSlot(type, options);
}
