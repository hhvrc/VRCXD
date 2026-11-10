<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { goto } from '$app/navigation';
	import ChevronRight from '@lucide/svelte/icons/chevron-right';
	import Ellipsis from '@lucide/svelte/icons/ellipsis';
	import GripVertical from '@lucide/svelte/icons/grip-vertical';
	import LinkIcon from '@lucide/svelte/icons/link';
	import Minus from '@lucide/svelte/icons/minus';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import { dashboards } from '#lib/nav/dashboards.svelte.js';
	import { navLayout } from '#lib/nav/nav-layout.svelte.js';
	import { DASHBOARD_NAV_KEY_PREFIX, DEFAULT_FOLDER_ICON, generateNavFolderId, isDashboardNavKey, isToolNavKey, type NavEntry, type NavFolderEntry } from '#lib/nav/nav-layout.js';

	// The original's CustomNavDialog: drag entries to reorder them and into or out of folders, add,
	// rename and remove folders, hide entries (they wait under Hidden to be shown again), and edit or
	// delete dashboards. Nothing is kept until Confirm.

	type Placement = { parentId: string | null; index: number };
	type Drag = { type: 'item'; key: string } | { type: 'folder'; id: string };
	type Drop = { parentId: string | null; index: number } | null;

	let layout = $state<NavEntry[]>([]);
	let hidden = $state<string[]>([]);
	let placements = new Map<string, Placement>();
	let expanded = $state<string[]>([]);
	let dragging = $state<Drag | null>(null);
	let dropAt = $state<Drop>(null);
	let intoFolder = $state<string | null>(null);
	let confirming = $state<Confirming | undefined>();
	let editor = $state<{ open: boolean; type: 'folder' | 'dashboard'; id: string; editing: boolean; name: string; icon: string }>({ open: false, type: 'folder', id: '', editing: false, name: '', icon: '' });

	const clone = (entries: NavEntry[]): NavEntry[] => entries.map((e) => (e.type === 'folder' ? { ...e, items: [...e.items] } : { ...e }));

	// A fresh copy each time the dialog opens.
	$effect(() => {
		if (!navLayout.editing) return;
		untrack(() => {
			layout = clone(navLayout.entries);
			hidden = [...navLayout.hiddenKeys];
			placements = new Map();
			expanded = layout.filter((e) => e.type === 'folder').map((e) => (e as NavFolderEntry).id);
		});
	});

	const def = (key: string) => navLayout.byKey.get(key);
	const hiddenItems = $derived(hidden.map((key) => def(key)).filter((d) => !!d));

	function close() {
		navLayout.editing = false;
	}

	/** Takes an item out wherever it is, saying where it was. */
	function takeItem(key: string): Placement | null {
		for (let i = 0; i < layout.length; i++) {
			const entry = layout[i];
			if (entry.type === 'item' && entry.key === key) {
				layout.splice(i, 1);
				return { parentId: null, index: i };
			}
			if (entry.type === 'folder') {
				const at = entry.items.indexOf(key);
				if (at >= 0) {
					entry.items.splice(at, 1);
					return { parentId: entry.id, index: at };
				}
			}
		}
		return null;
	}

	function putItem(key: string, at: Placement) {
		if (at.parentId) {
			const folder = layout.find((e): e is NavFolderEntry => e.type === 'folder' && e.id === at.parentId);
			if (folder) {
				folder.items.splice(Math.max(0, Math.min(at.index, folder.items.length)), 0, key);
				return;
			}
		}
		layout.splice(Math.max(0, Math.min(at.index, layout.length)), 0, { type: 'item', key });
	}

	/** handleHideItem: a pinned tool is removed instead; anything else waits under Hidden. */
	function hide(key: string) {
		const was = takeItem(key);
		if (isToolNavKey(key)) return;
		if (was) placements.set(key, was);
		hidden = [...hidden, key];
	}

	/** handleShowItem: back where it was hidden from, if that place still exists. */
	function show(key: string) {
		hidden = hidden.filter((k) => k !== key);
		putItem(key, placements.get(key) ?? { parentId: null, index: layout.length });
		placements.delete(key);
	}

	/** handleDeleteFolder: its entries take its place. */
	function deleteFolder(id: string) {
		const at = layout.findIndex((e) => e.type === 'folder' && e.id === id);
		if (at < 0) return;
		const folder = layout[at] as NavFolderEntry;
		layout.splice(at, 1, ...folder.items.map((key) => ({ type: 'item' as const, key })));
	}

	function toggle(id: string) {
		expanded = expanded.includes(id) ? expanded.filter((k) => k !== id) : [...expanded, id];
	}

	function addFolder() {
		editor = { open: true, type: 'folder', id: generateNavFolderId(), editing: false, name: '', icon: '' };
	}

	function editFolder(folder: NavFolderEntry) {
		editor = { open: true, type: 'folder', id: folder.id, editing: true, name: folder.name, icon: folder.icon };
	}

	function editDashboard(key: string) {
		const dashboard = dashboards.get(key.slice(DASHBOARD_NAV_KEY_PREFIX.length));
		if (dashboard) editor = { open: true, type: 'dashboard', id: dashboard.id, editing: true, name: dashboard.name, icon: dashboard.icon };
	}

	/** handleFolderEditorSave. */
	function saveEditor() {
		const name = editor.name.trim();
		if (!name) return;
		const icon = editor.icon.trim();
		if (editor.type === 'dashboard') {
			dashboards.update(editor.id, { name, icon: icon || undefined });
		} else if (editor.editing) {
			const folder = layout.find((e): e is NavFolderEntry => e.type === 'folder' && e.id === editor.id);
			if (folder) Object.assign(folder, { name, icon: icon || DEFAULT_FOLDER_ICON });
		} else {
			layout.push({ type: 'folder', id: editor.id, name, icon: icon || DEFAULT_FOLDER_ICON, items: [] });
			expanded = [...expanded, editor.id];
		}
		editor.open = false;
	}

	/** handleAddDashboard: made at once, then saved with the layout and opened for editing. */
	async function addDashboard() {
		const dashboard = dashboards.create('Dashboard');
		layout.push({ type: 'item', key: `${DASHBOARD_NAV_KEY_PREFIX}${dashboard.id}` });
		navLayout.save($state.snapshot(layout) as NavEntry[], [...hidden]);
		close();
		await goto(`/dashboard/${dashboard.id}`);
		dashboards.editingId = dashboard.id;
	}

	function deleteDashboard(key: string) {
		const id = key.slice(DASHBOARD_NAV_KEY_PREFIX.length);
		confirming = {
			get title() { return m.nav_menu_custom_nav_delete_dashboard(); },
			get description() { return m.dashboard_confirmations_delete_description(); },
			destructive: true,
			run: () => {
				dashboards.remove(id);
				takeItem(key);
			}
		};
	}

	/** handleSave: empty folders are dropped. */
	function save() {
		const kept = ($state.snapshot(layout) as NavEntry[]).filter((e) => e.type !== 'folder' || e.items.length > 0);
		navLayout.save(kept, [...hidden]);
		close();
	}

	/** handleReset. */
	function reset() {
		layout = clone(navLayout.defaults);
		hidden = [];
		placements = new Map();
		expanded = layout.filter((e) => e.type === 'folder').map((e) => (e as NavFolderEntry).id);
	}

	// Dragging: an item can go anywhere, a folder only between top-level entries. Over a folder's
	// middle an item drops into it; otherwise it goes before or after the entry under the pointer.
	function over(e: DragEvent, parentId: string | null, index: number, folderId?: string) {
		if (!dragging || (dragging.type === 'folder' && parentId !== null)) return;
		e.preventDefault();
		const box = (e.currentTarget as HTMLElement).getBoundingClientRect();
		const y = (e.clientY - box.top) / box.height;
		if (folderId && dragging.type === 'item' && y > 0.25 && y < 0.75) {
			intoFolder = folderId;
			dropAt = null;
			return;
		}
		intoFolder = null;
		dropAt = { parentId, index: y < 0.5 ? index : index + 1 };
	}

	function overEmptyFolder(e: DragEvent, folderId: string) {
		if (dragging?.type !== 'item') return;
		e.preventDefault();
		intoFolder = folderId;
		dropAt = null;
	}

	function drop(e: DragEvent) {
		e.preventDefault();
		const drag = dragging;
		const target = intoFolder ? { parentId: intoFolder, index: Number.MAX_SAFE_INTEGER } : dropAt;
		end();
		if (!drag || !target) return;
		if (drag.type === 'folder') {
			const from = layout.findIndex((x) => x.type === 'folder' && x.id === drag.id);
			if (from < 0) return;
			const [folder] = layout.splice(from, 1);
			layout.splice(target.index > from ? target.index - 1 : target.index, 0, folder);
			return;
		}
		const from = takeItem(drag.key);
		const index = from && from.parentId === target.parentId && target.index > from.index ? target.index - 1 : target.index;
		putItem(drag.key, { parentId: target.parentId, index });
		if (target.parentId && !expanded.includes(target.parentId)) expanded = [...expanded, target.parentId];
	}

	function end() {
		dragging = null;
		dropAt = null;
		intoFolder = null;
	}

	const lineBefore = (parentId: string | null, index: number) => dropAt?.parentId === parentId && dropAt.index === index;
