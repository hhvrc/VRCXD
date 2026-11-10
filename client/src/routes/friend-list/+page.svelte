<script lang="ts">
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import EyeOff from '@lucide/svelte/icons/eye-off';
	import X from '@lucide/svelte/icons/x';
	import UserIcon from '@lucide/svelte/icons/user';
	import UserMinus from '@lucide/svelte/icons/user-minus';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import FavoritesOnlyToggle from '#lib/components/common/favorites-only-toggle.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import type { FriendDetail, FriendDetails } from '#lib/api/types.js';
	import removeConfusables, { removeWhitespace } from '#lib/people/confusables.js';
	import { formatDateFilter, getFaviconUrl, replaceBioSymbols, statusClass, timeToText } from '#lib/helpers/format.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { hueToHex, languageClass, languagesOf, sortStatus, trustInfo } from '#lib/people/user-display.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';

	// The original's Friend List over GET /1/accounts/{id}/friends/details. Mutuals load on the
	// Mutual Friends chart, as the original moved them; the button here reads the counts again.
	const FILTERS = ['Display Name', 'User Name', 'Rank', 'Status', 'Bio', 'Note', 'Memo'].map((value) => ({ value, label: value }));
	const DEFAULT_FILTERS = ['Display Name', 'Rank', 'Status', 'Bio', 'Note', 'Memo'];
	const collator = new Intl.Collator(undefined, { sensitivity: 'base' });

	let friends = $state<FriendDetail[]>([]);
	let mutualOptOut = $state(false);
	let loading = $state(false);
	let error = $state('');
	let notice = $state('');
	let vipOnly = $state(false);
	let filters = $state<string[]>([]);
	let search = $state('');
	// The original applies a search 150 ms after typing stops, or at once on Enter.
	let query = $state('');
	let searchTimer: ReturnType<typeof setTimeout> | undefined;
	function scheduleSearch() {
		clearTimeout(searchTimer);
		searchTimer = setTimeout(() => (query = search), 150);
	}
	function applySearch() {
		clearTimeout(searchTimer);
		query = search;
	}
	let bulkMode = $state(false);
	let selected = $state<Set<string>>(new Set());
	let confirming = $state<Confirming | undefined>();
	let bulkDone = $state<number | undefined>();
	let loadTotal = $state(0);
	let loadCurrent = $state(0);
	let loadOpen = $state(false);
	let loadCancelled = false;

	const dark = $derived(appearance.isDarkMode);
	const loadPercent = $derived(loadTotal ? Math.min(100, Math.round((loadCurrent / loadTotal) * 100)) : 0);

	function includes(text: string | null | undefined, query: string) {
		if (!text || !query) return false;
		const size = query.length;
		for (let i = 0; i + size <= text.length; i++) {
			if (collator.compare(text.slice(i, i + size), query) === 0) return true;
		}
		return false;
	}

	function matches(friend: FriendDetail, query: string, active: string[]): boolean {
		const cleaned = removeWhitespace(query);
		if (active.includes('Display Name') && (includes(friend.displayName, cleaned) || includes(removeConfusables(friend.displayName), cleaned))) return true;
		if (active.includes('Memo') && includes(friend.memo, query)) return true;
		if (active.includes('Note') && includes(friend.note, query)) return true;
		if (active.includes('Bio') && includes(replaceBioSymbols(friend.bio), query)) return true;
		if (active.includes('Status') && includes(friend.statusDescription, query)) return true;
		return active.includes('Rank') && friend.trustLevel.toUpperCase().includes(query.toUpperCase());
	}

	const shown = $derived.by(() => {
		const active = filters.length ? filters : DEFAULT_FILTERS;
		return friends.filter((friend) => (!vipOnly || friend.isFavorite) && (!query || matches(friend, query, active)));
	});

	async function load() {
		const account = vrchatAuth.ownerUserId;
		if (!account) return;
		loading = true;
		try {
			const data = await api.get<FriendDetails>(accountPath(account, '/friends/details'));
			friends = data.friends;
			mutualOptOut = data.mutualOptOut;
			error = '';
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	async function unfriend(id: string) {
		await api.delete(ownPath(`/friends/${encodeURIComponent(id)}`));
		friends = friends.filter((f) => f.userId !== id);
	}

	function confirmUnfriend(id: string) {
		confirming = {
			get description() { return m.confirm_unfriend(); },
			run: async () => {
				confirming = undefined;
				try {
					await unfriend(id);
				} catch (err) {
					error = ApiError.from(err).message;
				}
			}
		};
	}

	function toggleSelected(id: string) {
		const next = new Set(selected);
		if (!next.delete(id)) next.add(id);
		selected = next;
	}

	function confirmBulk() {
		const pending = shown.filter((f) => selected.has(f.userId));
		if (!pending.length) return;
		confirming = {
			title: `Delete ${pending.length} friends?`,
			description: `Are you sure you want to delete ${pending.length} friends?\nThis can negatively affect your trust rank,\nThis action cannot be undone.\n\n${pending.map((f) => f.displayName).join('\n')}`,
			run: async () => {
				confirming = undefined;
				try {
					for (const friend of pending) {
						await unfriend(friend.userId);
						const next = new Set(selected);
						next.delete(friend.userId);
						selected = next;
					}
					bulkDone = pending.length;
				} catch (err) {
					error = ApiError.from(err).message;
				}
			}
		};
	}

	async function loadMissing() {
		await load();
		const toFetch = friends.filter((f) => f.needsLoad);
		loadTotal = toFetch.length;
		loadCurrent = 0;
		notice = '';
		if (!toFetch.length) {
			notice = 'Missing entries loaded';
			return;
		}
		loadCancelled = false;
		loadOpen = true;
		for (const friend of toFetch) {
			if (loadCancelled) break;
			loadCurrent += 1;
			try {
				await api.post(ownPath(`/friends/${encodeURIComponent(friend.userId)}/load`));
			} catch (err) {
				console.error(err);
			}
		}
		loadOpen = false;
		await load();
		if (!loadCancelled) notice = 'Missing entries loaded';
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(load);
		return watchLive(['friendship'], load, 750);
	});

	function byNumber(a: FriendDetail, b: FriendDetail) {
		return a.friendNumber - b.friendNumber;
	}
	function then(result: number, a: FriendDetail, b: FriendDetail) {
		return result !== 0 ? result : byNumber(a, b);
	}
	function byText(pick: (f: FriendDetail) => string | null | undefined) {
		return (a: FriendDetail, b: FriendDetail) => {
			const [left, right] = [pick(a), pick(b)];
			if (!left || !right) return then(!left && !right ? 0 : !left ? -1 : 1, a, b);
			return then(left.toLowerCase().localeCompare(right.toLowerCase()), a, b);
		};
	}
	function byCount(pick: (f: FriendDetail) => number) {
		return (a: FriendDetail, b: FriendDetail) => then(pick(a) - pick(b), a, b);
	}
	function languageKey(friend: FriendDetail) {
		return JSON.stringify(languagesOf(friend.tags).map((l) => l.value).sort());
	}

	const columns = $derived<Column<FriendDetail>[]>([
		{ id: 'leftSpacer', size: 20, enableResizing: false },
		...(bulkMode ? [{ id: 'bulkSelect', size: 55, cell: selectCell }] : []),
		{ id: 'friendNumber', get label() { return m.table_friendList_no(); }, size: 100, sortable: true, descFirst: true, compare: byNumber, cell: numberCell },
		{ id: 'avatar', get label() { return m.table_playerList_avatar(); }, size: 90, cell: avatarCell },
		{ id: 'displayName', get label() { return m.table_playerList_displayName(); }, size: 200, sortable: true, compare: byText((f) => f.displayName), cell: nameCell },
		{ id: 'rank', get label() { return m.table_playerList_rank(); }, size: 140, sortable: true, compare: byCount((f) => trustInfo(f.tags, f.developerType).sortNum), cell: rankCell },
		{ id: 'status', get label() { return m.table_import_status(); }, minSize: 200, stretch: true, sortable: true, compare: (a, b) => then(sortStatus(a.status ?? 'offline', b.status ?? 'offline'), a, b), cell: statusCell },
		{ id: 'language', get label() { return m.view_login_language(); }, size: 130, sortable: true, compare: (a, b) => then(languageKey(a).localeCompare(languageKey(b)), a, b), cell: languageCell },
		{ id: 'bioLink', get label() { return m.table_playerList_bioLink(); }, size: 130, cell: bioCell },
		{ id: 'joinCount', get label() { return m.table_friendList_joinCount(); }, size: 120, class: 'text-right', sortable: true, compare: byCount((f) => f.joinCount), cell: joinCell },
		{ id: 'timeTogether', get label() { return m.table_friendList_timeTogether(); }, size: 140, class: 'text-right', sortable: true, compare: byCount((f) => f.timeTogetherMs), cell: timeCell },
		{ id: 'lastSeen', get label() { return m.table_friendList_lastSeen(); }, size: 170, sortable: true, compare: byText((f) => f.lastSeen), cell: seenCell },
		{ id: 'mutualFriends', get label() { return m.table_friendList_mutualFriends(); }, size: 120, class: 'text-right', sortable: true, compare: byCount((f) => f.mutualCount), cell: mutualCell },
		{ id: 'lastActivity', get label() { return m.table_friendList_lastActivity(); }, size: 200, sortable: true, compare: byText((f) => f.lastActivity), cell: activityCell },
		{ id: 'lastLogin', get label() { return m.table_friendList_lastLogin(); }, size: 200, sortable: true, compare: byText((f) => f.lastLogin), cell: loginCell },
		{ id: 'dateJoined', get label() { return m.table_friendList_dateJoined(); }, size: 120, sortable: true, compare: byText((f) => f.dateJoined), cell: joinedCell },
		{ id: 'unfriend', get label() { return m.view_friend_log_filters_Unfriend(); }, size: 100, class: 'text-center', cell: unfriendCell }
	]);
</script>

{#snippet selectCell(row: FriendDetail)}
	<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
	<div class="flex items-center justify-center" onclick={(event) => event.stopPropagation()}>
		<Checkbox checked={selected.has(row.userId)} aria-label="Select {row.displayName}" onCheckedChange={() => toggleSelected(row.userId)} />
	</div>
{/snippet}

{#snippet numberCell(row: FriendDetail)}
	<span>{row.friendNumber || ''}</span>
{/snippet}

{#snippet avatarCell(row: FriendDetail)}
	<div class="flex items-center">
		<Avatar.Root class="size-6 rounded-full">
			<Avatar.Image src={imageSrc(row.thumbnailImageUrl)} class="object-cover" loading="lazy" />
			<Avatar.Fallback><UserIcon class="size-3 text-muted-foreground" /></Avatar.Fallback>
		</Avatar.Root>
	</div>
{/snippet}

{#snippet nameCell(row: FriendDetail)}
	<span class="name" style={tableSettings.randomUserColours ? `color: ${hueToHex(row.nameHue, dark)}` : undefined}>{row.displayName}</span>
{/snippet}

{#snippet rankCell(row: FriendDetail)}
	{@const trust = trustInfo(row.tags, row.developerType)}
	<span class="name" style="color: {trust.color}">{row.trustLevel}</span>
{/snippet}

{#snippet statusCell(row: FriendDetail)}
	<span class="flex items-center">
		{#if row.status && row.status !== 'offline'}<i class="x-user-status mr-1 {statusClass(row.status)}"></i>{/if}
		<span>{row.statusDescription ?? ''}</span>
	</span>
{/snippet}

{#snippet languageCell(row: FriendDetail)}
	<div class="flex items-center">
		{#each languagesOf(row.tags) as language (language.key)}
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<span {...props} class="flags mr-1 inline-block {languageClass(language.key)}"></span>
					{/snippet}
				</Tooltip.Trigger>
				<Tooltip.Content side="top">{language.value} ({language.key})</Tooltip.Content>
			</Tooltip.Root>
		{/each}
	</div>
{/snippet}

{#snippet bioCell(row: FriendDetail)}
	<div class="flex items-center">
		{#each row.bioLinks.filter(Boolean) as link, index (index)}
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<a {...props} href={link} target="_blank" rel="noopener noreferrer" onclick={(event) => event.stopPropagation()}>
							<img src={getFaviconUrl(link)} alt="" class="mr-1 size-4 cursor-pointer align-middle" loading="lazy" />
						</a>
					{/snippet}
				</Tooltip.Trigger>
				<Tooltip.Content>{link}</Tooltip.Content>
			</Tooltip.Root>
		{/each}
	</div>
{/snippet}

{#snippet joinCell(row: FriendDetail)}
	{row.joinCount || ''}
{/snippet}

{#snippet timeCell(row: FriendDetail)}
	{#if row.timeTogetherMs}<span>{timeToText(row.timeTogetherMs)}</span>{/if}
{/snippet}

{#snippet seenCell(row: FriendDetail)}
	{#if row.lastSeen}<span>{formatDateFilter(row.lastSeen, 'long')}</span>{/if}
{/snippet}

{#snippet mutualCell(row: FriendDetail)}
	{#if row.mutualCount || row.mutualOptedOut}
		<span class="inline-flex items-center gap-1">
			{row.mutualCount || ''}
			{#if row.mutualOptedOut}
				<Tooltip.Root>
					<Tooltip.Trigger>
						{#snippet child({ props })}
							<span {...props}><EyeOff class="size-3.5 text-muted-foreground" /></span>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="top">This user has disabled mutual friends sharing</Tooltip.Content>
				</Tooltip.Root>
			{/if}
		</span>
	{/if}
{/snippet}

{#snippet activityCell(row: FriendDetail)}
	<span>{formatDateFilter(row.lastActivity, 'long')}</span>
{/snippet}

{#snippet loginCell(row: FriendDetail)}
	<span>{formatDateFilter(row.lastLogin, 'long')}</span>
{/snippet}

{#snippet joinedCell(row: FriendDetail)}
	<span>{row.dateJoined ?? ''}</span>
{/snippet}

{#snippet unfriendCell(row: FriendDetail)}
	<button
		type="button"
		aria-label="Unfriend {row.displayName}"
		class="inline-block"
		onclick={(event) => {
			event.stopPropagation();
			confirmUnfriend(row.userId);
		}}
	>
		<UserMinus class="inline-block size-4 text-destructive" />
	</button>
{/snippet}

<svelte:head>
	<title>Friend List · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="flex h-full min-h-0 w-full flex-col">
		{#if !vrchatAuth.ownerUserId}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see your friends.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else}
			<DataTable
				rows={shown}
				{columns}
				{loading}
				rowKey={(row) => row.userId}
				persistKey="friendList"
				initialSort={{ id: 'friendNumber', desc: true }}
				resetKey={query}
				filtered={friends.length > 0}
				rowClass={() => 'cursor-pointer'}
				onRowClick={(row) => userDialog.open(row.userId)}
			>
				{#snippet toolbar()}
					<div class="flex items-center justify-between">
						<div class="mr-2 flex flex-none items-center">
							<FavoritesOnlyToggle pressed={vipOnly} onPressedChange={(value) => (vipOnly = value)} />
							<MultiSelect class="mx-2 w-37.5" values={filters} options={FILTERS} placeholder={m.view_my_avatars_filter()} onchange={(value) => (filters = value)} />
							<div class="relative w-62.5">
								<Input class="h-8 pr-8" placeholder={m.view_favorite_worlds_search()} bind:value={search} oninput={scheduleSearch} onkeydown={(event: KeyboardEvent) => event.key === 'Enter' && applySearch()} />
								{#if search}
									<button type="button" aria-label={m.view_favorite_clear()} class="absolute top-1/2 right-2 -translate-y-1/2 cursor-pointer text-muted-foreground" onclick={() => { search = ''; applySearch(); }}>
										<X class="size-4" />
									</button>
								{/if}
							</div>
						</div>
						<div class="flex items-center">
							{#if notice}<span class="mr-2 text-xs text-muted-foreground">{notice}</span>{/if}
							{#if bulkMode}
								<Button variant="outline" class="mr-2" onclick={confirmBulk}>{m.view_friend_list_bulk_unfriend_selection()}</Button>
							{/if}
							<div class="mr-2 flex items-center">
								<span class="name mr-2 text-xs">{m.view_friend_list_bulk_unfriend()}</span>
								<Switch
									checked={bulkMode}
									aria-label={m.view_friend_list_bulk_unfriend()}
									onCheckedChange={(value) => {
										bulkMode = value;
										if (!value) selected = new Set();
									}}
								/>
							</div>
							<Button variant="outline" class="mr-2" disabled={mutualOptOut} onclick={load}>{m.view_friend_list_load_mutual_friends()}</Button>
							<Button variant="outline" onclick={loadMissing}>{m.view_friend_list_load()}</Button>
						</div>
					</div>
					{#if error}<p class="mt-2 text-sm text-destructive">{error}</p>{/if}
				{/snippet}
			</DataTable>
		{/if}
	</div>
</Tooltip.Provider>

<Dialog.Root bind:open={loadOpen}>
	<Dialog.Content
		class="sm:max-w-105"
		showCloseButton={false}
		interactOutsideBehavior="ignore"
		escapeKeydownBehavior="ignore"
	>
		<Dialog.Header><Dialog.Title>{m.view_friend_list_load()}</Dialog.Title></Dialog.Header>
		<div class="mb-2">{m.view_friend_list_load_dialog_message()}</div>
		<div class="flex items-center gap-2">
			<Progress value={loadPercent} class="h-4 w-full" />
			<span class="w-10 text-right text-xs">{loadPercent}%</span>
		</div>
		<div class="mt-2 text-right"><span>{loadCurrent} / {loadTotal}</span></div>
		<Dialog.Footer>
			<Button
				variant="secondary"
				onclick={() => {
					loadCancelled = true;
					loadOpen = false;
				}}>{m.view_login_qr_cancel()}</Button
			>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root open={bulkDone !== undefined} onOpenChange={(open) => { if (!open) bulkDone = undefined; }}>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header><Dialog.Title>Bulk Unfriend Complete</Dialog.Title></Dialog.Header>
		<p class="text-sm">Unfriended {bulkDone} friends.</p>
		<Dialog.Footer><Button onclick={() => (bulkDone = undefined)}>{m.dialog_alertdialog_ok()}</Button></Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<ActionDialogs bind:confirming />
