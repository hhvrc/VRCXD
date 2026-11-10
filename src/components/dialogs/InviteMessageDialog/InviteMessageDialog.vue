<template>
    <Dialog
        :open="visible"
        @update:open="
            (open) => {
                if (!open) cancelSendInvite();
            }
        ">
        <DialogContent class="x-dialog sm:max-w-200">
            <DialogHeader>
                <DialogTitle>{{ t(`dialog.${config.i18nPrefix}.header`) }}</DialogTitle>
            </DialogHeader>

            <template v-if="isLocalUserVrcPlusSupporter">
                <!--            <template v-if="gallerySelectDialog.selectedFileId">-->
                <!--                <div class="mr-1.5" style="display: inline-block; flex: none">-->
                <!--                    <el-popover placement="right" :width="500px" trigger="click">-->
                <!--                        <template #reference>-->
                <!--                            <img-->
                <!--                                class="x-link"-->
                <!--                                :src="gallerySelectDialog.selectedImageUrl"-->
                <!--                                style="flex: none; width: 60px; height: 60px; border-radius: 4px; object-fit: cover" />-->
                <!--                        </template>-->
                <!--                        <img-->
                <!--                            class="x-link"-->
                <!--                            :src="gallerySelectDialog.selectedImageUrl"-->
                <!--                            style="height: 500px"-->
                <!--                            @click="showFullscreenImageDialog(gallerySelectDialog.selectedImageUrl)" />-->
                <!--                    </el-popover>-->
                <!--                </div>-->
                <!--                <el-button size="small" @click="clearImageGallerySelect" style="vertical-align: top">-->
                <!--                    {{ t('dialog.invite_message.clear_selected_image') }}-->
                <!--                </el-button>-->
                <!--            </template>-->
                <!--            <template v-else>-->
                <!--                <el-button size="small" @click="showGallerySelectDialog" style="margin-right: 6px">-->
                <!--                    {{ t('dialog.invite_message.select_image') }}-->
                <!--                </el-button>-->
                <!--            </template>-->
                <input class="inviteImageUploadButton" type="file" accept="image/*" @change="inviteImageUpload" />
            </template>

            <DataTableLayout
                style="margin-top: 8px"
                :table="inviteMessageTanstackTable"
                :loading="false"
                :show-pagination="false"
                :on-row-click="handleInviteMessageRowClick" />

            <DialogFooter>
                <Button variant="secondary" :class="config.pickerCancelClass" @click="cancelSendInvite">
                    {{ t(`dialog.${config.i18nPrefix}.cancel`) }}
                </Button>
                <Button :variant="config.refreshVariant" @click="refreshInviteMessageTableData(type)">
                    {{ t(`dialog.${config.i18nPrefix}.refresh`) }}
                </Button>
            </DialogFooter>
        </DialogContent>

        <Dialog
            :open="isConfirmDialogVisible"
            @update:open="
                (open) => {
                    if (!open) cancelInviteConfirm();
                }
            ">
            <DialogContent :class="config.stepDialogClass">
                <DialogHeader>
                    <DialogTitle>{{ t(`dialog.${config.confirmI18nPrefix}.header`) }}</DialogTitle>
                </DialogHeader>

                <div class="text-xs">
                    <span>{{ t(`dialog.${config.confirmI18nPrefix}.confirmation`) }}</span>
                </div>

                <DialogFooter>
                    <Button variant="secondary" :class="config.confirmCancelClass" @click="cancelInviteConfirm">
                        {{ t(`dialog.${config.confirmI18nPrefix}.cancel`) }}
                    </Button>
                    <Button @click="sendInviteConfirm">
                        {{ config.confirmLabel() }}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>

        <Dialog
            :open="editDialog.visible"
            @update:open="
                (open) => {
                    if (!open) cancelEditAndSendInvite();
                }
            ">
            <DialogContent :class="config.stepDialogClass">
                <DialogHeader>
                    <DialogTitle>{{ t(`dialog.${config.editI18nPrefix}.header`) }}</DialogTitle>
                </DialogHeader>

                <div class="text-xs">
                    <span>{{ t(`dialog.${config.editI18nPrefix}.description`) }}</span>
                </div>

                <InputGroupTextareaField
                    v-model="editDialog.newMessage"
                    :maxlength="64"
                    :rows="2"
                    class="mt-2.5"
                    placeholder=""
                    show-count />

                <DialogFooter>
                    <Button variant="secondary" class="mr-2" @click="cancelEditAndSendInvite">
                        {{ t(`dialog.${config.editI18nPrefix}.cancel`) }}
                    </Button>
                    <Button @click="saveEditAndSendInvite" :disabled="!editDialog.newMessage">
                        {{ t(`dialog.${config.editI18nPrefix}.send`) }}
                    </Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    </Dialog>
</template>

