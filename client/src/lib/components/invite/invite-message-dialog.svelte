<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { untrack } from 'svelte';
	import SquarePen from '@lucide/svelte/icons/square-pen';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { timeToText } from '#lib/helpers/format.js';
	import { fitImage, MAX_INPUT_BYTES } from '#lib/content/image-upload.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import type { InviteMessage } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { toast } from 'svelte-sonner';

	// The original's SendInviteDialog, SendInviteRequestDialog, SendInviteResponseDialog and
	// SendInviteRequestResponseDialog with their confirm and edit steps: pick a saved message,
	// optionally reword it, and send it. VRC+ supporters can attach a picture, as the original allows
	// on invites, invite requests and invite request answers.
	export type MessageType = 'message' | 'request' | 'response' | 'requestResponse';

	let {
		open = $bindable(false),
		type,
		send
	}: {
		open?: boolean;
		type: MessageType;
		/** Sends the chosen slot, with the picture when one is attached. */
		send: (slot: number, photo: Blob | null) => Promise<unknown>;
	} = $props();

	const COOL_DOWN_MS = 60 * 60 * 1000;
	const HEADERS: Record<MessageType, string> = {
		message: 'Send Invite Message',
		request: 'Send Invite Request Message',
		response: 'Send Invite Response Message',
		requestResponse: 'Send Invite Request Response Message'
	};
	const EDIT_HEADERS: Record<MessageType, string> = {
		message: 'Edit and Send Invite Message',
		request: 'Edit and Send Invite Message',
		response: 'Edit and Send Invite Response Message',
		requestResponse: 'Edit and Send Invite Response Message'
	};

	// The original's success messages, per kind and with or without a picture.
	const SENT: Record<MessageType, [string, string]> = {
		message: ['Invite message sent', 'Invite photo message sent'],
		request: ['Request invite message sent', 'Request invite photo message sent'],
		response: ['Invite response message sent', 'Invite response photo message sent'],
		requestResponse: ['Invite response message sent', 'Invite response photo message sent']
	};

	let messages = $state.raw<InviteMessage[]>([]);
	let error = $state('');
	let busy = $state(false);
	let confirming = $state<InviteMessage | undefined>();
	let editing = $state<InviteMessage | undefined>();
	let draft = $state('');
	const now = $derived(clock(1000).now);
	let photo = $state<Blob | null>(null);
	let photoUrl = $state('');
	let fileInput = $state<HTMLInputElement | undefined>();

	const path = $derived(ownPath(`/invite-messages/${type}`));
	// VRC+ supporters can attach a picture to every kind, invite answers included, as the original.
	const canAttach = $derived(!!sidebarData.me?.isVrcPlus);

	function coolDown(message: InviteMessage): string {
		const left = message.updatedAt ? Date.parse(message.updatedAt) + COOL_DOWN_MS - now : 0;
		return left > 0 ? timeToText(left, true) : '';
	}

	async function refresh() {
		error = '';
		try {
			messages = await api.get<InviteMessage[]>(path);
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	function clearPhoto() {
		photo = null;
		if (photoUrl) URL.revokeObjectURL(photoUrl);
		photoUrl = '';
		if (fileInput) fileInput.value = '';
	}

	async function pickPhoto(event: Event) {
		const file = (event.currentTarget as HTMLInputElement).files?.[0];
		error = '';
		if (!file) return clearPhoto();
		if (file.size >= MAX_INPUT_BYTES) {
			clearPhoto();
			error = 'File size too large';
			return;
		}
		if (!file.type.match(/image.*/)) {
			clearPhoto();
			error = "File isn't an image";
			return;
		}
		try {
			const fitted = await fitImage(file, false);
			clearPhoto();
			photo = fitted;
			photoUrl = URL.createObjectURL(fitted);
		} catch (err) {
			clearPhoto();
			error = err instanceof Error ? err.message : String(err);
		}
	}

	async function sendSlot(slot: number) {
		busy = true;
		error = '';
		try {
			const attached = canAttach ? photo : null;
			await send(slot, attached);
			toast.success(SENT[type][attached ? 1 : 0]);
			confirming = undefined;
			editing = undefined;
			open = false;
			clearPhoto();
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			busy = false;
		}
	}

	async function saveAndSend() {
		const message = editing;
		if (!message) return;
		if (message.message !== draft) {
			busy = true;
			try {
				const updated = await api.put<InviteMessage[]>(`${path}/${message.slot}`, { message: draft });
				if (updated[message.slot]?.message === message.message) {
					error = "VRChat API didn't update message, try again";
					toast.error(error);
					return;
				}
				toast(m.message_invite_message_updated());
			} catch (err) {
				error = ApiError.from(err).message;
				return;
			} finally {
				busy = false;
			}
		}
		await sendSlot(message.slot);
	}

	$effect(() => {
		if (!open) return;
		untrack(refresh);
		if (!sidebarData.me) untrack(() => sidebarData.loadMe());
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="bg-background sm:max-w-200">
		<Dialog.Header><Dialog.Title>{HEADERS[type]}</Dialog.Title></Dialog.Header>
		{#if canAttach}
			<div class="flex items-center gap-2">
				<input bind:this={fileInput} type="file" accept="image/*" class="text-sm" onchange={pickPhoto} />
				{#if photo}
					<Button size="sm" variant="secondary" onclick={clearPhoto}>{m.dialog_invite_message_clear_selected_image()}</Button>
				{/if}
			</div>
			{#if photoUrl}<img src={photoUrl} alt="" class="mt-1 max-h-24 w-fit rounded" />{/if}
		{/if}
		{#if error}<p class="text-sm text-destructive">{error}</p>{/if}
		<Table.Root class="mt-2">
			<Table.Header>
				<Table.Row>
					<Table.Head class="w-17.5">{m.table_profile_invite_messages_slot()}</Table.Head>
					<Table.Head>{m.dialog_group_post_edit_message()}</Table.Head>
					<Table.Head class="w-27.5 text-right">{m.table_profile_invite_messages_cool_down()}</Table.Head>
					<Table.Head class="w-17.5 text-right">{m.dialog_registry_backup_action()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each messages as message (message.slot)}
					<Table.Row class="cursor-pointer" onclick={() => (confirming = message)}>
						<Table.Cell>{message.slot}</Table.Cell>
						<Table.Cell class="whitespace-normal">{message.message}</Table.Cell>
						<Table.Cell class="text-right">{coolDown(message)}</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								size="icon-sm"
								variant="ghost"
								aria-label="Edit"
								onclick={(event) => {
									event.stopPropagation();
									draft = message.message;
									editing = message;
								}}
							>
								<SquarePen />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (open = false)}>{m.dialog_invite_message_cancel()}</Button>
			<Button onclick={refresh}>{m.dialog_invite_message_refresh()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => !!confirming, (value) => { if (!value) confirming = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-125" showCloseButton={false}>
		<Dialog.Header><Dialog.Title>{HEADERS[type]}</Dialog.Title></Dialog.Header>
		<span class="text-sm">{m.dialog_invite_message_confirmation()}</span>
		{#if error}<p class="text-sm text-destructive">{error}</p>{/if}
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (confirming = undefined)}>{m.dialog_invite_message_cancel()}</Button>
			<Button disabled={busy} onclick={() => confirming && sendSlot(confirming.slot)}>{m.dialog_invite_message_confirm()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => !!editing, (value) => { if (!value) editing = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-125" showCloseButton={false}>
		<Dialog.Header><Dialog.Title>{EDIT_HEADERS[type]}</Dialog.Title></Dialog.Header>
		<span class="text-sm">{m.dialog_edit_invite_message_description()}</span>
		<textarea bind:value={draft} rows="2" maxlength="64" class="mt-2 w-full resize-none rounded-md border border-input bg-background px-3 py-2 text-sm"></textarea>
		{#if error}<p class="text-sm text-destructive">{error}</p>{/if}
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (editing = undefined)}>{m.dialog_invite_message_cancel()}</Button>
			<Button disabled={busy || !draft} onclick={saveAndSend}>{m.dialog_boop_dialog_send()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
