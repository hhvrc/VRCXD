<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Copy from '@lucide/svelte/icons/copy';
	import Download from '@lucide/svelte/icons/download';
	import RefreshCcw from '@lucide/svelte/icons/refresh-ccw';
	import RotateCcw from '@lucide/svelte/icons/rotate-ccw';
	import RotateCw from '@lucide/svelte/icons/rotate-cw';
	import X from '@lucide/svelte/icons/x';
	import ZoomIn from '@lucide/svelte/icons/zoom-in';
	import ZoomOut from '@lucide/svelte/icons/zoom-out';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { imageSrc } from '#lib/api/links.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';

	// The original's FullscreenImagePreview: copy, download, zoom (buttons, wheel at the pointer, + and -),
	// rotate (buttons, R), drag to pan, and reset (button, 0). Copy was upstream issue 57.
	let scale = $state(1);
	let rotate = $state(0);
	let tx = $state(0);
	let ty = $state(0);
	let viewer = $state<HTMLDivElement | undefined>();
	let drag: { x: number; y: number; tx: number; ty: number } | undefined;

	const open = $derived(!!imageViewer.url);
	const transform = $derived(`translate(${tx}px, ${ty}px) scale(${scale}) rotate(${rotate}deg)`);

	$effect(() => {
		void imageViewer.url;
		reset();
	});

	const clamp = (n: number, min: number, max: number) => Math.min(max, Math.max(min, n));

	function reset() {
		scale = 1;
		rotate = 0;
		tx = 0;
		ty = 0;
	}

	const zoom = (factor: number) => (scale = clamp(scale * factor, 0.1, 10));

	function zoomAtPointer(event: WheelEvent, factor: number) {
		if (!viewer) return;
		const rect = viewer.getBoundingClientRect();
		const mx = event.clientX - rect.left;
		const my = event.clientY - rect.top;
		const cx = rect.width / 2;
		const cy = rect.height / 2;
		const next = clamp(scale * factor, 0.1, 10);
		const r = (rotate * Math.PI) / 180;
		const cos = Math.cos(r);
		const sin = Math.sin(r);
		const vx = mx - cx - tx;
		const vy = my - cy - ty;
		const ux = (vx * cos + vy * sin) / scale;
		const uy = (-vx * sin + vy * cos) / scale;
		tx = mx - cx - (ux * cos - uy * sin) * next;
		ty = my - cy - (ux * sin + uy * cos) * next;
		scale = next;
	}

	function onWheel(event: WheelEvent) {
		event.preventDefault();
		zoomAtPointer(event, event.deltaY < 0 ? 1.1 : 1 / 1.1);
	}

	function onPointerDown(event: PointerEvent) {
		if (event.button !== 0) return;
		(event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
		drag = { x: event.clientX, y: event.clientY, tx, ty };
	}

	function onPointerMove(event: PointerEvent) {
		if (!drag) return;
		tx = drag.tx + event.clientX - drag.x;
		ty = drag.ty + event.clientY - drag.y;
	}

	function onPointerUp(event: PointerEvent) {
		if (!drag) return;
		drag = undefined;
		(event.currentTarget as HTMLElement).releasePointerCapture?.(event.pointerId);
	}

	function onKeydown(event: KeyboardEvent) {
		if (!open) return;
		if (event.key === '+' || event.key === '=') zoom(1.2);
		else if (event.key === '-' || event.key === '_') zoom(1 / 1.2);
		else if (event.key.toLowerCase() === 'r') rotate = (rotate + 90) % 360;
		else if (event.key === '0') reset();
	}

	/** The image as a PNG blob, fetched through the server's image proxy (the clipboard only takes PNG). */
	async function pngBlob(url: string): Promise<Blob> {
		const response = await fetch(imageSrc(url));
		if (!response.ok) throw new Error(`HTTP ${response.status}`);
		const blob = await response.blob();
		if (blob.type === 'image/png') return blob;
		const bitmap = await createImageBitmap(blob);
		const canvas = document.createElement('canvas');
		canvas.width = bitmap.width;
		canvas.height = bitmap.height;
		canvas.getContext('2d')?.drawImage(bitmap, 0, 0);
		return await new Promise<Blob>((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('Could not encode the image'))), 'image/png'));
	}

	async function copyImage() {
		const url = imageViewer.url;
		if (!url) return;
		const pending = toast.info(m.message_image_downloading());
		try {
			await navigator.clipboard.write([new ClipboardItem({ 'image/png': pngBlob(url) })]);
			toast.success(m.message_image_copied_to_clipboard());
		} catch {
			toast.error(`Failed to download image. ${url}`);
		} finally {
			toast.dismiss(pending);
		}
	}

	/** The original's file name: the one given, else the VRChat file id, else the last path part. */
	function downloadName(url: string): string {
		if (imageViewer.fileName) return imageViewer.fileName;
		const fileId = /file_[0-9A-Za-z-]+/.exec(url)?.[0];
		if (fileId) return `${fileId}.png`;
		const last = url.split('?')[0].split('/').pop();
		return last ? `${last}.png` : 'image.png';
	}

	async function downloadImage() {
		const url = imageViewer.url;
		if (!url) return;
		const pending = toast.info(m.message_image_downloading());
		try {
			const blob = await pngBlob(url);
			const href = URL.createObjectURL(blob);
			const link = document.createElement('a');
			link.href = href;
			link.download = downloadName(url);
			document.body.appendChild(link);
			link.click();
			link.remove();
			URL.revokeObjectURL(href);
		} catch {
			toast.error(`Failed to download image. ${url}`);
		} finally {
			toast.dismiss(pending);
		}
	}
