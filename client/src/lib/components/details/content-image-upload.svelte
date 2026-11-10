<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { api } from '#lib/api/client.js';
	import ImageCropDialog from '#lib/components/tools/image-crop-dialog.svelte';
	import { fitImage, MAX_INPUT_BYTES } from '#lib/content/image-upload.js';

	// The original's Change Image for avatars and worlds: pick a file, crop it to 4:3, fit it to
	// VRChat's limits, and the server runs the file and signature upload (uploadImageLegacy).
	let { path, imageUrl, title, onuploaded }: { path: string; imageUrl: string; title: string; onuploaded: () => void } = $props();

	let input = $state<HTMLInputElement | null>(null);
	let file = $state<File | null>(null);
	let open = $state(false);

	export function pick() {
		input?.click();
	}

	function chosen(e: Event) {
		const target = e.currentTarget as HTMLInputElement;
		const next = target.files?.[0];
		target.value = '';
		if (!next) return;
		if (next.size >= MAX_INPUT_BYTES) {
			toast.error(m.message_file_too_large());
			return;
		}
		if (!/image.*/.test(next.type)) {
			toast.error("File isn't an image");
			return;
		}
		file = next;
		open = true;
	}

	async function upload(blob: Blob) {
		try {
			const form = new FormData();
			form.append('image', await fitImage(blob, false), 'image.png');
			form.append('imageUrl', imageUrl);
			await Promise.race([api.upload(`${path}/image`, form), new Promise((_, reject) => setTimeout(() => reject(new Error('Upload timed out')), 30_000))]);
			toast.success(m.message_upload_success());
			onuploaded();
		} catch (err) {
			console.error('Image upload process failed:', err);
			toast.error(m.message_upload_error());
		}
	}
</script>

<input bind:this={input} type="file" accept="image/*" class="hidden" onchange={chosen} />
<ImageCropDialog bind:open {title} aspectRatio={4 / 3} {file} onconfirm={upload} />
