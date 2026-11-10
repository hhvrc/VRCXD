<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ChevronLeft from '@lucide/svelte/icons/chevron-left';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import Upload from '@lucide/svelte/icons/upload';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import ImageCropDialog from '#lib/components/tools/image-crop-dialog.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { SECTION, PANEL } from '#lib/details.js';
	import { fitImage, MAX_INPUT_BYTES } from '#lib/content/image-upload.js';

	// The original's avatar gallery (AvatarDialog.vue and avatarStore.getAvatarGallery): the images,
	// and for the avatar's author upload, move left or right, and delete.
	let { path, filesPath, isOwner }: { path: string; filesPath: string; isOwner: boolean } = $props();

	type GalleryFile = { id: string; order?: number | null; versions?: { file?: { url?: string } }[] };
	type Image = { id: string; url: string };

	let images = $state<Image[]>([]);
	let index = $state(0);
	let loading = $state(false);
	let input = $state<HTMLInputElement | null>(null);
	let cropFile = $state<File | null>(null);
	let cropOpen = $state(false);
	let confirming = $state<Confirming | undefined>();

	async function getAvatarGallery(forPath = path) {
		try {
			const files = await api.get<GalleryFile[]>(`${forPath}/gallery`);
			if (forPath !== path) return images;
			// The original sorts by order only when both have one.
			const sorted = [...files].sort((a, b) => (!a.order && !b.order ? 0 : (a.order ?? 0) - (b.order ?? 0)));
			images = sorted.map((file) => ({ id: file.id, url: file.versions?.[file.versions.length - 1]?.file?.url ?? '' }));
		} catch (err) {
			console.error(err);
		} finally {
			loading = false;
		}
		return images;
	}

	$effect(() => {
		const forPath = path;
		untrack(() => {
			images = [];
			index = 0;
			getAvatarGallery(forPath);
		});
	});

	function scrollTo(imageId: string | undefined) {
		const at = images.findIndex((image) => image.id === imageId);
		index = at === -1 ? Math.max(images.length - 1, 0) : at;
	}

	function chosen(e: Event) {
		const target = e.currentTarget as HTMLInputElement;
		const file = target.files?.[0];
		target.value = '';
		if (!file) return;
		if (file.size >= MAX_INPUT_BYTES) {
			toast.error(m.message_file_too_large());
			return;
		}
		if (!/image.*/.test(file.type)) {
			toast.error("File isn't an image");
			return;
		}
		cropFile = file;
		cropOpen = true;
	}

	async function upload(blob: Blob) {
		loading = true;
		const uploading = (async () => {
			const form = new FormData();
			form.append('file', await fitImage(blob, false), 'blob');
			const created = await api.upload<{ id: string }>(`${path}/gallery`, form);
			await getAvatarGallery();
			return created;
		})();
		toast.promise(uploading, { loading: 'Uploading', success: 'Upload completed', error: 'Upload failed' });
		try {
			scrollTo((await uploading)?.id);
		} catch (err) {
			console.error('Failed to upload image', err);
		} finally {
			loading = false;
		}
	}

	async function move(at: number, direction: number) {
		const target = at + direction;
		if (target < 0 || target >= images.length) return;
		const next = [...images];
		[next[at], next[target]] = [next[target], next[at]];
		loading = true;
		try {
			await api.put(`${path}/gallery/order`, { ids: next.map((image) => image.id) });
		} catch (err) {
			console.error('Failed to reorder gallery', err);
			toast.error(m.message_upload_error());
		}
		await getAvatarGallery();
		scrollTo(next[target].id);
	}

	function remove(image: Image) {
		confirming = {
			command: 'Delete Image',
			run: async () => {
				confirming = undefined;
				loading = true;
				try {
					await api.delete(`${filesPath}/${encodeURIComponent(image.id)}`);
				} catch (err) {
					console.error('Failed to delete gallery image', err);
				}
				await getAvatarGallery();
				index = Math.min(index, Math.max(images.length - 1, 0));
			}
		};
	}
</script>

{#if images.length || isOwner}
	<div class="flex h-full min-h-0 flex-wrap content-start items-start overflow-y-auto {PANEL}" style="max-height: unset">
		<div class="w-full">
			<div class="{SECTION} mb-2 flex justify-between border-b border-border pb-2">
				<span>{m.dialog_avatar_info_gallery()}</span>
				{#if isOwner}
					<Button size="sm" variant="outline" class="h-6" disabled={loading} aria-label={m.dialog_gallery_icons_upload()} onclick={() => input?.click()}>
						<span class="text-3xs">{m.dialog_gallery_icons_upload()}</span>
						<Upload class="text-3xs" />
					</Button>
				{/if}
			</div>
			<input bind:this={input} type="file" accept="image/*" class="hidden" onchange={chosen} />

			<div class="mt-2 ml-20 w-[80%]">
				{#if images.length}
					{@const image = images[Math.min(index, images.length - 1)]}
					<div class="relative flex w-full items-center gap-2">
						<Button size="icon-sm" variant="outline" class="absolute top-1/2 -left-12 -translate-y-1/2 rounded-full" disabled={index === 0} aria-label={m.view_player_list_photon_previous()} onclick={() => (index -= 1)}><ChevronLeft /></Button>
						<div class="relative h-50 w-full">
							<ViewableImage src={image.url} class="size-full" imageClass="size-full object-contain" />
							{#if isOwner}
								<div class="absolute top-1 right-1 flex gap-1">
									<Button size="icon-sm" variant="outline" disabled={loading || index === 0} aria-label={m.dialog_avatar_info_gallery_move_left()} onclick={() => move(index, -1)}><ChevronLeft /></Button>
									<Button size="icon-sm" variant="outline" disabled={loading || index === images.length - 1} aria-label={m.dialog_avatar_info_gallery_move_right()} onclick={() => move(index, 1)}><ChevronRight /></Button>
									<Button size="icon-sm" variant="outline" disabled={loading} aria-label={m.dialog_avatar_info_gallery_delete()} onclick={() => remove(image)}><Trash2 /></Button>
								</div>
							{/if}
						</div>
						<Button size="icon-sm" variant="outline" class="absolute top-1/2 -right-12 -translate-y-1/2 rounded-full" disabled={index >= images.length - 1} aria-label={m.table_pagination_next()} onclick={() => (index += 1)}><ChevronRight /></Button>
					</div>
					<div class="mt-1 text-center text-xs text-muted-foreground">{index + 1}/{images.length}</div>
				{:else}
					<DataTableEmpty type="nodata" />
				{/if}
			</div>
		</div>
	</div>
{/if}

<ImageCropDialog bind:open={cropOpen} title={m.dialog_avatar_info_gallery()} aspectRatio={4 / 3} file={cropFile} onconfirm={upload} />
<ActionDialogs bind:confirming busy={loading} />
