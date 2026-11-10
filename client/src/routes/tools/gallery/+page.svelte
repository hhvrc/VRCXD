<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { browser } from '$app/env';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { imageSrc, userHref, worldHref } from '#lib/api/links.js';
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import Check from '@lucide/svelte/icons/check';
	import Gift from '@lucide/svelte/icons/gift';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Star from '@lucide/svelte/icons/star';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import Upload from '@lucide/svelte/icons/upload';
	import X from '@lucide/svelte/icons/x';
	import { onMount } from 'svelte';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { page } from '$app/state';
	import ImageCropDialog from '#lib/components/tools/image-crop-dialog.svelte';
	import EmojiImage from '#lib/components/tools/emoji-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { EMOJI_STYLE_URL, EMOJI_STYLES, fitImage, fitPrint, MAX_INPUT_BYTES } from '#lib/content/image-upload.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Gallery.vue: photos, icons, emojis, stickers, prints and inventory.

	type UserFile = { id: string; name: string | null; animationStyle: string | null; frames: number | null; framesOverTime: number | null; loopStyle: string | null; url: string | null };
	type Print = { id: string; authorId: string | null; authorName: string | null; note: string | null; worldId: string | null; worldName: string | null; createdAt: string | null; timestamp: string | null; files: { image: string | null } | null };
	type InventoryItem = { id: string; name: string | null; description: string | null; imageUrl: string | null; itemType: string | null; itemTypeLabel: string | null; created_at: string | null };
	type GalleryInfo = { limits: { gallery: number; icons: number; emoji: number; stickers: number; prints: number }; isSupporter: boolean; userIcon: string | null; currentAvatarImageUrl: string | null };
	type FileTab = 'gallery' | 'icon' | 'emoji' | 'sticker';

	const path = ownPath;

	let activeTab = $state((browser && page.url.searchParams.get('tab')) || 'gallery');
	let info = $state<GalleryInfo | null>(null);
	let files = $state<Record<FileTab, UserFile[]>>({ gallery: [], icon: [], emoji: [], sticker: [] });
	let prints = $state<Print[]>([]);
	let favoritePrints = $state<string[]>([]);
	let inventory = $state<InventoryItem[]>([]);
	let inventoryFilter = $state('all');
	let message = $state('');
	let pendingUploads = $state(0);

	let emojiAnimated = $state(false);
	let emojiStyle = $state('Stop');
	let emojiFps = $state(15);
	let emojiFrames = $state(4);
	let emojiPingPong = $state(false);
	let printNote = $state('');
	let printCropBorder = $state(true);

	let cropOpen = $state(false);
	let cropFile = $state<File | null>(null);
	let cropAspect = $state(4 / 3);
	let cropUpload: (blob: Blob) => Promise<void> = async () => {};

	let redeemOpen = $state(false);
	let redeemCode = $state('');

	const isUploading = $derived(pendingUploads > 0);
	const canUpload = $derived(Boolean(info?.isSupporter) && !isUploading);
	const inventoryTypes = $derived([...new Set(inventory.map((i) => i.itemTypeLabel ?? ''))].filter(Boolean).sort((a, b) => a.localeCompare(b)));
	const filteredInventory = $derived(inventoryFilter === 'all' ? inventory : inventory.filter((i) => i.itemTypeLabel === inventoryFilter));

	const fileId = (value: string | null | undefined) => /file_[0-9A-Za-z-]+/.exec(value ?? '')?.[0] ?? '';
	const fail = (err: unknown) => (message = ApiError.from(err).message);

	async function refreshFiles(tag: FileTab) {
		try {
			files[tag] = await api.get<UserFile[]>(path(`/gallery/files/${tag}`));
		} catch (err) {
			fail(err);
		}
	}

	async function refreshPrints() {
		try {
			const result = await api.get<{ prints: Print[]; favorites: string[] }>(path('/gallery/prints'));
			prints = result.prints;
			favoritePrints = result.favorites;
		} catch (err) {
			fail(err);
		}
	}

	async function refreshInventory() {
		inventory = [];
		try {
			inventory = await api.get<InventoryItem[]>(path('/inventory'));
		} catch (err) {
			fail(err);
		}
	}

	// The original's websocket content-refresh: VRChat says a kind of content changed, so the open
	// gallery reloads that tab (stickers and the print the game just created are left alone, as there).
	onMount(() =>
		onLiveEvent((event) => {
			if (event.event.case !== 'contentRefresh' || event.event.value.accountId !== vrchatAuth.ownerUserId) return;
			const { contentType, actionType } = event.event.value;
			if (contentType === 'icon' || contentType === 'gallery' || contentType === 'emoji') void refreshFiles(contentType);
			else if (contentType === 'print' && actionType !== 'created') void refreshPrints();
			else if (contentType === 'inventory') void refreshInventory();
		})
	);

	onMount(async () => {
		if (!vrchatAuth.ownerUserId) {
			message = 'Log in to VRChat first.';
			return;
		}
		try {
			info = await api.get<GalleryInfo>(path('/gallery/info'));
		} catch (err) {
			fail(err);
		}
		refreshFiles('gallery');
		refreshFiles('icon');
		refreshFiles('emoji');
		refreshFiles('sticker');
		refreshPrints();
		refreshInventory();
	});

	function fileForm(blob: Blob, tag: string, extra: Record<string, string | number> = {}): FormData {
		const form = new FormData();
		form.append('file', blob, 'blob');
		form.append('tag', tag);
		for (const [key, value] of Object.entries(extra)) form.append(key, String(value));
		return form;
	}

	async function uploadFile(tab: FileTab, blob: Blob, square: boolean, tag: string, extra: Record<string, string | number> = {}) {
		const created = await api.upload<UserFile>(path('/gallery/files'), fileForm(await fitImage(blob, square), tag, extra));
		files[tab] = [created, ...files[tab]];
	}

	const uploadGallery = (blob: Blob) => uploadFile('gallery', blob, false, 'gallery');
	const uploadIcon = (blob: Blob) => uploadFile('icon', blob, true, 'icon');
	const uploadSticker = (blob: Blob) => uploadFile('sticker', blob, true, 'sticker', { maskTag: 'square' });

	function uploadEmoji(blob: Blob) {
		const extra: Record<string, string | number> = { animationStyle: emojiStyle.toLowerCase(), maskTag: 'square' };
		if (emojiAnimated) {
			extra.frames = emojiFrames;
			extra.framesOverTime = emojiFps;
		}
		if (emojiPingPong) extra.loopStyle = 'pingpong';
		return uploadFile('emoji', blob, true, emojiAnimated ? 'emojianimated' : 'emoji', extra);
	}

	async function uploadPrint(blob: Blob, date: Date) {
		const form = new FormData();
		form.append('image', await fitPrint(blob, printCropBorder), 'image');
		form.append('note', printNote);
		form.append('timestamp', date.toISOString().slice(0, 19));
		const created = await api.upload<Print>(path('/gallery/prints'), form);
		prints = [created, ...prints];
	}

	/** The time on the viewer's clock, written as if it were UTC, which is what VRChat wants for a print. */
	function localAsUtc(date = new Date()): Date {
		const shifted = new Date(date);
		shifted.setMinutes(shifted.getMinutes() - shifted.getTimezoneOffset());
		return shifted;
	}

	async function runUpload(upload: () => Promise<void>) {
		pendingUploads += 1;
		message = '';
		try {
			await Promise.race([upload(), new Promise((_, reject) => setTimeout(() => reject(new Error('Upload timed out')), 30_000))]);
			message = 'Upload completed';
		} catch (err) {
			message = `Upload failed: ${err instanceof Error ? err.message : String(err)}`;
			throw err;
		} finally {
			pendingUploads -= 1;
		}
	}

	function parseEmojiFileName(name: string) {
		for (const value of name.replace(/\.[^/.]+$/, '').split('_')) {
			if (value.endsWith('animationStyle')) {
				emojiAnimated = false;
				const style = value.replace('animationStyle', '').toLowerCase();
				emojiStyle = Object.keys(EMOJI_STYLES).find((s) => s.toLowerCase() === style) ?? emojiStyle;
			}
			if (value.endsWith('frames')) {
				emojiAnimated = true;
				emojiFrames = parseInt(value.replace('frames', '')) || emojiFrames;
			}
			if (value.endsWith('fps')) emojiFps = parseInt(value.replace('fps', '')) || emojiFps;
			if (value.endsWith('loopStyle')) emojiPingPong = value.replace('loopStyle', '').toLowerCase() === 'pingpong';
		}
	}

	function onFileChosen(e: Event, aspect: number, upload: (blob: Blob) => Promise<void>, before?: (file: File) => void) {
		const input = e.currentTarget as HTMLInputElement;
		const file = input.files?.[0];
		input.value = '';
		if (!file) return;
		if (file.size > MAX_INPUT_BYTES) {
			message = 'File size too large';
			return;
		}
		if (!file.type.startsWith('image/')) {
			message = "File isn't an image";
			return;
		}
		before?.(file);
		cropFile = file;
		cropAspect = aspect;
		cropUpload = upload;
		cropOpen = true;
	}

	/** Dropped files skip the crop dialog, as in the original. */
	async function onDrop(e: DragEvent, upload: (file: File) => Promise<void>) {
		e.preventDefault();
		for (const file of e.dataTransfer?.files ?? []) {
			if (!file.type.startsWith('image/')) {
				message = "File isn't an image";
				continue;
			}
			await runUpload(() => upload(file)).catch(() => {});
		}
	}

	async function deleteFile(tab: FileTab, id: string) {
		try {
			await api.delete(path(`/gallery/files/${encodeURIComponent(id)}`));
			files[tab] = files[tab].filter((f) => f.id !== id);
		} catch (err) {
			fail(err);
		}
	}

	async function setUserIcon(id: string) {
		if (!info) return;
		if (!info.isSupporter) {
			message = 'VRCPlus required';
			return;
		}
		if (id ? fileId(info.userIcon) === id : !info.userIcon) return;
		try {
			const result = await api.put<{ userIcon: string }>(path('/gallery/icon'), { fileId: id });
			info = { ...info, userIcon: result.userIcon };
			message = 'Profile icon changed';
		} catch (err) {
			fail(err);
		}
	}

	async function deletePrint(id: string) {
		if (favoritePrints.includes(id)) {
			message = 'This print is in your favorites. Please remove it from your favorites before deleting it.';
			return;
		}
		try {
			await api.delete(path(`/gallery/prints/${encodeURIComponent(id)}`));
			prints = prints.filter((p) => p.id !== id);
		} catch (err) {
			fail(err);
		}
	}

	async function toggleFavoritePrint(id: string) {
		const favoritePath = path(`/gallery/prints/${encodeURIComponent(id)}/favorite`);
		try {
			if (favoritePrints.includes(id)) {
				await api.delete(favoritePath);
				favoritePrints = favoritePrints.filter((p) => p !== id);
			} else {
				await api.put(favoritePath);
				favoritePrints = [...favoritePrints, id];
			}
		} catch (err) {
			fail(err);
		}
	}

	async function consumeBundle(id: string) {
		try {
			await api.post(path(`/inventory/${encodeURIComponent(id)}/consume`));
			await refreshInventory();
		} catch (err) {
			fail(err);
		}
	}

	async function redeem() {
		const code = redeemCode.trim();
		if (!code) return;
		try {
			await api.post(path('/inventory/redeem'), { code });
			message = 'Code redeemed successfully';
			redeemOpen = false;
			redeemCode = '';
			await refreshInventory();
		} catch (err) {
			fail(err);
		}
	}

	function emojiFileName(emoji: UserFile): string {
		if (emoji.frames) return `${emoji.name}_${emoji.animationStyle}animationStyle_${emoji.frames}frames_${emoji.framesOverTime}fps_${emoji.loopStyle || 'linear'}loopStyle.png`;
		return `${emoji.name}_${emoji.animationStyle}animationStyle.png`;
	}

	function printFileName(print: Print): string {
		const local = print.createdAt ? localAsUtc(new Date(print.createdAt)) : print.timestamp ? new Date(print.timestamp) : localAsUtc();
		const date = local.toISOString().replace(/:/g, '-').replace(/T/g, '_').replace(/Z/g, '');
		return `${print.authorName}_${date}_${print.id}.png`;
	}
