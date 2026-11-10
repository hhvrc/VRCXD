<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import FlipHorizontal from '@lucide/svelte/icons/flip-horizontal';
	import FlipVertical from '@lucide/svelte/icons/flip-vertical';
	import Expand from '@lucide/svelte/icons/expand';
	import Frame from '@lucide/svelte/icons/frame';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import RotateCcw from '@lucide/svelte/icons/rotate-ccw';
	import RotateCw from '@lucide/svelte/icons/rotate-cw';
	import ZoomIn from '@lucide/svelte/icons/zoom-in';
	import ZoomOut from '@lucide/svelte/icons/zoom-out';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';

	// The original's ImageCropDialog: rotate, flip, zoom, fit or free placement, and an auto resize
	// option that skips cropping. The crop frame stays still and the image moves under it.

	let {
		open = $bindable(false),
		title,
		aspectRatio,
		file,
		onconfirm
	}: { open?: boolean; title: string; aspectRatio: number; file: File | null; onconfirm: (blob: Blob) => Promise<void> } = $props();

	const AUTO_RESIZE_KEY = 'vrcx.imageCropAutoResize';
	const MIN_ZOOM = 0.3;
	const MAX_ZOOM = 5;

	let imageUrl = $state('');
	let image = $state<HTMLImageElement | null>(null);
	let boxWidth = $state(0);
	let boxHeight = $state(0);
	let zoom = $state(1);
	let panX = $state(0);
	let panY = $state(0);
	let angle = $state(0);
	let flipH = $state(false);
	let flipV = $state(false);
	let freeMode = $state(false);
	let loading = $state(false);
	let error = $state('');
	let autoResize = $state(readSetting<boolean>(AUTO_RESIZE_KEY, false) === true);
	let drag: { x: number; y: number; panX: number; panY: number } | null = null;


	function setAutoResize(value: boolean) {
		autoResize = value;
		storeSetting(AUTO_RESIZE_KEY, value);
	}

	$effect(() => {
		if (!file) return;
		const url = URL.createObjectURL(file);
		const loaded = new Image();
		loaded.onload = () => (image = loaded);
		loaded.src = url;
		imageUrl = url;
		reset();
		return () => {
			URL.revokeObjectURL(url);
			image = null;
			imageUrl = '';
		};
	});

	function reset() {
		zoom = 1;
		panX = 0;
		panY = 0;
		angle = 0;
		flipH = false;
		flipV = false;
		freeMode = false;
		error = '';
	}

	const turned = $derived(angle % 180 !== 0);
	const sourceWidth = $derived(image?.naturalWidth ?? 1);
	const sourceHeight = $derived(image?.naturalHeight ?? 1);
	const shownWidth = $derived(turned ? sourceHeight : sourceWidth);
	const shownHeight = $derived(turned ? sourceWidth : sourceHeight);
	const frameWidth = $derived(Math.max(1, Math.min(boxWidth - 40, (boxHeight - 40) * aspectRatio)));
	const frameHeight = $derived(frameWidth / aspectRatio);
	// The scale at which the image exactly covers the frame.
	const coverScale = $derived(Math.max(frameWidth / shownWidth, frameHeight / shownHeight));
	const scale = $derived(coverScale * zoom);
	const sliderValue = $derived(Math.round(((Math.log(zoom) - Math.log(MIN_ZOOM)) / (Math.log(MAX_ZOOM) - Math.log(MIN_ZOOM))) * 100));

	function clamp() {
		if (freeMode) return;
		zoom = Math.max(1, zoom);
		const maxX = Math.max(0, (shownWidth * coverScale * zoom - frameWidth) / 2);
		const maxY = Math.max(0, (shownHeight * coverScale * zoom - frameHeight) / 2);
		panX = Math.min(maxX, Math.max(-maxX, panX));
		panY = Math.min(maxY, Math.max(-maxY, panY));
	}

	function setZoom(value: number) {
		zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, value));
		clamp();
	}

	function rotate(by: number) {
		angle = (angle + by + 360) % 360;
		clamp();
	}

	function toggleMode() {
		freeMode = !freeMode;
		clamp();
	}

	function onPointerDown(e: PointerEvent) {
		if (loading) return;
		(e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
		drag = { x: e.clientX, y: e.clientY, panX, panY };
	}

	function onPointerMove(e: PointerEvent) {
		if (!drag) return;
		panX = drag.panX + e.clientX - drag.x;
		panY = drag.panY + e.clientY - drag.y;
		clamp();
	}

	function onWheel(e: WheelEvent) {
		e.preventDefault();
		if (!loading) setZoom(zoom * (e.deltaY < 0 ? 1.1 : 0.9));
	}

	function cropped(): Promise<Blob> {
		const untouched = zoom === 1 && panX === 0 && panY === 0 && angle === 0 && !flipH && !flipV;
		if (untouched && file && Math.abs(sourceWidth / sourceHeight - aspectRatio) < 0.01) return Promise.resolve(file);
		const canvas = document.createElement('canvas');
		canvas.width = Math.round(frameWidth / scale);
		canvas.height = Math.round(frameHeight / scale);
		const context = canvas.getContext('2d')!;
		context.translate(canvas.width / 2 + panX / scale, canvas.height / 2 + panY / scale);
		context.rotate((angle * Math.PI) / 180);
		context.scale(flipH ? -1 : 1, flipV ? -1 : 1);
		context.drawImage(image!, -sourceWidth / 2, -sourceHeight / 2);
		return new Promise((resolve, reject) => canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Could not encode the image'))), 'image/png'));
	}

	async function confirm() {
		if (!file) return;
		loading = true;
		error = '';
		try {
			await onconfirm(autoResize ? file : await cropped());
			open = false;
		} catch (err) {
			error = (err as Error).message;
		} finally {
			loading = false;
		}
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-212.5">
		<Dialog.Header>
			<Dialog.Title>{title}</Dialog.Title>
		</Dialog.Header>
		{#if imageUrl && autoResize}
			<img src={imageUrl} alt="" class="mt-4 h-100 max-h-full w-full object-contain" />
		{:else if imageUrl}
			<div
				class="relative mt-4 h-100 cursor-move touch-none overflow-hidden rounded bg-black select-none"
				role="presentation"
				bind:clientWidth={boxWidth}
				bind:clientHeight={boxHeight}
				onpointerdown={onPointerDown}
				onpointermove={onPointerMove}
				onpointerup={() => (drag = null)}
				onwheel={onWheel}
			>
				{#if image}
					<img
						src={imageUrl}
						alt=""
						draggable="false"
						class="pointer-events-none absolute top-1/2 left-1/2 max-w-none"
						style="width:{sourceWidth}px;height:{sourceHeight}px;transform:translate(-50%,-50%) translate({panX}px,{panY}px) rotate({angle}deg) scale({flipH ? -scale : scale},{flipV ? -scale : scale})"
					/>
					<div class="pointer-events-none absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2" style="width:{frameWidth}px;height:{frameHeight}px;border:1px solid #fff;box-shadow:0 0 0 9999px rgba(0,0,0,0.6)"></div>
				{/if}
			</div>
			<div class="mt-3 flex items-center justify-center gap-1">
				<Button size="icon-sm" variant="outline" class="size-8 rounded-full" disabled={loading} title="Rotate left" aria-label="Rotate left" onclick={() => rotate(-90)}><RotateCcw class="size-4" /></Button>
				<Button size="icon-sm" variant="outline" class="size-8 rounded-full" disabled={loading} title="Rotate right" aria-label="Rotate right" onclick={() => rotate(90)}><RotateCw class="size-4" /></Button>
				<div class="mx-1 h-5 w-px bg-border"></div>
				<Button size="icon-sm" variant="outline" class="size-8 rounded-full" disabled={loading} title="Flip horizontal" aria-label="Flip horizontal" onclick={() => (flipH = !flipH)}><FlipHorizontal class="size-4" /></Button>
				<Button size="icon-sm" variant="outline" class="size-8 rounded-full" disabled={loading} title="Flip vertical" aria-label="Flip vertical" onclick={() => (flipV = !flipV)}><FlipVertical class="size-4" /></Button>
				<div class="mx-1 h-5 w-px bg-border"></div>
				<Button size="icon-sm" variant="ghost" class="size-7 rounded-full" disabled={loading} title="Zoom out" aria-label="Zoom out" onclick={() => setZoom(zoom * 0.8)}><ZoomOut class="size-3.5" /></Button>
				<input
					type="range"
					min="0"
					max="100"
					step="1"
					class="w-28 accent-primary"
					aria-label={m.view_settings_appearance_appearance_zoom()}
					disabled={loading}
					value={sliderValue}
					oninput={(e) => setZoom(Math.exp(Math.log(MIN_ZOOM) + (Number(e.currentTarget.value) / 100) * (Math.log(MAX_ZOOM) - Math.log(MIN_ZOOM))))}
				/>
				<Button size="icon-sm" variant="ghost" class="size-7 rounded-full" disabled={loading} title="Zoom in" aria-label="Zoom in" onclick={() => setZoom(zoom * 1.2)}><ZoomIn class="size-3.5" /></Button>
				<div class="mx-1 h-5 w-px bg-border"></div>
				<Button size="icon-sm" variant={freeMode ? 'default' : 'outline'} class="size-8 rounded-full" disabled={loading} title={freeMode ? 'Fit mode' : 'Free mode'} aria-label={freeMode ? 'Fit mode' : 'Free mode'} onclick={toggleMode}>
					{#if freeMode}<Expand class="size-4" />{:else}<Frame class="size-4" />{/if}
				</Button>
				<Button size="icon-sm" variant="outline" class="size-8 rounded-full" disabled={loading} title={m.dialog_image_crop_reset()} aria-label={m.dialog_image_crop_reset()} onclick={reset}><RefreshCw class="size-4" /></Button>
			</div>
		{/if}
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		<Dialog.Footer class="items-center">
			<label class="mr-auto inline-flex items-center gap-2 text-sm">
				<input type="checkbox" class="accent-primary" checked={autoResize} disabled={loading} onchange={(e) => setAutoResize(e.currentTarget.checked)} />
				Auto resize
			</label>
			<Button variant="secondary" size="sm" disabled={loading} onclick={() => (open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button size="sm" disabled={loading} onclick={confirm}>
				{#if loading}<Spinner />Uploading{:else}Upload{/if}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
