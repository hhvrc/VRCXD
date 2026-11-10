<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Table from '#lib/components/ui/table/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import type { Favorites } from '#lib/api/types.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';

	// The original's Friend, World and Avatar Import and Export dialogs. Import resolves pasted ids into
	// a table first, then adds them to one VRChat or local group; export writes CSV.
	type Kind = 'friend' | 'world' | 'avatar';
	type Slot = { type: string; name: string; displayName: string; capacity: number; count: number };
	type LocalFavorite = { groupName: string; targetId: string; targetName: string; thumbnailImageUrl?: string | null; authorId?: string | null; authorName?: string | null };
	type Favorite = Favorites['favorites'][number] & { authorId?: string | null; authorName?: string | null };
	type Row = { id: string; name: string; authorId?: string | null; authorName?: string | null; thumbnailImageUrl?: string | null; releaseStatus?: string | null };

	let {
		type,
		account,
		slots,
		localGroups,
		favorites,
		localFavorites,
		importOpen = $bindable(false),
		importText = $bindable(''),
		exportOpen = $bindable(false),
		onchange
	}: {
		type: Kind;
		account: string | null | undefined;
		slots: Slot[];
		localGroups: { name: string; count: number }[];
		favorites: Favorite[];
		localFavorites: LocalFavorite[];
		importOpen?: boolean;
		importText?: string;
		exportOpen?: boolean;
		onchange: () => void | Promise<void>;
	} = $props();

	const NOUN = { friend: 'Friend', world: 'World', avatar: 'Avatar' } as const;
	const PLURAL = { friend: 'Friends', world: 'Worlds', avatar: 'Avatars' } as const;
	const ID_NAME = { friend: 'user', world: 'world', avatar: 'avatar' } as const;
	const PATTERN = { friend: 'usr', world: 'wrld', avatar: 'avtr' } as const;
	const ALL = '__all__';
	const NONE = '__none__';
	const base = $derived(accountPath(account ?? '', '/favorites'));

	// Import

	let rows = $state.raw<Row[]>([]);
	let processing = $state(false);
	let progress = $state(0);
	let progressTotal = $state(0);
	let importing = $state(false);
	let importProgress = $state(0);
	let importProgressTotal = $state(0);
	let errors = $state('');
	let remoteGroup = $state('');
	let localGroup = $state('');
	const remoteSlot = $derived(slots.find((s) => s.name === remoteGroup));

	async function lookup(id: string): Promise<Row> {
		if (type === 'avatar') {
			const details = await api.get<{ avatar?: Row | null }>(accountPath(account ?? '', `/avatars/${encodeURIComponent(id)}/details`));
			if (!details.avatar) throw new Error('Avatar not found');
			return details.avatar;
		}
		if (type === 'world') return api.get<Row>(accountPath(account ?? '', `/worlds/${encodeURIComponent(id)}/live`));
		const user = await api.get<{ id: string; displayName: string; iconUrl?: string; userIcon?: string; profilePicOverride?: string; currentAvatarThumbnailImageUrl?: string }>(`/1/users/${encodeURIComponent(id)}`, {
			account: account ?? undefined
		});
		return { id: user.id, name: user.displayName, thumbnailImageUrl: user.iconUrl || user.userIcon || user.profilePicOverride || user.currentAvatarThumbnailImageUrl };
	}

	async function processList() {
		const regex = new RegExp(`${PATTERN[type]}_[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}`, 'g');
		const ids = [...new Set(importText.match(regex) ?? [])];
		importText = '';
		errors = '';
		processing = true;
		progress = 0;
		progressTotal = ids.length;
		for (const id of ids) {
			if (!processing || !importOpen) break;
			if (!rows.some((r) => r.id === id)) {
				try {
					const row = await lookup(id);
					rows = [...rows, row];
				} catch (err) {
					errors += `${NOUN[type]}Id: ${id}\n${ApiError.from(err).message}\n\n`;
				}
			}
			progress++;
		}
		processing = false;
		progress = 0;
		progressTotal = 0;
	}

	async function runImport() {
		if (!remoteGroup && !localGroup) return;
		importing = true;
		importProgressTotal = rows.length;
		let current: Row | undefined;
		try {
			for (const row of [...rows]) {
				if (!importing || !importOpen) break;
				current = row;
				if (remoteSlot) {
					if (favorites.some((f) => f.targetId === row.id)) throw new Error(`${NOUN[type]} is already in favorites`);
					await api.post(base, { type: remoteSlot.type, targetId: row.id, group: remoteSlot.name });
				} else {
					await api.post(`${base}/local`, { type: NOUN[type], targetId: row.id, group: localGroup });
				}
				rows = rows.filter((r) => r.id !== row.id);
				importProgress++;
			}
		} catch (err) {
			errors = `Name: ${current?.name}\n${NOUN[type]}Id: ${current?.id}\n${err instanceof Error && !(err instanceof ApiError) ? err : ApiError.from(err).message}\n\n`;
		} finally {
			importProgress = 0;
			importProgressTotal = 0;
			importing = false;
			await onchange();
		}
	}

	function resetImport() {
		importText = '';
		processing = false;
		importing = false;
		rows = [];
		errors = '';
	}

	function viewRow(row: Row) {
		if (type === 'friend') userDialog.open(row.id);
		else if (type === 'world') userDialog.openWorld(row.id);
		else userDialog.openAvatar(row.id);
	}

	// Export

	const OPTIONS = [
		{ label: 'ID', value: 'id' },
		{ get label() { return m.table_import_name(); }, value: 'name' },
		{ label: 'Author ID', value: 'authorId' },
		{ label: 'Author Name', value: 'authorName' },
		{ label: 'Thumbnail', value: 'thumbnailImageUrl' }
	] as const;
	let exportFields = $state<string[]>(['ID', 'Name']);
	let exportRemote = $state(ALL);
	let exportLocal = $state(NONE);

	function csvField(value: unknown): string {
		if (value === null || value === undefined) return '';
		const text = String(value);
		// eslint-disable-next-line no-control-regex
		return /[\x00-\x1f,"]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
	}

	const exportRows = $derived.by((): Row[] => {
		const fromFavorite = (f: Favorite): Row => ({ id: f.targetId, name: f.targetName, authorId: f.authorId, authorName: f.authorName, thumbnailImageUrl: f.thumbnailImageUrl });
		const fromLocal = (f: LocalFavorite): Row => {
			const known = favorites.find((x) => x.targetId === f.targetId);
			// Local-only ones carry the stored world's or avatar's author too (upstream issue 984).
			return known ? fromFavorite(known) : { id: f.targetId, name: f.targetName, authorId: f.authorId, authorName: f.authorName, thumbnailImageUrl: f.thumbnailImageUrl };
		};
		if (exportRemote !== ALL) return favorites.filter((f) => f.groupName === exportRemote).map(fromFavorite);
		if (exportLocal !== NONE) return localFavorites.filter((f) => f.groupName === exportLocal).map(fromLocal);
		return [...favorites.map(fromFavorite), ...localFavorites.map(fromLocal)];
	});

	const exportText = $derived.by(() => {
		if (type === 'friend') return ['UserID,Name', ...exportRows.map((r) => `${csvField(r.id)},${csvField(r.name)}`)].reverse().join('\n');
		const fields = OPTIONS.filter((o) => exportFields.includes(o.label)).map((o) => o.value);
		return [exportFields.join(','), ...exportRows.map((r) => fields.map((f) => csvField(r[f])).join(','))].reverse().join('\n');
	});

	let copied = $state('');
	function copyExport(event: MouseEvent) {
		(event.currentTarget as HTMLTextAreaElement).select();
		navigator.clipboard.writeText(exportText).then(
			() => (copied = 'Copied successfully!'),
			() => (copied = 'Copy failed!')
		);
	}

	$effect(() => {
		if (exportOpen) {
			exportRemote = ALL;
			exportLocal = NONE;
			copied = '';
		}
	});
	$effect(() => {
		if (!importOpen && (processing || importing)) resetImport();
	});
	// The original clears the table each time the dialog opens, and processes ids handed to it (the
	// edit mode's Copy) straight away.
	let wasOpen = false;
	$effect(() => {
		const open = importOpen;
		if (open && !wasOpen) {
			untrack(() => {
				rows = [];
				errors = '';
				if (importText) void processList();
			});
		}
		wasOpen = open;
	});
</script>

<Dialog.Root bind:open={importOpen}>
	<Dialog.Content class="bg-background sm:max-w-xl">
		<Dialog.Header><Dialog.Title>{NOUN[type]} Favorites Import</Dialog.Title></Dialog.Header>
		<div class="flex items-center justify-between">
			<div class="text-xs">Enter a list of {ID_NAME[type]} IDs</div>
			<div class="flex items-center">
				{#if progress}
					<div class="flex items-center">Progress: {progress} / {progressTotal}<Spinner class="mx-2 inline-block" /></div>
				{/if}
				{#if processing}
					<Button size="sm" variant="secondary" onclick={() => (processing = false)}>{m.dialog_group_transfer_cancel()}</Button>
				{:else}
					<Button size="sm" disabled={!importText} onclick={processList}>{m.dialog_world_import_process_list()}</Button>
				{/if}
			</div>
		</div>
		<textarea bind:value={importText} rows="10" class="mt-2 w-full resize-none rounded-md border border-input bg-background px-3 py-2 font-mono text-xs"></textarea>
		<div class="mt-1.5 flex items-center justify-between">
			<div>
				<div class="flex items-center gap-2">
					<Select.Root type="single" value={remoteGroup} onValueChange={(v) => { remoteGroup = v; localGroup = ''; }}>
						<Select.Trigger size="sm">{remoteSlot ? `${remoteSlot.displayName} (${remoteSlot.count}/${remoteSlot.capacity})` : 'Select Group'}</Select.Trigger>
						<Select.Content>
							{#each slots as slot (slot.name)}
								<Select.Item value={slot.name} disabled={slot.count >= slot.capacity}>{slot.displayName} ({slot.count}/{slot.capacity})</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
					<Select.Root type="single" value={localGroup} onValueChange={(v) => { localGroup = v; remoteGroup = ''; }}>
						<Select.Trigger size="sm">{localGroup ? `${localGroup} (${localGroups.find((g) => g.name === localGroup)?.count ?? 0})` : 'Select Group'}</Select.Trigger>
						<Select.Content>
							{#each localGroups as group (group.name)}
								<Select.Item value={group.name}>{group.name} ({group.count})</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
				{#if remoteSlot}<span class="ml-1.5 text-sm">{rows.length} / {remoteSlot.capacity - remoteSlot.count}</span>{/if}
			</div>
			<div>
				<Button size="sm" variant="secondary" class="mr-2" onclick={() => (rows = [])}>{m.dialog_world_import_clear_table()}</Button>
				<Button size="sm" disabled={rows.length === 0 || (!remoteGroup && !localGroup) || importing} onclick={runImport}>Import {PLURAL[type]}</Button>
			</div>
		</div>
		{#if importProgress}
			<span class="m-2 flex items-center"><Spinner class="mx-2 inline-block" />Import Progress: {importProgress}/{importProgressTotal}</span>
		{/if}
		{#if errors}
			<div>
				<Button size="sm" variant="secondary" onclick={() => (errors = '')}>{m.dialog_note_export_clear_errors()}</Button>
				<h2 class="my-1.5 font-bold">{m.dialog_note_export_errors()}</h2>
				<pre class="max-h-40 overflow-auto text-xs whitespace-pre-wrap">{errors}</pre>
			</div>
		{/if}
		<div class="max-h-[40vh] overflow-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head class="w-17.5">{m.dialog_group_post_edit_image()}</Table.Head>
						<Table.Head>{m.view_favorite_worlds_search_mode_name()}</Table.Head>
						{#if type !== 'friend'}<Table.Head>{m.dialog_screenshot_metadata_col_author()}</Table.Head><Table.Head class="w-17.5">{m.view_feed_filters_Status()}</Table.Head>{/if}
						<Table.Head class="w-22.5 text-right">{m.dialog_registry_backup_action()}</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each rows as row (row.id)}
						<Table.Row>
							<Table.Cell>
								{#if row.thumbnailImageUrl}<button type="button" class="cursor-pointer" aria-label="View image" onclick={() => imageViewer.show(row.thumbnailImageUrl)}><img src={imageSrc(row.thumbnailImageUrl)} alt="" loading="lazy" class="h-7.5 w-10 rounded object-cover" /></button>{/if}
							</Table.Cell>
							<Table.Cell><button type="button" class="max-w-50 cursor-pointer truncate" title={row.name} onclick={() => viewRow(row)}>{row.name}</button></Table.Cell>
							{#if type !== 'friend'}
								<Table.Cell>
									<button type="button" class="max-w-40 cursor-pointer truncate" title={row.authorName ?? ''} onclick={() => row.authorId && userDialog.open(row.authorId)}>{row.authorName}</button>
								</Table.Cell>
								<Table.Cell>
									{@const status = row.releaseStatus ?? ''}
									<span class={status === 'public' ? 'text-status-online' : status === 'private' ? 'text-destructive' : ''}>{status ? status[0].toUpperCase() + status.slice(1) : ''}</span>
								</Table.Cell>
							{/if}
							<Table.Cell class="text-right">
								<Button size="icon-sm" variant="ghost" aria-label={m.view_favorite_delete_tooltip()} onclick={() => (rows = rows.filter((r) => r.id !== row.id))}><Trash2 /></Button>
							</Table.Cell>
						</Table.Row>
					{:else}
						<Table.Row><Table.Cell colspan={type === 'friend' ? 3 : 5} class="text-center text-muted-foreground">{m.common_no_data()}</Table.Cell></Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={exportOpen}>
	<Dialog.Content class="bg-background sm:max-w-xl">
		<Dialog.Header><Dialog.Title>{NOUN[type]} Favorites Export</Dialog.Title></Dialog.Header>
		{#if type !== 'friend'}
			<div class="mb-2 flex flex-col gap-2">
				{#each OPTIONS as option (option.value)}
					<label class="inline-flex items-center gap-2 text-sm">
						<Checkbox
							checked={exportFields.includes(option.label)}
							onCheckedChange={(checked) => (exportFields = checked ? (exportFields.includes(option.label) ? exportFields : [...exportFields, option.label]) : exportFields.filter((l) => l !== option.label))}
						/>
						<span>{option.label}</span>
					</label>
				{/each}
			</div>
		{/if}
		<div class={type === 'friend' ? 'flex flex-col gap-4' : 'flex items-center gap-2'}>
			<Select.Root type="single" value={exportRemote} onValueChange={(v) => { exportRemote = v; exportLocal = NONE; }}>
				<Select.Trigger size="sm">{exportRemote === ALL ? 'All Favorites' : (() => { const s = slots.find((x) => x.name === exportRemote); return s ? `${s.displayName} (${s.count}/${s.capacity})` : exportRemote; })()}</Select.Trigger>
				<Select.Content>
					<Select.Item value={ALL}>{m.dialog_world_export_all_favorites()}</Select.Item>
					{#each slots as slot (slot.name)}<Select.Item value={slot.name}>{slot.displayName} ({slot.count}/{slot.capacity})</Select.Item>{/each}
				</Select.Content>
			</Select.Root>
			<Select.Root type="single" value={exportLocal} onValueChange={(v) => { exportLocal = v; exportRemote = ALL; }}>
				<Select.Trigger size="sm">{exportLocal === NONE ? 'Select Group' : `${exportLocal} (${localGroups.find((g) => g.name === exportLocal)?.count ?? 0})`}</Select.Trigger>
				<Select.Content>
					<Select.Item value={NONE}>{m.dialog_gallery_select_none()}</Select.Item>
					{#each localGroups as group (group.name)}<Select.Item value={group.name}>{group.name} ({group.count})</Select.Item>{/each}
				</Select.Content>
			</Select.Root>
		</div>
		<textarea readonly value={exportText} rows="15" class="mt-4 w-full resize-none rounded-md border border-input bg-background px-3 py-2 font-mono text-xs" onclick={copyExport}></textarea>
		{#if copied}<p class="text-xs text-muted-foreground">{copied}</p>{/if}
	</Dialog.Content>
</Dialog.Root>
