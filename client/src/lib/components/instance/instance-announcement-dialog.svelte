<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InstanceAnnouncementDialog: a message, and a gallery photo or else the group's banner.
	let { open = $bindable(false), location }: { open?: boolean; location: string } = $props();

	let message = $state('');
	let image = $state({ fileId: '', imageUrl: '' });
	let galleryOpen = $state(false);
	let loading = $state(false);

	const canSubmit = $derived(!loading && message.trim().length > 0);

	$effect(() => {
		if (open) {
			message = '';
			image = { fileId: '', imageUrl: '' };
		}
	});

	function setOpen(value: boolean) {
		if (!loading) open = value;
	}

	async function send() {
		if (!canSubmit) return;
		loading = true;
		try {
			await api.post(ownPath('/instances/announcement'), { location, message: message.trim(), imageUrl: image.imageUrl || null });
			toast.success(m.message_instance_announcement_sent());
			open = false;
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}
</script>

<Dialog.Root bind:open={() => open, setOpen}>
	<Dialog.Content class="sm:max-w-[650px]">
		<Dialog.Header><Dialog.Title>{m.dialog_instance_announcement_header()}</Dialog.Title></Dialog.Header>
		<div class="flex flex-col gap-4">
			<label class="flex flex-col gap-1.5 text-sm">
				<span class="font-medium">{m.dialog_instance_announcement_message()}</span>
				<Textarea rows={4} class="resize-none" bind:value={message} disabled={loading} />
			</label>
			<div class="flex flex-col gap-1.5 text-sm">
				<span class="font-medium">{m.dialog_instance_announcement_image()}</span>
				{#if image.fileId}
					<div class="flex items-start gap-2">
						<img src={imageSrc(image.imageUrl)} class="size-16 rounded-md object-cover" alt="" loading="lazy" />
						<Button size="sm" variant="outline" disabled={loading} onclick={() => (image = { fileId: '', imageUrl: '' })}>{m.dialog_instance_announcement_clear_image()}</Button>
					</div>
				{:else}
					<div><Button size="sm" variant="outline" disabled={loading} onclick={() => (galleryOpen = true)}>{m.dialog_instance_announcement_select_image()}</Button></div>
				{/if}
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" disabled={loading} onclick={() => (open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button disabled={!canSubmit} onclick={send}>{loading ? 'Sending...' : 'Send Announcement'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<GallerySelectDialog bind:open={galleryOpen} onselect={(picked) => (image = picked)} />