</script>

<svelte:window onkeydown={onKeydown} />

<Dialog.Root bind:open={() => open, (value) => { if (!value) imageViewer.close(); }}>
	<Dialog.Content
		class="fixed inset-0 top-0 left-0 h-screen w-screen max-w-none translate-x-0 translate-y-0 rounded-none border-0 bg-background/80 p-6 shadow-none backdrop-blur-sm sm:max-w-none sm:p-10"
		showCloseButton={false}
		onclick={() => imageViewer.close()}
	>
		<Dialog.Title class="sr-only">{m.dialog_group_post_edit_image()}</Dialog.Title>
		<div bind:this={viewer} class="relative h-full w-full overflow-hidden select-none">
			<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
			<div class="absolute top-3 right-3 z-10 flex items-center gap-2 rounded-md border bg-background/70 px-2 py-1 backdrop-blur" onclick={(e) => e.stopPropagation()}>
				<Button variant="ghost" size="icon" class="size-8" aria-label={m.view_favorite_copy()} title={m.view_favorite_copy()} onclick={copyImage}><Copy class="size-4" /></Button>
				<Button variant="ghost" size="icon" class="size-8" aria-label={m.dialog_vrcx_updater_download()} title={m.dialog_vrcx_updater_download()} onclick={downloadImage}><Download class="size-4" /></Button>
				<div class="mx-1 h-5 w-px bg-border"></div>
				<Button variant="ghost" size="icon" class="size-8" aria-label="Zoom out" title="Zoom out" onclick={() => zoom(1 / 1.2)}><ZoomOut class="size-4" /></Button>
				<Button variant="ghost" size="icon" class="size-8" aria-label="Zoom in" title="Zoom in" onclick={() => zoom(1.2)}><ZoomIn class="size-4" /></Button>
				<Button variant="ghost" size="icon" class="size-8" aria-label="Rotate right" title="Rotate right" onclick={() => (rotate = (rotate + 90) % 360)}><RotateCw class="size-4" /></Button>
				<Button variant="ghost" size="icon" class="size-8" aria-label="Rotate left" title="Rotate left" onclick={() => (rotate = (rotate - 90 + 360) % 360)}><RotateCcw class="size-4" /></Button>
				<Button variant="ghost" size="icon" class="size-8" aria-label={m.view_settings_advanced_advanced_user_generated_content_reset_override()} title={m.view_settings_advanced_advanced_user_generated_content_reset_override()} onclick={reset}><RefreshCcw class="size-4" /></Button>
				<div class="mx-1 h-5 w-px bg-border"></div>
				<Button variant="ghost" size="icon" class="size-8" aria-label={m.dialog_change_log_close()} title={m.dialog_change_log_close()} onclick={() => imageViewer.close()}><X class="size-4" /></Button>
			</div>
			<div class="flex h-full w-full items-center justify-center" onwheel={onWheel}>
				{#if imageViewer.url}
					<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_noninteractive_element_interactions -->
					<img
						src={imageSrc(imageViewer.url)}
						alt=""
						class="max-h-full max-w-full cursor-grab will-change-transform"
						style="transform: {transform}; transform-origin: center center"
						draggable="false"
						onclick={(e) => e.stopPropagation()}
						onpointerdown={onPointerDown}
						onpointermove={onPointerMove}
						onpointerup={onPointerUp}
						onpointercancel={onPointerUp}
					/>
				{/if}
			</div>
		</div>
	</Dialog.Content>
</Dialog.Root>
