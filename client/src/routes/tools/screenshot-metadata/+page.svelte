<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { goto } from '$app/navigation';
	import { onMount } from 'svelte';
	import { toast } from 'svelte-sonner';
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import Camera from '@lucide/svelte/icons/camera';
	import ChevronLeft from '@lucide/svelte/icons/chevron-left';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import Copy from '@lucide/svelte/icons/copy';
	import FolderOpen from '@lucide/svelte/icons/folder-open';
	import FolderSearch from '@lucide/svelte/icons/folder-search';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import Upload from '@lucide/svelte/icons/upload';
	import Users from '@lucide/svelte/icons/users';
	import { api, ApiError } from '#lib/api/client.js';
	import { NONE, formatDateFilter } from '#lib/helpers/format.js';
	import Location from '#lib/components/instance/location.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { loadHandle, saveHandle } from '#lib/content/handle-store.js';
	import { fitImage } from '#lib/content/image-upload.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { deleteTextMetadata, getScreenshotMetadata, isScreenshotPath, readResolution, screenshotDate, searchMatch, SEARCH_TYPES, type ScreenshotMetadata, type SearchType } from '#lib/content/screenshot-metadata.js';
	import { lookupUser } from '#lib/people/user-lookup.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Screenshot Manager (Tools/ScreenshotMetadata.vue) for the browser: a PNG opened
	// with Browse or dropped here shows its VRChat and VRCX metadata. Picking the VRChat Photos folder
	// (remembered by this browser) adds what the original does with the disk: the last screenshot,
	// previous/next through the folder, and searching every picture by player or world. Delete
	// Metadata rewrites the file in place when the browser was given it as a file, else saves a copy.

	type Entry = { path: string; handle: FileSystemFileHandle };
	type Shown = {
		path: string;
		name: string;
		url: string;
		file: File;
		handle?: FileSystemFileHandle;
		metadata: ScreenshotMetadata;
		resolution: string;
		dateTime: number;
	};
	type Row = { path: string; dateTime: number; dateFormatted: string; world: string; playerCount: number; resolution: string; match: string; author: string };

	const FOLDER_KEY = 'vrchat-photos';
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);
	const canPickFolder = typeof window !== 'undefined' && 'showDirectoryPicker' in window;

	let folder = $state<FileSystemDirectoryHandle | null>(null);
	let folderFiles = $state.raw<Entry[]>([]);
	let shown = $state<Shown | null>(null);
	let error = $state('');
	let search = $state('');
	let searchType = $state<SearchType>('Player Name');
	let searching = $state(false);
	let results = $state.raw<Row[]>([]);
	let resultIndex = $state<number | null>(null);
	let viewMode = $state<'detail' | 'table'>('detail');
	let sort = $state<{ key: keyof Row; asc: boolean }>({ key: 'dateTime', asc: false });
	let supporter = $state(false);
	let uploading = $state(false);
	let fileInput: HTMLInputElement | undefined = $state();

	// What each file read gave, so a second search doesn't read the folder again (the original's metadata cache).
	const cache = new Map<string, { lastModified: number; metadata: ScreenshotMetadata; resolution: string; dateTime: number }>();

	const hasMatchColumn = $derived(searchType === 'Player Name' || searchType === 'Player ID');
	const sortedResults = $derived.by(() => {
		const { key, asc } = sort;
		return [...results].sort((a, b) => {
			let va: unknown = a[key];
			let vb: unknown = b[key];
			if (typeof va === 'string') va = va.toLowerCase();
			if (typeof vb === 'string') vb = vb.toLowerCase();
			if ((va as number) < (vb as number)) return asc ? -1 : 1;
			if ((va as number) > (vb as number)) return asc ? 1 : -1;
			return 0;
		});
	});

	async function listPngs(dir: FileSystemDirectoryHandle, prefix = ''): Promise<Entry[]> {
		const out: Entry[] = [];
		for await (const [name, handle] of (dir as unknown as { entries(): AsyncIterable<[string, FileSystemHandle]> }).entries()) {
			const path = prefix ? `${prefix}/${name}` : name;
			if (handle.kind === 'directory') out.push(...(await listPngs(handle as FileSystemDirectoryHandle, path)));
			else if (name.toLowerCase().endsWith('.png')) out.push({ path, handle: handle as FileSystemFileHandle });
		}
		return out;
	}

	async function useFolder(dir: FileSystemDirectoryHandle) {
		folder = dir;
		folderFiles = (await listPngs(dir)).sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
	}

	async function pickFolder() {
		try {
			const dir = await (window as unknown as { showDirectoryPicker(o?: object): Promise<FileSystemDirectoryHandle> }).showDirectoryPicker({ id: FOLDER_KEY, mode: 'read' });
			await saveHandle(FOLDER_KEY, dir);
			await useFolder(dir);
			toast.success(`${folderFiles.length} pictures in ${dir.name}`);
		} catch (err) {
			if ((err as DOMException)?.name !== 'AbortError') toast.error(String(err));
		}
	}

	/** The remembered folder, asking the browser again when it needs to (only from a click). */
	async function ensureFolder(ask: boolean): Promise<boolean> {
		if (folder) return true;
		const saved = await loadHandle<FileSystemDirectoryHandle>(FOLDER_KEY);
		if (saved) {
			const handle = saved as unknown as { queryPermission(o: object): Promise<string>; requestPermission(o: object): Promise<string> };
			let state = await handle.queryPermission({ mode: 'read' });
			if (state === 'prompt' && ask) state = await handle.requestPermission({ mode: 'read' });
			if (state === 'granted') {
				await useFolder(saved);
				return true;
			}
		}
		if (ask && canPickFolder) {
			await pickFolder();
			return !!folder;
		}
		return false;
	}

	async function readEntry(path: string, file: File) {
		const hit = cache.get(path);
		if (hit && hit.lastModified === file.lastModified) return hit;
		const bytes = new Uint8Array(await file.arrayBuffer());
		const metadata = getScreenshotMetadata(bytes);
		const value = { lastModified: file.lastModified, metadata, resolution: readResolution(bytes), dateTime: screenshotDate(file.name, metadata, file.lastModified) };
		cache.set(path, value);
		return value;
	}

	async function display(path: string, file: File, handle?: FileSystemFileHandle) {
		if (!file.name.toLowerCase().endsWith('.png')) {
			error = 'Invalid file selected. Please select a valid VRChat screenshot.';
			return;
		}
		cache.delete(path);
		const read = await readEntry(path, file);
		if (shown) URL.revokeObjectURL(shown.url);
		shown = { path, name: file.name.replace(/\.png$/i, ''), url: URL.createObjectURL(file), file, handle, ...read };
		error = read.metadata.error ?? '';
	}

	async function displayEntry(entry: Entry) {
		await display(entry.path, await entry.handle.getFile(), entry.handle);
	}

	async function browse() {
		resetSearch();
		const picker = (window as unknown as { showOpenFilePicker?(o: object): Promise<FileSystemFileHandle[]> }).showOpenFilePicker;
		if (!picker) {
			fileInput?.click();
			return;
		}
		try {
			const [handle] = await picker.call(window, { id: FOLDER_KEY, types: [{ description: 'PNG Files', accept: { 'image/png': ['.png'] } }] });
			const file = await handle.getFile();
			const inFolder = folderFiles.find((f) => f.handle.name === file.name);
			await display(inFolder?.path ?? file.name, file, handle);
		} catch (err) {
			if ((err as DOMException)?.name !== 'AbortError') toast.error(String(err));
		}
	}

	async function lastScreenshot(ask = true) {
		resetSearch();
		if (!(await ensureFolder(ask))) return;
		let newest: { entry: Entry; time: number } | null = null;
		for (const entry of folderFiles) {
			if (!isScreenshotPath(entry.path)) continue;
			const time = (await entry.handle.getFile()).lastModified;
			if (!newest || time > newest.time) newest = { entry, time };
		}
		if (newest) await displayEntry(newest.entry);
	}

	async function onDrop(event: DragEvent) {
		event.preventDefault();
		const item = event.dataTransfer?.items?.[0];
		if (!item) return;
		resetSearch();
		const getHandle = (item as unknown as { getAsFileSystemHandle?(): Promise<FileSystemHandle | null> }).getAsFileSystemHandle;
		const file = item.getAsFile();
		const handle = getHandle ? await getHandle.call(item) : null;
		if (handle && handle.kind === 'file') {
			const fromHandle = await (handle as FileSystemFileHandle).getFile();
			await display(folderFiles.find((f) => f.handle.name === fromHandle.name)?.path ?? fromHandle.name, fromHandle, handle as FileSystemFileHandle);
			return;
		}
		if (file) await display(file.name, file);
	}

	// Previous/next: through the search results while there are any, else the picture's folder.
	async function step(delta: 1 | -1) {
		if (resultIndex !== null && sortedResults.length) {
			const next = (resultIndex + delta + sortedResults.length) % sortedResults.length;
			resultIndex = next;
			const entry = folderFiles.find((f) => f.path === sortedResults[next].path);
			if (entry) await displayEntry(entry);
			return;
		}
		if (!shown) return;
		const current = shown.path;
		const dir = current.includes('/') ? current.slice(0, current.lastIndexOf('/') + 1) : '';
		const siblings = folderFiles.filter((f) => f.path.startsWith(dir) && !f.path.slice(dir.length).includes('/'));
		const index = siblings.findIndex((f) => f.path === current);
		const target = index < 0 ? undefined : siblings[index + delta];
		if (target) await displayEntry(target);
	}

	function onKey(event: KeyboardEvent) {
		if (!event.altKey) return;
		if (event.key === 'ArrowLeft') {
			event.preventDefault();
			void step(-1);
		} else if (event.key === 'ArrowRight') {
			event.preventDefault();
			void step(1);
		}
	}

	function resetSearch() {
		search = '';
		results = [];
		resultIndex = null;
		viewMode = 'detail';
	}

	let searchRun = 0;
	function onSearchInput() {
		const run = ++searchRun;
		setTimeout(() => {
			if (run !== searchRun) return;
			void runSearch();
		}, 500);
	}

	async function runSearch() {
		const query = search.trim();
		if (query.length < 3) {
			results = [];
			resultIndex = null;
			viewMode = 'detail';
			if (!query.length && shown) error = shown.metadata.error ?? '';
			return;
		}
		if (!(await ensureFolder(true))) {
			error = 'Pick the VRChat Photos folder to search it.';
			return;
		}
		const run = searchRun;
		searching = true;
		try {
			const found: Row[] = [];
			for (const entry of folderFiles) {
				const file = await entry.handle.getFile();
				const read = await readEntry(entry.path, file);
				if (read.metadata.error) continue;
				const { matches, match } = searchMatch(read.metadata, query, searchType);
				if (!matches) continue;
				found.push({
					path: entry.path,
					dateTime: read.dateTime,
					dateFormatted: read.dateTime ? formatDateFilter(new Date(read.dateTime).toISOString(), 'short') : NONE,
					world: read.metadata.world.name ?? '',
					playerCount: read.metadata.players.length,
					resolution: read.resolution,
					match,
					author: read.metadata.author.displayName ?? ''
				});
			}
			if (run !== searchRun) return;
			if (!found.length) {
				results = [];
				resultIndex = null;
				viewMode = 'detail';
				shown = null;
				error = 'No results found';
				return;
			}
			results = found;
			resultIndex = 0;
			viewMode = 'table';
		} finally {
			searching = false;
		}
	}

	function toggleSort(key: keyof Row) {
		sort = sort.key === key ? { key, asc: !sort.asc } : { key, asc: key !== 'dateTime' };
	}

	async function selectResult(index: number) {
		const row = sortedResults[index];
		const entry = row && folderFiles.find((f) => f.path === row.path);
		if (!entry) return;
		resultIndex = index;
		viewMode = 'detail';
		await displayEntry(entry);
	}

	async function copyImage() {
		if (!shown) return;
		try {
			await navigator.clipboard.write([new ClipboardItem({ 'image/png': shown.file })]);
			toast.success(m.message_image_copied_to_clipboard());
		} catch (err) {
			toast.error(String(err));
		}
	}

	async function upload() {
		if (!shown || !vrchatAuth.ownerUserId) return;
		if (shown.file.size > 10_000_000) {
			toast.error('File is too large');
			return;
		}
		uploading = true;
		try {
			const form = new FormData();
			form.append('file', await fitImage(shown.file, false), 'blob');
			form.append('tag', 'gallery');
			await api.upload(ownPath('/gallery/files'), form);
			toast.success(m.message_gallery_uploaded());
		} catch (err) {
			toast.error(`Failed to upload gallery image: ${ApiError.from(err).message}`);
		} finally {
			uploading = false;
		}
	}

	type Writable = { write(d: BufferSource): Promise<void>; close(): Promise<void> };
	type WritableHandle = FileSystemFileHandle & { requestPermission?(o: object): Promise<string>; createWritable?(): Promise<Writable> };

	async function deleteMetadata() {
		if (!shown) return;
		const cleaned = deleteTextMetadata(new Uint8Array(await shown.file.arrayBuffer()));
		const handle = shown.handle as WritableHandle | undefined;
		try {
			if (handle?.createWritable && (await handle.requestPermission?.({ mode: 'readwrite' })) === 'granted') {
				const writable = await handle.createWritable();
				await writable.write(cleaned as BufferSource);
				await writable.close();
				toast.success('Metadata deleted');
				await display(shown.path, await handle.getFile(), handle);
				return;
			}
			const link = document.createElement('a');
			link.href = URL.createObjectURL(new Blob([cleaned as BlobPart], { type: 'image/png' }));
			link.download = shown.file.name;
			link.click();
			URL.revokeObjectURL(link.href);
			toast.success('Saved a copy without metadata');
		} catch {
			toast.error('Failed to delete metadata');
		}
	}

	onMount(() => {
		if (vrchatAuth.ownerUserId) {
			api.get<{ tags?: string[] }>(`/1/users/${encodeURIComponent(vrchatAuth.ownerUserId)}`).then(
				(me) => (supporter = me.tags?.includes('system_supporter') ?? false),
				() => {}
			);
		}
		// The original opens the last screenshot; here only when the folder may already be read.
		void lastScreenshot(false);
		return () => {
			if (shown) URL.revokeObjectURL(shown.url);
		};
	});

	const SECTION = 'mb-1.5 text-2xs font-medium tracking-[0.08em] text-muted-foreground uppercase';
	const TH = 'cursor-pointer border-b px-3 py-2 text-left text-2xs font-medium tracking-[0.08em] whitespace-nowrap text-muted-foreground uppercase select-none';
	const TD = 'overflow-hidden border-b px-3 py-2 text-sm text-ellipsis whitespace-nowrap';
	const columns = $derived<[keyof Row, string][]>([['dateTime', 'Date'], ['world', 'World'], ...(hasMatchColumn ? ([['match', 'Match']] as [keyof Row, string][]) : []), ['author', 'Author'], ['playerCount', 'Players']]);