<script setup>
    import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
    import { computed, ref } from 'vue';
    import { Button } from '@/components/ui/button';
    import { DataTableLayout } from '@/components/ui/data-table';
    import { InputGroupTextareaField } from '@/components/ui/input-group';
    import { storeToRefs } from 'pinia';
    import { toast } from 'vue-sonner';
    import { useI18n } from 'vue-i18n';
    import { useVrcxVueTable } from '@/lib/table/useVrcxVueTable';

    import { INVITE_MESSAGE_TYPES, sendInviteMessage } from './inviteMessageTypes';
    import { useGalleryStore, useInviteStore, useUserStore } from '../../../stores';
    import { createColumns } from './inviteMessageColumns.jsx';
    import { inviteMessagesRequest } from '../../../api';

    // Picks one of the saved invite messages of the given type and sends it,
    // after a confirmation or after editing it.
    const props = defineProps({
        type: {
            type: String,
            required: true,
            validator: (value) => value in INVITE_MESSAGE_TYPES
        },
        visible: {
            type: Boolean,
            default: false
        },
        // { messageSlot, params, userId } for invites and invite requests,
        // { messageSlot, invite } for responses
        dialog: {
            type: Object,
            default: () => ({})
        },
        inviteDialog: {
            type: Object,
            required: false,
            default: () => ({})
        }
    });

    const emit = defineEmits(['closeInviteDialog', 'update:visible', 'update:dialog']);

    const { t } = useI18n();

    const inviteStore = useInviteStore();
    const { refreshInviteMessageTableData } = inviteStore;
    const inviteStoreRefs = storeToRefs(inviteStore);
    const { inviteImageUpload } = useGalleryStore();
    const { isLocalUserVrcPlusSupporter } = storeToRefs(useUserStore());

    const config = INVITE_MESSAGE_TYPES[props.type];
    const inviteMessageTable = inviteStoreRefs[config.tableKey];

    const isConfirmDialogVisible = ref(false);

    const editDialog = ref({
        visible: false,
        newMessage: ''
    });

    const inviteMessageRows = computed(() => inviteMessageTable.value?.data ?? []);
    const inviteMessageColumns = computed(() =>
        createColumns({
            onEdit: showEditAndSendInviteDialog,
            actionButtonClass: config.actionButtonClass
        })
    );

    const { table: inviteMessageTanstackTable } = useVrcxVueTable({
        persistKey: config.persistKey,
        get data() {
            return inviteMessageRows.value;
        },
        columns: inviteMessageColumns,
        getRowId: (row) => String(row?.slot ?? ''),
        enablePagination: false,
        initialSorting: [{ id: 'slot', desc: false }]
    });

    /**
     * @param row
     */
    function handleInviteMessageRowClick(row) {
        showSendInviteConfirmDialog(row?.original);
    }

    /**
     * @param row
     */
    function showSendInviteConfirmDialog(row) {
        emit('update:dialog', { ...props.dialog, messageSlot: row });
        isConfirmDialogVisible.value = true;
    }

    /**
     * @param row
     */
    function showEditAndSendInviteDialog(row) {
        emit('update:dialog', { ...props.dialog, messageSlot: row });
        editDialog.value = {
            newMessage: row.message,
            visible: true
        };
    }

    function cancelSendInvite() {
        emit('update:visible', false);
    }

    function closeInviteDialog() {
        cancelSendInvite();
        if (config.emitCloseInviteDialog) {
            emit('closeInviteDialog');
        }
    }

    function cancelInviteConfirm() {
        isConfirmDialogVisible.value = false;
    }

    function cancelEditAndSendInvite() {
        editDialog.value.visible = false;
    }

    /**
     * Sends the slot and closes the dialogs. Invites close the picker right
     * away, responses once the response has been sent.
     *
     * @param {object} dialog
     * @param {number} slot
     */
    function sendSlot(dialog, slot) {
        const pending = sendInviteMessage(props.type, { slot, dialog, inviteDialog: props.inviteDialog });
        cancelInviteConfirm();
        if (config.closeAfterSend) {
            pending.finally(closeInviteDialog);
        } else {
            closeInviteDialog();
        }
    }

    function sendInviteConfirm() {
        const D = props.dialog;
        sendSlot(D, D.messageSlot.slot);
    }

    async function saveEditAndSendInvite() {
        const D = editDialog.value;
        const I = props.dialog;
        D.visible = false;
        const messageType = I.messageSlot.messageType;
        const slot = I.messageSlot.slot;
        if (I.messageSlot.message !== D.newMessage) {
            const params = {
                message: D.newMessage
            };
            let args;
            try {
                args = await inviteMessagesRequest.editInviteMessage(params, messageType, slot);
            } catch (err) {
                console.error(config.editFailedLog, err);
                if (config.editFailedToast) {
                    toast.error(t('message.error'));
                }
                return;
            }
            if (args.json[slot].message === I.messageSlot.message) {
                toast.error(t('message.invite.message_update_failed'));
                return;
            }
            toast(config.editUpdatedToast());
        }
        sendSlot(I, slot);
    }
</script>