</script>

<svelte:head><title>Inventory · VRCXD</title></svelte:head>

{#snippet refreshUpload(refresh: () => void, inputId: string)}
	<div class="flex">
		<Button variant="outline" size="sm" class="rounded-r-none" onclick={refresh}><RefreshCw class="size-4" />{m.dialog_note_export_refresh()}</Button>
		<Button variant="outline" size="sm" class="-ml-px rounded-l-none" disabled={!canUpload} onclick={() => document.getElementById(inputId)?.click()}><Upload class="size-4" />{m.dialog_gallery_icons_upload()}</Button>
	</div>
{/snippet}

{#snippet tabLabel(label: string, count: number, limit?: number)}
	<span>{label}<span class="ml-[5px] text-xs">{count}{limit === undefined ? '' : `/${limit}`}</span></span>
{/snippet}

{#snippet deleteButton(onclick: () => void)}
	<Button size="icon-sm" variant="ghost" class="rounded-full text-destructive" aria-label={m.dialog_world_actions_delete()} {onclick}><Trash2 class="size-4" /></Button>
{/snippet}

<div class="h-full overflow-y-auto p-4">
	<div class="ml-2 flex items-center gap-2">
		<Button variant="ghost" size="sm" class="mr-3" href="/tools"><ArrowLeft class="size-4" />{m.view_tools_header()}</Button>
		<span class="text-lg font-semibold">{m.dialog_gallery_icons_header()}</span>
	</div>
	{#if message}<p class="mt-2 ml-2 text-xs text-muted-foreground">{message}</p>{/if}

	<Tabs.Root bind:value={activeTab} class="mt-3">
		<Tabs.List>
			<Tabs.Trigger value="gallery">{@render tabLabel('Photos', files.gallery.length, info?.limits.gallery)}</Tabs.Trigger>
			<Tabs.Trigger value="icons">{@render tabLabel('Icons', files.icon.length, info?.limits.icons)}</Tabs.Trigger>
			<Tabs.Trigger value="emojis">{@render tabLabel('Emojis', files.emoji.length, info?.limits.emoji)}</Tabs.Trigger>
			<Tabs.Trigger value="stickers">{@render tabLabel('Stickers', files.sticker.length, info?.limits.stickers)}</Tabs.Trigger>
			<Tabs.Trigger value="prints">{@render tabLabel('Prints', prints.length, info?.limits.prints)}</Tabs.Trigger>
			<Tabs.Trigger value="inventory">{@render tabLabel('Inventory', inventory.length)}</Tabs.Trigger>
		</Tabs.List>

		<Tabs.Content value="gallery">
			<div role="presentation" ondragover={(e) => e.preventDefault()} ondrop={(e) => onDrop(e, uploadGallery)}>
				<input id="GalleryUploadButton" type="file" accept="image/*" class="hidden" onchange={(e) => onFileChosen(e, 4 / 3, uploadGallery)} />
				{@render refreshUpload(() => refreshFiles('gallery'), 'GalleryUploadButton')}
				<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(180px, 1fr))">
					{#each files.gallery.filter((f) => f.url) as image (image.id)}
						<div class="overflow-hidden rounded-md border hover:bg-accent hover:shadow-sm">
							<ViewableImage src={image.url} imageClass="aspect-[4/3] w-full rounded-t-md object-cover" />
							<div class="flex gap-1 p-2">{@render deleteButton(() => deleteFile('gallery', image.id))}</div>
						</div>
					{/each}
				</div>
			</div>
		</Tabs.Content>

		<Tabs.Content value="icons">
			<div role="presentation" ondragover={(e) => e.preventDefault()} ondrop={(e) => onDrop(e, uploadIcon)}>
				<input id="VRCPlusIconUploadButton" type="file" accept="image/*" class="hidden" onchange={(e) => onFileChosen(e, 1, uploadIcon)} />
				<div class="flex items-center gap-2">
					{@render refreshUpload(() => refreshFiles('icon'), 'VRCPlusIconUploadButton')}
					<Button variant="outline" size="sm" disabled={!info?.userIcon || info.userIcon === info.currentAvatarImageUrl} onclick={() => setUserIcon('')}><X class="size-4" />{m.view_favorite_clear()}</Button>
				</div>
				<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(160px, 1fr))">
					{#each files.icon.filter((f) => f.url) as image (image.id)}
						{@const current = fileId(info?.userIcon) === image.id}
						<div class="overflow-hidden rounded-md border hover:bg-accent hover:shadow-sm {current ? 'ring-2 ring-primary' : ''}">
							<ViewableImage src={image.url} imageClass="aspect-square w-full rounded-t-md object-cover" />
							<div class="flex gap-1 p-2">
								{@render deleteButton(() => deleteFile('icon', image.id))}
								<Button size="icon-sm" class="rounded-full" variant={current ? 'default' : 'ghost'} aria-label="Use as icon" onclick={() => setUserIcon(image.id)}><Check class="size-4" /></Button>
							</div>
						</div>
					{/each}
				</div>
			</div>
		</Tabs.Content>

		<Tabs.Content value="emojis">
			<div role="presentation" ondragover={(e) => e.preventDefault()} ondrop={(e) => onDrop(e, uploadEmoji)}>
				<input id="EmojiUploadButton" type="file" accept="image/*" class="hidden" onchange={(e) => onFileChosen(e, 1, uploadEmoji, (file) => parseEmojiFileName(file.name))} />
				<div class="flex flex-col gap-2">
					<div class="flex items-center gap-2">
						{@render refreshUpload(() => refreshFiles('emoji'), 'EmojiUploadButton')}
						<Select.Root type="single" bind:value={emojiStyle}>
							<Select.Trigger size="sm" class="w-60" aria-label={m.dialog_gallery_icons_emoji_animation_styles()}>{emojiStyle}</Select.Trigger>
							<Select.Content class="max-h-80">
								{#each Object.entries(EMOJI_STYLES) as [name, fileName] (name)}
									<Select.Item value={name}>
										<span class="flex items-center gap-2"><img src={imageSrc(EMOJI_STYLE_URL + fileName)} alt="" loading="lazy" class="size-10 rounded-sm object-cover" />{name}</span>
									</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
						<label class="inline-flex items-center gap-2 text-sm"><input type="checkbox" class="accent-primary" bind:checked={emojiAnimated} />{m.dialog_gallery_icons_emoji_animation_type()}</label>
					</div>
					{#if emojiAnimated}
						<div class="flex items-center gap-2 text-sm">
							<Button size="sm" variant="outline" href="https://vrcemoji.com" target="_blank" rel="noopener noreferrer">{m.dialog_gallery_icons_create_animated_emoji()}</Button>
							<span>{m.dialog_gallery_icons_emoji_animation_fps()}</span>
							<Input type="number" min="1" max="64" step="1" class="h-8 w-28" bind:value={emojiFps} />
							<span>{m.dialog_gallery_icons_emoji_animation_frame_count()}</span>
							<Input type="number" min="2" max="64" step="1" class="h-8 w-28" bind:value={emojiFrames} />
							<label class="inline-flex items-center gap-2"><input type="checkbox" class="accent-primary" bind:checked={emojiPingPong} />{m.dialog_gallery_icons_emoji_loop_pingpong()}</label>
						</div>
						<span class="text-sm text-muted-foreground">{m.dialog_gallery_icons_flipbook_info()}</span>
					{/if}
				</div>
				<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(160px, 1fr))">
					{#each files.emoji.filter((f) => f.url) as image (image.id)}
						<div class="overflow-hidden rounded-md border hover:bg-accent hover:shadow-sm">
							<ViewableImage src={image.url} fileName={emojiFileName(image)}><EmojiImage url={imageSrc(image.url!)} frames={image.frames} fps={image.framesOverTime} loopStyle={image.loopStyle} /></ViewableImage>
							<div class="flex flex-wrap items-center gap-1 px-2.5 pt-1.5 text-xs text-muted-foreground">
								{#if image.loopStyle === 'pingpong'}<RefreshCw class="size-3" />{/if}
								<span>{image.animationStyle}</span>
								{#if image.framesOverTime}<span>{image.framesOverTime}fps</span>{/if}
								{#if image.frames}<span>{image.frames}frames</span>{/if}
							</div>
							<div class="flex gap-1 p-2">{@render deleteButton(() => deleteFile('emoji', image.id))}</div>
						</div>
					{/each}
				</div>
			</div>
		</Tabs.Content>

		<Tabs.Content value="stickers">
			<div role="presentation" ondragover={(e) => e.preventDefault()} ondrop={(e) => onDrop(e, uploadSticker)}>
				<input id="StickerUploadButton" type="file" accept="image/*" class="hidden" onchange={(e) => onFileChosen(e, 1, uploadSticker)} />
				{@render refreshUpload(() => refreshFiles('sticker'), 'StickerUploadButton')}
				<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(160px, 1fr))">
					{#each files.sticker.filter((f) => f.url) as image (image.id)}
						<div class="overflow-hidden rounded-md border hover:bg-accent hover:shadow-sm">
							<ViewableImage src={image.url} imageClass="aspect-square w-full rounded-t-md object-cover" />
							<div class="flex gap-1 p-2">{@render deleteButton(() => deleteFile('sticker', image.id))}</div>
						</div>
					{/each}
				</div>
			</div>
		</Tabs.Content>

		<Tabs.Content value="prints">
			<div role="presentation" ondragover={(e) => e.preventDefault()} ondrop={(e) => onDrop(e, (file) => uploadPrint(file, new Date(file.lastModified)))}>
				<input id="PrintUploadButton" type="file" accept="image/*" class="hidden" onchange={(e) => onFileChosen(e, 16 / 9, (blob) => uploadPrint(blob, localAsUtc()))} />
				<div class="flex items-center">
					{@render refreshUpload(refreshPrints, 'PrintUploadButton')}
					<Input class="ml-2 h-8 w-75" maxlength={32} placeholder={m.dialog_user_info_note()} bind:value={printNote} />
					<label class="mx-2 inline-flex items-center gap-2 text-sm"><input type="checkbox" class="accent-primary" bind:checked={printCropBorder} />{m.dialog_gallery_icons_crop_print_border()}</label>
				</div>
				<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(200px, 1fr))">
					{#each prints as print (print.id)}
						{@const favorite = favoritePrints.includes(print.id)}
						<div class="overflow-hidden rounded-md border hover:bg-accent hover:shadow-sm">
							<ViewableImage src={print.files?.image} fileName={printFileName(print)} imageClass="aspect-[16/9] w-full rounded-t-md object-cover" />
							<div class="min-w-0 px-2.5 pt-1.5 text-xs text-muted-foreground">
								{#if print.note}<div class="truncate text-sm font-medium text-foreground">{print.note}</div>{/if}
								<div class="truncate">{#if print.worldId}<a class="hover:underline" href={worldHref(print.worldId)}>{print.worldName ?? print.worldId}</a>{:else}{print.worldName ?? ' '}{/if}</div>
								<div class="truncate font-mono">
									{#if print.authorId}<a class="hover:underline" href={userHref(print.authorId)}>{print.authorName ?? print.authorId}</a>{:else}&nbsp;{/if}
								</div>
								{#if print.createdAt}<div class="truncate font-mono text-2xs">{formatDateFilter(print.createdAt, 'long')}</div>{/if}
							</div>
							<div class="flex gap-1 p-2">
								{@render deleteButton(() => deletePrint(print.id))}
								<Button size="icon-sm" variant="ghost" class="ml-auto rounded-full" aria-label={favorite ? 'Remove from favorites' : 'Add to favorites'} onclick={() => toggleFavoritePrint(print.id)}>
									<Star class="size-4 {favorite ? 'fill-yellow-500 text-yellow-500' : 'hover:text-yellow-500'}" />
								</Button>
							</div>
						</div>
					{/each}
				</div>
			</div>
		</Tabs.Content>

		<Tabs.Content value="inventory">
			<div class="flex flex-wrap items-center gap-2">
				<div class="flex">
					<Button variant="outline" size="sm" class="rounded-r-none" onclick={refreshInventory}><RefreshCw class="size-4" />{m.dialog_note_export_refresh()}</Button>
					<Button variant="outline" size="sm" class="-ml-px rounded-l-none" onclick={() => (redeemOpen = true)}><Gift class="size-4" />{m.dialog_gallery_icons_redeem()}</Button>
				</div>
				<Select.Root type="single" bind:value={inventoryFilter}>
					<Select.Trigger size="sm" class="w-44">{inventoryFilter === 'all' ? 'All types' : inventoryFilter}</Select.Trigger>
					<Select.Content>
						<Select.Item value="all">{m.dialog_gallery_icons_all_types()}</Select.Item>
						{#each inventoryTypes as type (type)}
							<Select.Item value={type}>{type}</Select.Item>
						{/each}
					</Select.Content>
				</Select.Root>
			</div>
			<div class="mt-3 grid gap-3" style="grid-template-columns: repeat(auto-fill, minmax(180px, 1fr))">
				{#each filteredInventory as item (item.id)}
					<div class="overflow-hidden rounded-md border pb-4 hover:bg-accent hover:shadow-sm">
						<ViewableImage src={item.imageUrl} imageClass="aspect-square w-full rounded-t-md object-cover" />
						<div class="min-w-0 px-2.5 pt-1.5 text-xs text-muted-foreground">
							<div class="truncate text-sm font-medium text-foreground">{item.name}</div>
							{#if item.description}<div class="truncate">{item.description}</div>{/if}
							<div class="truncate font-mono text-2xs">{formatDateFilter(item.created_at, 'long')}</div>
							<div>{item.itemTypeLabel || item.itemType}</div>
						</div>
						{#if item.itemType === 'bundle'}
							<div class="p-2"><Button size="sm" onclick={() => consumeBundle(item.id)}>{m.dialog_gallery_icons_consume_bundle()}</Button></div>
						{/if}
					</div>
				{/each}
			</div>
		</Tabs.Content>
	</Tabs.Root>
</div>

<ImageCropDialog bind:open={cropOpen} title={m.dialog_gallery_icons_upload()} aspectRatio={cropAspect} file={cropFile} onconfirm={(blob) => runUpload(() => cropUpload(blob))} />

<Dialog.Root bind:open={redeemOpen}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header><Dialog.Title>{m.prompt_redeem_header()}</Dialog.Title></Dialog.Header>
		<Input placeholder={m.prompt_redeem_description()} bind:value={redeemCode} />
		<Dialog.Footer>
			<Button variant="secondary" size="sm" onclick={() => (redeemOpen = false)}>{m.view_login_qr_cancel()}</Button>
			<Button size="sm" disabled={!redeemCode.trim()} onclick={redeem}>{m.dialog_gallery_icons_redeem()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