</script>

{#snippet row(label: string, icon: string, depth: number, opts: { folder?: NavFolderEntry; key?: string; parentId: string | null; index: number })}
	{@const isFolder = !!opts.folder}
	{@const isOpen = isFolder && expanded.includes(opts.folder!.id)}
	{@const dragged = dragging && (isFolder ? dragging.type === 'folder' && dragging.id === opts.folder!.id : dragging.type === 'item' && dragging.key === opts.key)}
	{#if lineBefore(opts.parentId, opts.index)}<div class="h-0.5 rounded bg-primary" style:margin-left="{depth * 1.5}rem"></div>{/if}
	<div
		role="listitem"
		draggable="true"
		class={[
			'group flex cursor-grab items-center gap-2 rounded-md px-2 py-1.5 select-none active:cursor-grabbing hover:bg-muted/50',
			dragged && 'opacity-40',
			isFolder && 'border-l-2 border-l-primary/60 bg-muted/50 font-semibold',
			depth > 0 && 'text-muted-foreground',
			isFolder && intoFolder === opts.folder!.id && 'bg-primary/10 ring-2 ring-primary/50'
		]}
		style:margin-left="{depth * 1.5}rem"
		ondragstart={(e) => {
			dragging = isFolder ? { type: 'folder', id: opts.folder!.id } : { type: 'item', key: opts.key! };
			e.dataTransfer?.setData('text/plain', label);
		}}
		ondragend={end}
		ondragover={(e) => over(e, opts.parentId, opts.index, opts.folder?.id)}
		ondrop={drop}
	>
		<GripVertical class="size-4 shrink-0 text-muted-foreground opacity-50 group-hover:opacity-100" />
		{#if isFolder}
			<button type="button" class={['flex size-4 shrink-0 cursor-pointer items-center justify-center rounded transition-transform', isOpen && 'rotate-90']} aria-label="Expand" onclick={() => toggle(opts.folder!.id)}><ChevronRight class="size-3.5" /></button>
		{:else}
			<span class="size-4 shrink-0"></span>
		{/if}
		{#if icon}<i class={['text-base', icon]}></i>{/if}
		<span class="flex-1 truncate text-sm">{label}</span>
		<DropdownMenu.Root>
			<DropdownMenu.Trigger>
				{#snippet child({ props })}
					<Button {...props} size="icon-sm" variant="ghost" aria-label={m.nav_tooltip_manage()} class="ml-auto size-6 shrink-0 opacity-0 group-hover:opacity-100"><Ellipsis class="size-3.5" /></Button>
				{/snippet}
			</DropdownMenu.Trigger>
			<DropdownMenu.Content align="end">
				{#if opts.folder}
					{@const folder = opts.folder}
					<DropdownMenu.Item onSelect={() => editFolder(folder)}>{m.nav_menu_custom_nav_edit_folder()}</DropdownMenu.Item>
					<DropdownMenu.Item class="text-destructive" onSelect={() => deleteFolder(folder.id)}>{m.nav_menu_custom_nav_delete_folder()}</DropdownMenu.Item>
				{:else if opts.key && isDashboardNavKey(opts.key)}
					{@const key = opts.key}
					<DropdownMenu.Item onSelect={() => editDashboard(key)}>{m.nav_menu_custom_nav_edit_dashboard()}</DropdownMenu.Item>
					<DropdownMenu.Item class="text-destructive" onSelect={() => deleteDashboard(key)}>{m.nav_menu_custom_nav_delete_dashboard()}</DropdownMenu.Item>
				{:else if opts.key}
					{@const key = opts.key}
					<DropdownMenu.Item onSelect={() => hide(key)}>{isToolNavKey(key) ? 'Delete' : 'Hide'}</DropdownMenu.Item>
				{/if}
			</DropdownMenu.Content>
		</DropdownMenu.Root>
	</div>
{/snippet}

<Dialog.Root bind:open={() => navLayout.editing, (v) => !v && close()}>
	<Dialog.Content class="sm:min-w-180">
		<Dialog.Header>
			<Dialog.Title>Custom Nav</Dialog.Title>
		</Dialog.Header>
		<div class="max-h-[60vh] min-h-[40vh] overflow-y-auto" role="list">
			<div class="flex flex-col gap-0.5 pr-3">
				{#each layout as entry, index (entry.type === 'folder' ? entry.id : entry.key)}
					{#if entry.type === 'folder'}
						{@render row(entry.name.trim() || 'Folder name', entry.icon || DEFAULT_FOLDER_ICON, 0, { folder: entry, parentId: null, index })}
						{#if expanded.includes(entry.id)}
							{#each entry.items as key, childIndex (key)}
								{@render row(def(key)?.label ?? key, def(key)?.icon ?? '', 1, { key, parentId: entry.id, index: childIndex })}
							{/each}
							{#if lineBefore(entry.id, entry.items.length) && entry.items.length}<div class="ml-6 h-0.5 rounded bg-primary"></div>{/if}
							{#if !entry.items.length}
								<div role="listitem" class={['mt-1 ml-6 rounded-md border border-dashed border-muted-foreground/25 p-1.5 text-sm text-muted-foreground/50', intoFolder === entry.id && 'border-primary bg-primary/10']} ondragover={(e) => overEmptyFolder(e, entry.id)} ondrop={drop}>Drop items here</div>
							{/if}
						{/if}
					{:else}
						{@render row(def(entry.key)?.label ?? entry.key, def(entry.key)?.icon ?? '', 0, { key: entry.key, parentId: null, index })}
					{/if}
				{/each}
				{#if lineBefore(null, layout.length)}<div class="h-0.5 rounded bg-primary"></div>{/if}
			</div>
			{#if hiddenItems.length}
				<div class="my-3 flex items-center gap-2 pr-3">
					<Separator class="flex-1" />
					<span class="text-xs text-muted-foreground">{m.dialog_group_tags_hidden()}</span>
					<Separator class="flex-1" />
				</div>
				<div class="flex flex-col gap-0.5 pr-3">
					{#each hiddenItems as item (item.key)}
						<button type="button" class="group flex cursor-pointer items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm text-muted-foreground hover:bg-muted/50" onclick={() => show(item.key)}>
							<span class="size-4 shrink-0"></span>
							<span class="size-4 shrink-0"></span>
							{#if item.icon}<i class={['text-base', item.icon]}></i>{/if}
							<span class="flex-1 truncate">{item.label}</span>
							<Minus class="ml-auto size-3.5 shrink-0 opacity-0 group-hover:opacity-100" />
						</button>
					{/each}
				</div>
			{/if}
		</div>
		<Dialog.Footer>
			<div class="flex w-full items-center justify-between">
				<div class="flex gap-2">
					<Button variant="outline" onclick={addFolder}>{m.nav_menu_custom_nav_new_folder()}</Button>
					<Button variant="outline" onclick={addDashboard}>{m.dashboard_new_dashboard()}</Button>
					<Button variant="ghost" class="text-destructive" onclick={reset}>{m.nav_menu_custom_nav_restore_default()}</Button>
				</div>
				<div class="flex gap-2">
					<Button variant="secondary" onclick={close}>{m.dialog_alertdialog_cancel()}</Button>
					<Button onclick={save}>{m.dialog_alertdialog_confirm()}</Button>
				</div>
			</div>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={editor.open}>
	<Dialog.Content class="sm:max-w-100">
		<Dialog.Header>
			<Dialog.Title>{editor.type === 'dashboard' ? 'Edit Dashboard' : 'Edit Folder'}</Dialog.Title>
		</Dialog.Header>
		<div class="flex flex-col gap-3">
			<Input bind:value={editor.name} placeholder={m.nav_menu_custom_nav_folder_name_placeholder()} />
			<div class="relative">
				<Input bind:value={editor.icon} placeholder="Icon class name (optional)" class="pr-9" />
				<HoverCard.Root>
					<HoverCard.Trigger class="absolute top-1/2 right-2 -translate-y-1/2 text-muted-foreground" aria-label="Icon class name (optional)"><LinkIcon class="size-3.5" /></HoverCard.Trigger>
					<HoverCard.Content side="bottom" align="end" class="w-80 text-sm leading-snug">
						<div>Find the icon you want on this site and paste its class name here, e.g. <span class="font-mono">ri-arrow-left-up-line</span></div>
						<div class="mt-2"><a class="text-blue-600" href="https://remixicon.com/" target="_blank" rel="noopener noreferrer">https://remixicon.com/</a></div>
					</HoverCard.Content>
				</HoverCard.Root>
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" onclick={() => (editor.open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button disabled={!editor.name.trim()} onclick={saveEditor}>{m.dialog_alertdialog_confirm()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<ActionDialogs bind:confirming />