</script>

<svelte:head><title>Screenshot Metadata · VRCXD</title></svelte:head>
<svelte:window onkeydown={onKey} />

<input
	bind:this={fileInput}
	type="file"
	accept=".png,image/png"
	class="hidden"
	onchange={(e) => {
		const f = e.currentTarget.files?.[0];
		if (f) void display(f.name, f);
		e.currentTarget.value = '';
	}}
/>

<div class="flex h-full flex-col overflow-hidden p-4">
	<div class="ml-2 flex items-center gap-2">
		<Button variant="ghost" size="sm" class="mr-3" onclick={() => goto('/tools')}><ArrowLeft />{m.view_tools_header()}</Button>
		<span class="text-lg font-semibold">{m.dialog_screenshot_metadata_header()}</span>
	</div>
	<div class="my-2 flex flex-wrap items-center gap-2">
		<Button size="sm" variant="outline" onclick={browse}><FolderSearch />{m.dialog_screenshot_metadata_browse()}</Button>
		<Button size="sm" variant="outline" onclick={() => lastScreenshot(true)} disabled={!canPickFolder}><ImageIcon />{m.dialog_screenshot_metadata_last_screenshot()}</Button>
		{#if canPickFolder}
			<Button size="sm" variant="outline" onclick={pickFolder} title="The VRChat Photos folder, for Last Screenshot, previous/next and search"><FolderOpen />{folder ? folder.name : 'Photos Folder'}</Button>
		{/if}
		<div class="flex-1"></div>
		<Input bind:value={search} placeholder={m.view_favorite_worlds_search()} class="h-8 w-[200px]" oninput={onSearchInput} disabled={!canPickFolder} />
		<Select.Root
			type="single"
			value={searchType}
			onValueChange={(v) => {
				searchType = v as SearchType;
				onSearchInput();
			}}
		>
			<Select.Trigger size="sm" class="w-[150px]">{searchType}</Select.Trigger>
			<Select.Content>
				{#each SEARCH_TYPES as t (t)}<Select.Item value={t} label={t} />{/each}
			</Select.Content>
		</Select.Root>
		{#if searching}
			<span class="text-xs text-muted-foreground">Searching…</span>
		{:else if viewMode === 'table' && results.length}
			<span class="text-xs whitespace-pre-wrap">{results.length} results</span>
		{:else if resultIndex !== null}
			<span class="text-xs whitespace-pre-wrap">{resultIndex + 1}/{results.length}</span>
		{/if}
	</div>

	{#if viewMode === 'table'}
		<div class="min-h-0 flex-1 overflow-auto">
			<table class="w-full border-collapse text-compact">
				<thead class="sticky top-0 z-1 bg-background">
					<tr>
						{#each columns as [key, label] (key)}
							<th class={TH} onclick={() => toggleSort(key)}>{label}{#if sort.key === key}<span class="ml-1 text-3xs">{sort.asc ? '↑' : '↓'}</span>{/if}</th>
						{/each}
						<th class="{TH} w-24 cursor-default">{m.dialog_screenshot_metadata_col_resolution()}</th>
						<th class="w-8 border-b"></th>
					</tr>
				</thead>
				<tbody>
					{#each sortedResults as row, i (row.path)}
						<tr class={['group/row cursor-pointer transition-colors duration-100 hover:bg-accent', i === resultIndex && 'border-l-[3px] border-l-primary bg-accent']} onclick={() => selectResult(i)}>
							<td class={[TD, i === resultIndex && 'pl-[9px]']}>{row.dateFormatted}</td>
							<td class={TD}>{row.world || NONE}</td>
							{#if hasMatchColumn}<td class="{TD} text-primary">{row.match || NONE}</td>{/if}
							<td class="{TD} text-muted-foreground">{row.author || NONE}</td>
							<td class={TD}><span class="inline-flex items-center gap-1"><Users class="size-3 text-muted-foreground" />{row.playerCount}</span></td>
							<td class="{TD} text-xs text-muted-foreground">{row.resolution}</td>
							<td class="border-b py-2 pr-2"><ChevronRight class="size-4 text-muted-foreground opacity-0 transition-opacity duration-150 group-hover/row:opacity-100" /></td>
						</tr>
					{/each}
				</tbody>
			</table>
		</div>
	{:else}
		<div class="grid min-h-0 flex-1 gap-4 overflow-hidden" style="grid-template-columns: 1fr 380px">
			<div class="flex min-h-0 flex-col items-center" role="region" aria-label="Screenshot" ondragover={(e) => e.preventDefault()} ondragenter={(e) => e.preventDefault()} ondrop={onDrop}>
				<div class="relative flex min-h-0 w-full flex-1 items-center justify-center">
					{#if shown}
						<button type="button" class="contents" onclick={() => imageViewer.show(shown?.url, shown?.file.name)}>
							<img class="max-h-full max-w-full cursor-pointer object-contain" src={shown.url} alt={shown.name} />
						</button>
						<Button variant="ghost" size="icon" aria-label={m.view_player_list_photon_previous()} class="absolute top-1/2 left-2 -translate-y-1/2 rounded-full bg-background/50 opacity-0 transition-opacity hover:opacity-100" onclick={() => step(-1)}><ChevronLeft /></Button>
						<Button variant="ghost" size="icon" aria-label={m.table_pagination_next()} class="absolute top-1/2 right-2 -translate-y-1/2 rounded-full bg-background/50 opacity-0 transition-opacity hover:opacity-100" onclick={() => step(1)}><ChevronRight /></Button>
					{:else}
						<span class="text-sm text-muted-foreground">{m.dialog_screenshot_metadata_drag()}</span>
					{/if}
				</div>
				<div class="flex h-[50px] shrink-0 items-center justify-center">
					<div class="flex rounded-lg shadow-lg">
						<Button variant="outline" size="sm" class="rounded-r-none" onclick={() => step(-1)}><ArrowLeft /><kbd class="ml-1 rounded border px-1 text-3xs">{isMac ? '⌥' : 'Alt'}</kbd><kbd class="rounded border px-1 text-3xs">←</kbd></Button>
						<Button variant="outline" size="sm" class="rounded-l-none border-l-0" onclick={() => step(1)}><kbd class="ml-1 rounded border px-1 text-3xs">{isMac ? '⌥' : 'Alt'}</kbd><kbd class="rounded border px-1 text-3xs">→</kbd><ArrowRight /></Button>
					</div>
				</div>
			</div>
			<div class="overflow-y-auto pr-1">
				{#if results.length}
					<Button variant="ghost" size="sm" class="mb-2" onclick={() => (viewMode = 'table')}><ArrowLeft class="size-3.5" />Back to {results.length} results</Button>
				{/if}
				{#if error}
					<pre class="text-xs whitespace-pre-wrap">{error}</pre>
				{/if}
				{#if shown}
					{@const meta = shown.metadata}
					{#if !error && (meta.world.instanceId || meta.world.name || meta.author.id || meta.author.displayName)}
						<div class="pb-4">
							<h4 class={SECTION}>{m.dialog_launch_location()}</h4>
							{#if meta.world.instanceId || meta.world.name}<Location location={meta.world.instanceId || meta.world.id} hint={meta.world.name ?? ''} />{/if}
							{#if meta.author.id || meta.author.displayName}
								<div class="flex items-center gap-1 text-muted-foreground">
									<Camera class="size-3.5 shrink-0" />
									<button type="button" class="truncate hover:underline" onclick={() => lookupUser({ userId: meta.author.id || null, displayName: meta.author.displayName })}>{meta.author.displayName || meta.author.id}</button>
								</div>
							{/if}
						</div>
					{/if}
					{#if !error && meta.players.length}
						<div class="border-t pt-4 pb-4">
							<h4 class={SECTION}>Players ({meta.players.length})</h4>
							<div class="flex max-h-[180px] flex-wrap gap-1 overflow-y-auto">
								{#each meta.players as user, i (`${user.id}:${i}`)}
									<Badge variant="secondary" class="cursor-pointer transition-colors hover:bg-accent" onclick={() => lookupUser({ userId: user.id || null, displayName: user.displayName })}>{user.displayName}</Badge>
								{/each}
							</div>
						</div>
					{/if}
					<div class="border-t pt-4 pb-4">
						<h4 class={SECTION}>{m.dialog_screenshot_metadata_section_file_info()}</h4>
						{#if shown.dateTime}<span class="text-sm">{formatDateFilter(new Date(shown.dateTime).toISOString(), 'long')}</span><br />{/if}
						<span class="text-xs text-muted-foreground">{shown.resolution} · {(shown.file.size / 1024 / 1024).toFixed(2)} MB</span>
						<br />
						<span class="text-xs text-muted-foreground/60">{shown.name}</span>
						{#if shown.path !== shown.file.name}<br /><span class="text-xs text-muted-foreground/60">{shown.path}</span>{/if}
					</div>
					{#if !error && meta.note}
						<div class="border-t pt-4 pb-4">
							<h4 class={SECTION}>{m.dialog_user_info_note()}</h4>
							<span class="text-sm text-muted-foreground">{meta.note}</span>
						</div>
					{/if}
					<div class="border-t pt-4 pb-4">
						<h4 class={SECTION}>{m.dialog_group_member_moderation_actions()}</h4>
						<div class="flex flex-wrap gap-2">
							<Button size="sm" variant="outline" onclick={copyImage}><Copy />{m.dialog_screenshot_metadata_copy_image()}</Button>
							{#if supporter}<Button size="sm" variant="outline" disabled={uploading} onclick={upload}><Upload />{m.dialog_gallery_icons_upload()}</Button>{/if}
							<Button size="sm" variant="destructive" onclick={deleteMetadata}><Trash2 />{m.dialog_screenshot_metadata_delete_metadata()}</Button>
						</div>
					</div>
				{/if}
			</div>
		</div>
	{/if}
</div>
