<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Upload from '@lucide/svelte/icons/upload';
	import X from '@lucide/svelte/icons/x';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { fitImage } from '#lib/content/image-upload.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GallerySelectDialog: pick one of the account's gallery photos, or with icon set
	// one of its VRC+ icons, or None; VRC+ supporters can upload one from here.
	type UserFile = { id: string; url: string | null };
	type GalleryInfo = { isSupporter: boolean; limits: { gallery: number; icons: number } };

	let { open = $bindable(false), icon = false, onselect }: { open?: boolean; icon?: boolean; onselect: (image: { imageUrl: string; fileId: string }) => void } = $props();

	let images = $state<UserFile[]>([]);
	let info = $state<GalleryInfo | undefined>();
	let input = $state<HTMLInputElement | undefined>();

	const tag = $derived(icon ? 'icon' : 'gallery');
	const limit = $derived(info ? (icon ? info.limits.icons : info.limits.gallery) : '');
	const path = ownPath;

	async function refreshTable() {
		const current = tag;
		try {
			const files = await api.get<UserFile[]>(path(`/gallery/files/${current}`));
			if (current === tag) images = files;
		} catch (err) {
			toastError(err);
		}
	}

	$effect(() => {
		if (!open) return;
		untrack(() => {
			images = [];
			refreshTable();
			api.get<GalleryInfo>(path('/gallery/info')).then((i) => (info = i), () => {});
		});
	});

	function select(imageUrl: string, fileId: string) {
		onselect({ imageUrl, fileId });
		open = false;
	}

	async function onFileChange(e: Event) {
		const target = e.currentTarget as HTMLInputElement;
		const file = target.files?.[0];
		target.value = '';
		if (!file) return;
		if (file.size >= 100000000) {
			toast.error(m.message_file_too_large());
			return;
		}
		if (!file.type.match(/image.*/)) {
			toast.error("File isn't an image");
			return;
		}
		const uploadTag = tag;
		try {
			const form = new FormData();
			form.append('file', await fitImage(file, uploadTag === 'icon'), 'blob');
			form.append('tag', uploadTag);
			const created = await api.upload<UserFile>(path('/gallery/files'), form);
			toast.success(m.message_gallery_uploaded());
			if (uploadTag === tag && images.length !== 0) images = [created, ...images];
		} catch (err) {
			toastError(err);
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="w-full bg-background sm:max-w-none">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_gallery_select_header()}</Dialog.Title>
		</Dialog.Header>

		<div class="max-h-[70vh] overflow-y-auto">
			<span>{m.dialog_gallery_select_gallery()}</span>
			<span class="ml-1.5 text-xs text-muted-foreground">{images.length}/{limit}</span>
			<br />
			<input bind:this={input} type="file" accept="image/*" style="display: none" onchange={onFileChange} />
			<div class="mt-1 inline-flex [&>*:not(:first-child)]:rounded-l-none [&>*:not(:first-child)]:border-l-0 [&>*:not(:last-child)]:rounded-r-none">
				<Button variant="outline" size="sm" onclick={() => select('', '')}><X /> {m.dialog_gallery_select_none()}</Button>
				<Button variant="outline" size="sm" onclick={refreshTable}><RefreshCw /> {m.dialog_gallery_select_refresh()}</Button>
				<Button variant="outline" size="sm" disabled={!info?.isSupporter} onclick={() => input?.click()}><Upload /> {m.dialog_gallery_select_upload()}</Button>
			</div>
			<br />
			{#each images as image (image.id)}
				{#if image.url}
					{@const url = image.url}
					<div class="mt-2.5 box-border inline-block cursor-default">
						<button type="button" class="mr-5 h-[200px] w-[200px] cursor-pointer overflow-hidden rounded-[20px]" onclick={() => select(url, image.id)}>
							<img src={imageSrc(url)} alt="" class="h-full w-full rounded-[15px] object-cover" loading="lazy" />
						</button>
					</div>
				{/if}
			{/each}
		</div>
	</Dialog.Content>
</Dialog.Root>
