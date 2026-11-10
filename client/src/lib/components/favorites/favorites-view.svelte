<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { launchHandoff } from '#lib/instances/launch-handoff.svelte.js';
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import UserContextMenu from '#lib/components/common/user-context-menu.svelte';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import { instanceDialogs, canOpenInGame, createAndSelfInvite, canInvite, desktop, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { goto } from '$app/navigation';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { nameColour } from '#lib/people/user-colour.js';
	import { untrack } from 'svelte';
	import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down';
	import Ellipsis from '@lucide/svelte/icons/ellipsis';
	import ImageIcon from '@lucide/svelte/icons/image';
	import MoreHorizontal from '@lucide/svelte/icons/ellipsis';
	import Plus from '@lucide/svelte/icons/plus';
	import RefreshCcw from '@lucide/svelte/icons/refresh-ccw';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import UserIcon from '@lucide/svelte/icons/user';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import FavoriteButton from '#lib/components/favorites/favorite-button.svelte';
	import FavoritesTransfer from '#lib/components/favorites/favorites-transfer.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { avatarHref, imageSrc, worldHref } from '#lib/api/links.js';
	import type { Favorites } from '#lib/api/types.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's FavoritesFriend, FavoritesWorld and FavoritesAvatar pages: VRChat's groups and
	// the local ones on the left, the chosen group's cards on the right.
	type Kind = 'friend' | 'world' | 'avatar';
	type Favorite = Favorites['favorites'][number] & {
		createdAt?: string | null;
		authorId?: string | null;
		authorName?: string | null;
		releaseStatus?: string | null;
		tags?: string[] | null;
		status?: string | null;
		statusDescription?: string | null;
		state?: string | null;
		location?: string | null;
		travelingToLocation?: string | null;
		missing?: boolean;
	};
	type Slot = { type: string; name: string; displayName: string; capacity: number; count: number; visibility: string };
	type LocalGroup = { name: string; count: number };
	type LocalFavorite = { groupName: string; targetId: string; targetName: string; thumbnailImageUrl?: string | null; authorName?: string | null; authorId?: string | null; releaseStatus?: string | null; tags?: string[] | null };
	type HistoryEntry = { id: string; name: string; authorId?: string | null; authorName?: string | null; thumbnailImageUrl?: string | null; lastUsedAt: string; isCurrent: boolean };
	type Card = { id: string; name: string; image?: string | null; favorite?: Favorite; local?: boolean; history?: HistoryEntry };

	let { type }: { type: Kind } = $props();

	const KEY = { friend: 'Friend', world: 'World', avatar: 'Avatar' }[untrack(() => type)];
	const TYPE_ENUM = { friend: 'Friend', world: 'World', avatar: 'Avatar' } as const;
	const VISIBILITIES = ['public', 'friends', 'private'] as const;
	const VISIBILITY_LABEL: Record<string, string> = { public: 'Public', friends: 'Friends', private: 'Private' };

	let favorites = $state.raw<Favorite[]>([]);
	let slots = $state.raw<Slot[]>([]);
	let localGroups = $state.raw<LocalGroup[]>([]);
	let localFavorites = $state.raw<LocalFavorite[]>([]);
	let loading = $state(false);
	let notice = $state('');
	let selected = $state<{ kind: 'remote' | 'local' | 'history'; key: string } | null>(null);
	let history = $state.raw<HistoryEntry[]>([]);
	let checking = $state<{ current: number; total: number } | null>(null);
	let search = $state('');
	let searchMode = $state<'name' | 'tag'>('name');
	// Shared with Settings > Interface > Sort Favorites by.
	const sortByDate = $derived(appearance.sortFavorites.value);
	// The original's world Players sort: not stored, and the counts are VRChat's current ones (upstream issue 674).
	let sortPlayers = $state(false);
	let occupants = $state<Record<string, number>>({});
	const byPlayers = (a: Card, b: Card) => (occupants[b.id] ?? -1) - (occupants[a.id] ?? -1);

	async function loadOccupants() {
		const ids = [...new Set(localFavorites.map((f) => f.targetId))];
		try {
			const query = new URLSearchParams();
			for (const id of ids) query.append('ids', id);
			occupants = await api.get<Record<string, number>>(`${accountPath(account, '/favorites/world-occupants')}?${query}`);
		} catch (err) {
			notice = ApiError.from(err).message;
		}
	}
	let editMode = $state(false);
	let chosen = $state<string[]>([]);
	let creating = $state(false);
	let newGroup = $state('');
	let scale = $state(readSetting<number>(`VRCX_Favorites${KEY}CardScale`, 1));
	let spacing = $state(readSetting<number>(`VRCX_Favorites${KEY}CardSpacing`, 1));
	let panelWidth = $state(Math.min(360, readSetting<number>(`VRCX_Favorites${KEY}Splitter`, 260)));
	let confirming = $state<Confirming | undefined>();
	let boopFor = $state<Card | undefined>();
	let boopOpen = $state(false);
	let renaming = $state<{ title: string; value: string; save: (name: string) => Promise<unknown> } | undefined>();
	let editTarget = $state<string | undefined>();
	let editOpen = $state(false);
	let importOpen = $state(false);
	let importText = $state('');
	// A vrcx://import/<type>/... launch command opens the import with its text.
	$effect(() => {
		const pending = launchHandoff.import;
		if (!pending || pending.type !== type) return;
		launchHandoff.import = null;
		importText = pending.text;
		importOpen = true;
	});
	let exportOpen = $state(false);
	let currentLocation = $state<string | null>(null);

	const account = $derived(vrchatAuth.ownerUserId);
	const base = $derived(accountPath(account, '/favorites'));
	const searching = $derived(search.trim().length >= 3);
	const hasSearchInput = $derived(search.trim().length > 0);
	const activeSlot = $derived(selected?.kind === 'remote' ? slots.find((s) => s.name === selected!.key) : undefined);
	const activeLocal = $derived(selected?.kind === 'local' ? selected.key : undefined);
	const historySelected = $derived(type === 'avatar' && selected?.kind === 'history');
	const historyCards = $derived(
		historySelected
			? history.map((h): Card => ({ id: h.id, name: h.name, image: h.thumbnailImageUrl, history: h, favorite: favorites.find((x) => x.targetId === h.id) ?? ({ targetId: h.id, targetName: h.name, authorName: h.authorName } as Favorite) }))
			: []
	);

	function compare(a: Card, b: Card): number {
		if (sortPlayers) return byPlayers(a, b);
		if (sortByDate) return (b.favorite?.createdAt ?? '').localeCompare(a.favorite?.createdAt ?? '');
		return a.name.toLowerCase().localeCompare(b.name.toLowerCase());
	}

	const toCard = (f: Favorite): Card => ({ id: f.targetId, name: f.targetName, image: f.thumbnailImageUrl, favorite: f });
	/**
	 * A local favorite as a card. Like the original, it shows what is known of the world, avatar or
	 * friend: the stored author and release status, or the friend's live status from the sidebar. Only
	 * one the server never stored (its name is the id) counts as unavailable.
	 */
	function localCard(f: LocalFavorite): Card {
		const remote = favorites.find((x) => x.targetId === f.targetId);
		if (remote) return { id: f.targetId, name: f.targetName, image: f.thumbnailImageUrl, local: true, favorite: remote };
		const friend = type === 'friend' ? sidebarData.friends.find((x) => x.userId === f.targetId) : undefined;
		const favorite: Favorite = {
			id: `local:${f.targetId}`,
			type,
			targetId: f.targetId,
			targetName: f.targetName,
			thumbnailImageUrl: f.thumbnailImageUrl ?? friend?.thumbnailImageUrl ?? undefined,
			authorId: f.authorId,
			authorName: f.authorName,
			releaseStatus: f.releaseStatus,
			tags: f.tags,
			status: friend?.status,
			statusDescription: friend?.statusDescription,
			state: friend?.state,
			location: friend?.location,
			travelingToLocation: friend?.travelingToLocation,
			missing: f.targetName === f.targetId
		};
		return { id: f.targetId, name: friend?.displayName ?? f.targetName, image: favorite.thumbnailImageUrl, local: true, favorite };
	}
	const groupCards = $derived(activeSlot ? favorites.filter((f) => f.groupName === activeSlot.name).map(toCard).sort(compare) : []);
	const localCards = $derived(
		activeLocal !== undefined
			? localFavorites
					.filter((f) => f.groupName === activeLocal)
					.map(localCard)
					.sort((a, b) => (sortPlayers ? byPlayers(a, b) : 0))
			: []
	);
	const searchCards = $derived.by(() => {
		if (!searching) return [];
		const term = search.trim().toLowerCase();
		// The original searches the local groups and VRChat's alike, by name or author, or for worlds by
		// author tag (upstream issue 735).
		const matches = (name: string, authorName?: string | null, tags?: string[] | null) =>
			type === 'world' && searchMode === 'tag'
				? (tags ?? []).some((tag) => tag.startsWith('author_tag_') && tag.substring(11).toLowerCase().includes(term))
				: name.toLowerCase().includes(term) || (authorName ?? '').toLowerCase().includes(term);
		const seen = new Set<string>();
		const found: Card[] = [];
		for (const f of localFavorites) {
			if (seen.has(f.targetId) || !matches(f.targetName, f.authorName, f.tags)) continue;
			seen.add(f.targetId);
			found.push(localCard(f));
		}
		for (const f of favorites) {
			if (seen.has(f.targetId) || !matches(f.targetName, f.authorName, f.tags)) continue;
			seen.add(f.targetId);
			found.push(toCard(f));
		}
		return found.sort(compare);
	});
	const cards = $derived(searching ? searchCards : activeSlot ? groupCards : historySelected ? historyCards : localCards);
	const allChosen = $derived(cards.length > 0 && cards.every((c) => chosen.includes(c.id)));
	// The original's useFavoritesCardScaling: as many columns of the scaled card width as fit, stretched
	// to fill the row only once there are enough cards to fill it; padding and gaps follow the scale.
	let gridWidth = $state(0);
	const gridStyle = $derived.by(() => {
		const minWidth = 260 * scale;
		const gap = Math.max(4, (12 + (scale - 1) * 8) * spacing);
		const maxColumns = gridWidth > 0 ? Math.max(1, Math.floor((gridWidth + gap) / (minWidth + gap)) || 1) : 1;
		const count = cards.length;
		const columns = Math.max(1, Math.min(count || 1, maxColumns));
		const track = count >= maxColumns ? 'minmax(0, 1fr)' : `${Math.round(minWidth)}px`;
		return `grid-template-columns: repeat(${columns}, ${track}); gap: ${Math.round(gap)}px;`;
	});
	const cardPadding = $derived(`padding: ${Math.round(Math.max(4, 8 * scale * spacing))}px ${Math.round(Math.max(4, 10 * scale * spacing))}px; gap: ${Math.round(Math.max(4, 10 * scale * spacing))}px;`);

	async function load() {
		if (!account) return;
		loading = true;
		try {
			const [list, locals, groups] = await Promise.all([
				api.get<Favorites>(base, { type: TYPE_ENUM[type] }),
				api.get<LocalFavorite[]>(`${base}/local`, { type: TYPE_ENUM[type] }),
				api.get<LocalGroup[]>(`${base}/local/groups`, { type: TYPE_ENUM[type] })
			]);
			favorites = list.favorites as Favorite[];
			localFavorites = locals;
			localGroups = groups;
			if (type === 'avatar') history = await api.get<HistoryEntry[]>(accountPath(account, '/avatar-history')).catch(() => []);
			const picker = await api.get<{ groups: Slot[] }>(`${base}/picker`, { type: TYPE_ENUM[type], targetId: '' }).catch(() => undefined);
			slots = picker?.groups ?? list.groups.map((g) => ({ type: g.type, name: g.name, displayName: g.displayName || g.name, capacity: 0, count: list.favorites.filter((f) => f.groupName === g.name).length, visibility: g.visibility }));
			if (!selected || (selected.kind === 'history' ? type !== 'avatar' : selected.kind === 'remote' ? !slots.some((s) => s.name === selected!.key) : !localGroups.some((g) => g.name === selected!.key)))
				{
					// The original opens the first group that has anything in it.
					const first = slots.find((g) => g.count > 0) ?? slots[0];
					// Then the first local group, then Local History, as the original's selectDefaultGroup.
					selected = first ? { kind: 'remote', key: first.name } : localGroups[0] ? { kind: 'local', key: localGroups[0].name } : type === 'avatar' && history.length ? { kind: 'history', key: 'local-history' } : null;
				}
		} catch (err) {
			notice = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	async function act(work: () => Promise<unknown>, done?: string) {
		notice = '';
		try {
			await work();
			if (done) notice = done;
		} catch (err) {
			notice = ApiError.from(err).message;
		}
		await load();
	}

	function choose(kind: 'remote' | 'local' | 'history', key: string) {
		if (hasSearchInput) search = '';
		selected = { kind, key };
		chosen = [];
	}

	const refresh = () => act(() => api.post(`${base}/refresh`, {}));

	function renameRemote(slot: Slot) {
		renaming = {
			title: 'Change Favorite Group Name',
			value: slot.displayName,
			save: (name) => act(() => api.put(`${base}/groups/${slot.type}/${slot.name}`, { displayName: name }), 'Group name changed')
		};
	}

	const setVisibility = (slot: Slot, visibility: string) =>
		act(() => api.put(`${base}/groups/${slot.type}/${slot.name}`, { visibility }), 'Group visibility updated');

	function clearRemote(slot: Slot) {
		confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_clear_group(); }, destructive: true, run: () => act(() => api.delete(`${base}/groups/${slot.type}/${slot.name}`)) };
	}

	function renameLocal(name: string) {
		renaming = {
			title: 'Rename Local Favorite Group',
			value: name,
			save: (next) => {
				if (selected?.kind === 'local' && selected.key === name) selected = { kind: 'local', key: next };
				return act(() => api.put(`${base}/local/groups`, { type: TYPE_ENUM[type], name, newName: next }), 'Group name changed');
			}
		};
	}

	function deleteLocal(name: string) {
		confirming = {
			get title() { return m.confirm_title(); },
			description: `Continue? Delete Group ${name}`,
			destructive: true,
			run: () => {
				if (selected?.kind === 'local' && selected.key === name) selected = null;
				return act(() => api.delete(`${base}/local/groups?type=${TYPE_ENUM[type]}&name=${encodeURIComponent(name)}`));
			}
		};
	}

	function clearHistory() {
		confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_clear_avatar_history(); },
			destructive: true,
			run: () => act(() => api.delete(accountPath(account, '/avatar-history')))
		};
	}

	// The original's local Refresh: each local favorite fetched from VRChat again, one a second, until
	// done or cancelled; the server stores what it gets.
	let refreshingLocal = $state(false);
	let refreshToken: { cancelled: boolean; wake?: () => void } | null = null;

	async function refreshLocal() {
		if (refreshingLocal) return;
		refreshingLocal = true;
		const token: { cancelled: boolean; wake?: () => void } = { cancelled: false };
		refreshToken = token;
		try {
			for (const id of [...new Set(localFavorites.map((f) => f.targetId))]) {
				if (token.cancelled) break;
				await api.post(`${base}/local/refresh?type=${TYPE_ENUM[type]}&targetId=${encodeURIComponent(id)}`, {}).catch(() => {});
				if (token.cancelled) break;
				await new Promise<void>((resolve) => {
					const timer = setTimeout(resolve, 1000);
					token.wake = () => {
						clearTimeout(timer);
						resolve();
					};
				});
			}
		} finally {
			if (refreshToken === token) refreshToken = null;
			refreshingLocal = false;
		}
		await load();
	}

	function cancelLocalRefresh() {
		if (!refreshToken) return;
		refreshToken.cancelled = true;
		refreshToken.wake?.();
		refreshingLocal = false;
	}

	/** The original's checkInvalidLocalAvatars: ask VRChat for each avatar in the group, half a second apart. */
	function checkInvalid(group: string) {
		confirming = {
			get title() { return m.view_favorite_avatars_check_invalid(); },
			get description() { return m.view_favorite_avatars_check_description(); },
			run: async () => {
				confirming = undefined;
				const ids = localFavorites.filter((f) => f.groupName === group).map((f) => f.targetId);
				const invalid: string[] = [];
				checking = { current: 0, total: ids.length };
				try {
					for (const [i, id] of ids.entries()) {
						checking = { current: i + 1, total: ids.length };
						const found = await api.get<{ exists: boolean }>(accountPath(account, `/avatars/${encodeURIComponent(id)}/exists`)).catch(() => ({ exists: false }));
						if (!found.exists) invalid.push(id);
						else await new Promise((resolve) => setTimeout(resolve, 500));
					}
				} finally {
					checking = null;
				}
				if (!invalid.length) {
					notice = 'No invalid avatars found';
					return;
				}
				const list = invalid.join('\n');
				confirming = {
					get title() { return m.view_favorite_avatars_confirm_delete_invalid(); },
					description: `Found ${invalid.length} invalid avatars, delete them?\n\nAvatars pending deletion:\n${list}`,
					destructive: true,
					cancelText: 'Copy Avatar IDs',
					oncancel: () => {
						navigator.clipboard.writeText(list).then(
							() => (notice = 'Avatar IDs copied to clipboard. Delete operation cancelled'),
							() => (notice = 'Delete operation cancelled')
						);
					},
					run: () =>
						act(async () => {
							confirming = undefined;
							for (const id of invalid) await api.delete(`${base}/local?type=Avatar&targetId=${encodeURIComponent(id)}&group=${encodeURIComponent(group)}`);
						}, `Successfully deleted ${invalid.length} invalid avatars`)
				};
			}
		};
	}

	async function createLocal() {
		const name = newGroup.trim();
		creating = false;
		newGroup = '';
		if (!name) return;
		await act(() => api.post(`${base}/local/groups`, { type: TYPE_ENUM[type], name }));
		selected = { kind: 'local', key: name };
	}

	const removeOne = (card: Card) =>
		act(() =>
			card.local && activeLocal !== undefined
				? api.delete(`${base}/local?type=${TYPE_ENUM[type]}&targetId=${encodeURIComponent(card.id)}&group=${encodeURIComponent(activeLocal)}`)
				: api.delete(`${base}/${encodeURIComponent(card.id)}`)
		);

	function bulkUnfavorite() {
		const ids = [...chosen];
		if (!ids.length) return;
		confirming = {
			title: `Delete ${ids.length} favorites?`,
			description: `Are you sure you want to unfavorite ${ids.length} favorites?\nThis action cannot be undone.`,
			destructive: true,
			run: () =>
				act(async () => {
					for (const id of ids) {
						if (activeLocal !== undefined) await api.delete(`${base}/local?type=${TYPE_ENUM[type]}&targetId=${encodeURIComponent(id)}&group=${encodeURIComponent(activeLocal)}`);
						else await api.delete(`${base}/${encodeURIComponent(id)}`);
					}
					chosen = [];
					editMode = false;
				})
		};
	}

	function copyChosen() {
		if (!chosen.length) return;
		importText = chosen.map((id) => `${id}\n`).join('');
		importOpen = true;
	}

	function setScale(value: number) {
		scale = value;
		storeSetting(`VRCX_Favorites${KEY}CardScale`, value);
	}
	function setSpacing(value: number) {
		spacing = value;
		storeSetting(`VRCX_Favorites${KEY}CardSpacing`, value);
	}
	function setSort(value: string) {
		if (value === 'players') {
			sortPlayers = true;
			void loadOccupants();
			return;
		}
		sortPlayers = false;
		appearance.sortFavorites.value = value === 'date';
	}

	function startDrag(event: PointerEvent) {
		const startX = event.clientX;
		const start = panelWidth;
		// The original's splitter stops at 360px.
		const move = (e: PointerEvent) => (panelWidth = Math.min(360, Math.max(0, start + e.clientX - startX)));
		const up = () => {
			window.removeEventListener('pointermove', move);
			window.removeEventListener('pointerup', up);
			storeSetting(`VRCX_Favorites${KEY}Splitter`, panelWidth);
		};
		window.addEventListener('pointermove', move);
		window.addEventListener('pointerup', up);
	}

	function openCard(card: Card) {
		if (type === 'friend') userDialog.open(card.id);
		else window.location.href = type === 'world' ? worldHref(card.id) : avatarHref(card.id);
	}

	function friendAction(work: () => Promise<unknown>, done: string) {
		act(work, done);
	}

	$effect(() => {
		if (!account) return;
		void type;
		untrack(load);
		api.get<{ currentLocation?: string | null }>(accountPath(account, '/instances/options')).then(
			(data) => (currentLocation = data.currentLocation ?? null),
			() => (currentLocation = null)
		);
	});

	$effect(() => {
		if (searching && editMode) editMode = false;
	});
	$effect(() => {
		if (!editMode) chosen = [];
	});
</script>

{#snippet empty(text = 'No data')}
	<div class="flex h-full items-center justify-center py-3 text-compact text-muted-foreground">{text}</div>
{/snippet}

{#snippet groupMenu(items: { label: string; run: () => void; destructive?: boolean }[], slot?: Slot)}
	<DropdownMenu.Root>
		<DropdownMenu.Trigger>
			{#snippet child({ props })}
				<Button {...props} class="rounded-full" variant="ghost" size="icon-sm" aria-label={m.nav_tooltip_manage()} onclick={(e: MouseEvent) => e.stopPropagation()}><MoreHorizontal /></Button>
			{/snippet}
		</DropdownMenu.Trigger>
		<DropdownMenu.Content side="right" class="w-55">
			{#each items.filter((i) => !i.destructive) as item (item.label)}<DropdownMenu.Item onclick={item.run}>{item.label}</DropdownMenu.Item>{/each}
			{#if slot}
				<DropdownMenu.Sub>
					<DropdownMenu.SubTrigger>{m.view_favorite_visibility_tooltip()}</DropdownMenu.SubTrigger>
					<DropdownMenu.SubContent class="w-45">
						{#each VISIBILITIES as visibility (visibility)}
							<DropdownMenu.CheckboxItem checked={slot.visibility === visibility} onSelect={() => setVisibility(slot, visibility)}>{VISIBILITY_LABEL[visibility]}</DropdownMenu.CheckboxItem>
						{/each}
					</DropdownMenu.SubContent>
				</DropdownMenu.Sub>
			{/if}
			{#each items.filter((i) => i.destructive) as item (item.label)}<DropdownMenu.Item variant="destructive" onclick={item.run}>{item.label}</DropdownMenu.Item>{/each}
		</DropdownMenu.Content>
	</DropdownMenu.Root>
{/snippet}

{#snippet cardMenu(card: Card, M: typeof DropdownMenu | typeof ContextMenu)}
	{@const f = card.favorite}
	{@const missing = !f || f.missing || f.targetName === f.targetId || f.targetName === '???'}
	<M.Item onclick={() => openCard(card)}>{m.view_charts_mutual_friend_context_menu_view_details()}</M.Item>
	{#if type === 'world' && !missing}
		<!-- The original's WorldActionMenuItems on a favorite world. -->
		<M.Separator />
		<M.Item onclick={() => goto(worldHref(card.id))}>{m.dialog_new_instance_header()}</M.Item>
		<M.Item onclick={() => createAndSelfInvite(card.id).catch(() => {})}>{canOpenInGame() ? 'New Instance and Open In-game' : 'New Instance and Self Invite'}</M.Item>
	{/if}
	{#if type === 'friend' && f}
		{@const L = f.location ?? ''}
		{@const realLocation = L.startsWith('wrld_')}
		{#if f.state === 'online'}<M.Item onclick={() => friendAction(() => api.post(accountPath(account, `/invite-requests/${encodeURIComponent(card.id)}`), {}), 'Request invite sent')}>{m.view_notification_filters_requestInvite()}</M.Item>{/if}
		{#if desktop.gameRunning}
			<M.Item
				disabled={!currentLocation || !canInvite(currentLocation, account, currentLocation)}
				onclick={() => currentLocation && friendAction(() => api.post(accountPath(account, `/invites/${encodeURIComponent(card.id)}`), { location: currentLocation, worldName: parseLocation(currentLocation).worldId }), 'Invite sent')}
				>{m.view_notification_actions_invite()}</M.Item
			>
		{/if}
		<M.Item
			disabled={!sidebarData.me?.isBoopingEnabled}
			onclick={() => {
				boopFor = card;
				boopOpen = true;
			}}>{m.dialog_user_actions_send_boop()}</M.Item
		>
		{#if f.state === 'online' && realLocation}
			<M.Separator />
			<M.Item disabled={!canInvite(L, account, currentLocation)} onclick={() => (instanceDialogs.launch = { location: L })}>{m.dialog_launch_header()}</M.Item>
			<M.Item disabled={!canInvite(L, account, currentLocation)} onclick={() => friendAction(() => selfInvite(L), 'Self invite sent')}>{m.dialog_user_info_self_invite_tooltip()}</M.Item>
		{/if}
	{/if}
	<!-- The original's canSelectAvatar: local favorites always, else not deleted and not private; never the one worn. -->
	{#if type === 'avatar' && f && (card.local || card.history || (!f.missing && f.releaseStatus !== 'private'))}
		<M.Item
			disabled={card.history?.isCurrent || (!!sidebarData.me?.currentAvatar && sidebarData.me.currentAvatar === card.id)}
			onclick={() => (confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_select_avatar(); }, run: () => act(() => api.post(accountPath(account, `/avatars/${encodeURIComponent(card.id)}/select`), {}), 'Avatar changed') })}
			>{m.view_favorite_select_avatar_tooltip()}</M.Item
		>
	{/if}
	<M.Separator />
	<M.Item
		onclick={() => {
			editTarget = card.id;
			editOpen = true;
		}}>{m.view_favorite_edit_favorite_tooltip()}</M.Item
	>
	{#if !card.history}<M.Item variant="destructive" onclick={() => removeOne(card)}>{card.local ? 'Delete' : 'Unfavorite'}</M.Item>{/if}
{/snippet}

{#snippet cardView(card: Card)}
	<!-- The original's right-click menus on favorite cards (upstream issue 716): friends get the full user
	     menu with Edit Favorite and Delete appended, worlds and avatars the button's actions. -->
	{#if type === 'friend' && !editMode}
		<UserContextMenu userId={card.id} name={card.name} state={card.favorite?.state ?? ''} location={card.favorite?.location ?? ''}>
			{@render cardBody(card, {})}
			{#snippet append(M)}
				<M.Separator />
				<M.Item
					onSelect={() => {
						editTarget = card.id;
						editOpen = true;
					}}>{m.view_favorite_edit_favorite_tooltip()}</M.Item
				>
				<M.Item variant="destructive" onSelect={() => removeOne(card)}>{card.local ? 'Delete' : 'Unfavorite'}</M.Item>
			{/snippet}
		</UserContextMenu>
	{:else}
		<ContextMenu.Root>
			<ContextMenu.Trigger>
				{#snippet child({ props })}{@render cardBody(card, props)}{/snippet}
			</ContextMenu.Trigger>
			{#if !editMode}<ContextMenu.Content>{@render cardMenu(card, ContextMenu)}</ContextMenu.Content>{/if}
		</ContextMenu.Root>
	{/if}
{/snippet}

{#snippet cardBody(card: Card, props: Record<string, unknown>)}
	{@const f = card.favorite}
	{@const missing = !f || f.missing || f.targetName === f.targetId || f.targetName === '???'}
	{@const isPrivate = type !== 'friend' && f?.releaseStatus === 'private'}
	{@const known = type === 'friend' ? sidebarData.friends.find((x) => x.userId === card.id) : undefined}
	<div
		{...props}
		class={['flex min-w-0 cursor-pointer items-center rounded-lg border hover:bg-muted', chosen.includes(card.id) && 'border-primary']}
		style={cardPadding}
		role="button"
		tabindex="0"
		onclick={() => openCard(card)}
		onkeydown={(e) => e.key === 'Enter' && openCard(card)}
	>
		{#if type === 'friend'}
			<div class="relative shrink-0" style="width: {40 * scale}px; height: {40 * scale}px">
				<Avatar.Root class="size-full">
					<Avatar.Image src={imageSrc(card.image)} loading="lazy" />
					<Avatar.Fallback><UserIcon class="size-4 text-muted-foreground" /></Avatar.Fallback>
				</Avatar.Root>
				<IconFrame iconFrame={known?.iconFrame} />
			</div>
		{:else if card.image}
			<img src={imageSrc(card.image)} alt="" loading="lazy" class="shrink-0 rounded-md object-cover" style="width: {64 * scale}px; height: {48 * scale}px" />
		{:else}
			<div class="flex shrink-0 items-center justify-center rounded-md bg-muted" style="width: {64 * scale}px; height: {48 * scale}px"><ImageIcon class="size-4 text-muted-foreground" /></div>
		{/if}
		<div class="min-w-0 flex-1" style="font-size: {13 * scale}px">
			<!-- The original colours a favorite friend's name when it knows the user. -->
			<div class="truncate font-medium" style:color={known ? nameColour(known, appearance.isDarkMode) : undefined}>{card.name}</div>
			<div class="truncate text-xs text-muted-foreground">
				{#if type === 'friend'}
					{#if f?.location && f.location !== 'offline'}<Location location={f.location} traveling={f.travelingToLocation ?? undefined} link={false} />{:else}{f?.statusDescription ?? (missing ? card.id : '')}{/if}
				{:else}
					{f?.authorName ?? (missing ? card.id : '')}
					{#if isPrivate}<span class="ml-1 text-destructive">{m.view_favorite_private()}</span>{/if}
					{#if missing}<span class="ml-1">{m.view_favorite_unavailable_tooltip()}</span>{/if}
				{/if}
			</div>
		</div>
		{#if editMode && !searching}
			<span role="presentation" onclick={(e) => e.stopPropagation()} onkeydown={(e) => e.stopPropagation()}>
				<Checkbox checked={chosen.includes(card.id)} onCheckedChange={(v) => (chosen = v ? [...chosen, card.id] : chosen.filter((id) => id !== card.id))} />
			</span>
		{:else}
			<DropdownMenu.Root>
				<DropdownMenu.Trigger>
					{#snippet child({ props })}
						<Button {...props} size="icon-sm" variant="ghost" class="shrink-0 rounded-full" aria-label={m.nav_tooltip_manage()} onclick={(e: MouseEvent) => e.stopPropagation()}><MoreHorizontal class="h-4 w-4" /></Button>
					{/snippet}
				</DropdownMenu.Trigger>
				<DropdownMenu.Content align="end">{@render cardMenu(card, DropdownMenu)}</DropdownMenu.Content>
			</DropdownMenu.Root>
		{/if}
	</div>
{/snippet}

<Tooltip.Provider>
	{#if !account}
		<Card.Root><Card.Header><Card.Description>Log in to VRChat to see your favorites.</Card.Description></Card.Header></Card.Root>
	{:else}
		<div class="flex h-full min-h-0 flex-col pb-0">
			<div class="mb-3 flex flex-wrap items-center justify-between gap-3">
				<Select.Root type="single" value={sortPlayers ? 'players' : sortByDate ? 'date' : 'name'} onValueChange={setSort}>
					<Select.Trigger size="sm" class="min-w-50">
						<span class="flex items-center gap-2"><ArrowUpDown class="h-4 w-4" />{sortPlayers ? 'Players' : sortByDate ? 'Sort by date' : 'Sort alphabetically'}</span>
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="name">Sort alphabetically</Select.Item>
						<Select.Item value="date">Sort by date</Select.Item>
						{#if type === 'world'}<Select.Item value="players">{m.view_settings_appearance_appearance_sort_favorite_by_players()}</Select.Item>{/if}
					</Select.Content>
				</Select.Root>
				<div class="flex flex-1 items-center gap-2">
					<div class="relative flex-1">
						<Input bind:value={search} class="h-8" placeholder={type === 'world' && searchMode === 'tag' ? 'Search by Tag' : 'Search'} />
						{#if type === 'world'}
							<div class="absolute top-1/2 right-1 flex -translate-y-1/2 gap-0.5">
								{#each [['name', 'Name'], ['tag', 'Tag']] as [value, label] (value)}
									<button type="button" class={['h-5 rounded border px-1.5 text-2xs', searchMode === value ? 'bg-muted' : '']} onclick={() => (searchMode = value as 'name' | 'tag')}>{label}</button>
								{/each}
							</div>
						{/if}
					</div>
					<DropdownMenu.Root>
						<DropdownMenu.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="rounded-full" size="icon-sm" variant="ghost" aria-label="More"><Ellipsis /></Button>
							{/snippet}
						</DropdownMenu.Trigger>
						<DropdownMenu.Content class="p-2">
							<div class="min-w-55 cursor-default px-4 pt-3 pb-2">
								<div class="mb-2 flex items-center justify-between text-compact font-semibold"><span>{m.view_friends_locations_scale()}</span><span class="text-xs">{Math.round(scale * 100)}%</span></div>
								<Slider type="single" class="px-1 pb-1" value={scale} min={0.6} max={1} step={0.01} onValueChange={setScale} />
							</div>
							<div class="min-w-55 cursor-default px-4 pt-3 pb-2">
								<div class="mb-2 flex items-center justify-between text-compact font-semibold"><span>{m.view_friends_locations_spacing()}</span><span class="text-xs">{Math.round(spacing * 100)}%</span></div>
								<Slider type="single" class="px-1 pb-1" value={spacing} min={0.5} max={1.5} step={0.05} onValueChange={setSpacing} />
							</div>
							<DropdownMenu.Separator />
							<DropdownMenu.Item onclick={() => (importOpen = true)}>{m.view_favorite_import()}</DropdownMenu.Item>
							<DropdownMenu.Item onclick={() => (exportOpen = true)}>{m.view_favorite_export()}</DropdownMenu.Item>
						</DropdownMenu.Content>
					</DropdownMenu.Root>
				</div>
			</div>
			{#if notice}<p class="mb-2 text-sm text-muted-foreground">{notice}</p>{/if}
			{#if checking}
				<div class="mb-2 flex max-w-sm flex-col gap-2 rounded-md border p-3">
					<div class="text-sm font-medium">Checking avatar ({checking.current}/{checking.total})...</div>
					<Progress value={checking.total ? Math.floor((checking.current / checking.total) * 100) : 0} class="h-2 w-full" />
				</div>
			{/if}

			<div class="flex min-h-0 flex-1">
				<div class="flex h-full shrink-0 flex-col gap-3 overflow-auto pr-2" style="width: {panelWidth}px">
					<div class="flex flex-col gap-2">
						<div class="mb-2 flex items-center justify-between text-sm font-semibold">
							<span>{m.view_favorite_worlds_vrchat_favorites()}</span>
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<Button {...props} class="rounded-full" variant="ghost" size="icon-sm" disabled={loading} aria-label={m.view_favorite_refresh_favorites_tooltip()} onclick={refresh}>
											{#if loading}<Spinner />{:else}<RefreshCw />{/if}
										</Button>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="bottom">{m.view_favorite_refresh_favorites_tooltip()}</Tooltip.Content>
							</Tooltip.Root>
						</div>
						{#each slots as slot (slot.name)}
							<div
								class={['cursor-pointer rounded-lg border p-2.5 hover:shadow-sm', !hasSearchInput && selected?.kind === 'remote' && selected.key === slot.name ? 'border-primary bg-muted' : 'hover:bg-muted/50']}
								role="button"
								tabindex="0"
								onclick={() => choose('remote', slot.name)}
								onkeydown={(e) => e.key === 'Enter' && choose('remote', slot.name)}
							>
								<div class="mb-1 flex items-start justify-between text-compact">
									<span class="font-semibold">{slot.displayName}</span>
									<span class="text-xs">{slot.count}/{slot.capacity}</span>
								</div>
								<div class="flex items-center justify-between gap-2">
									<span class="text-2xs text-muted-foreground">{VISIBILITY_LABEL[slot.visibility] ?? slot.visibility}</span>
									{@render groupMenu([{ get label() { return m.view_favorite_rename_tooltip(); }, run: () => renameRemote(slot) }, { get label() { return m.view_favorite_clear(); }, run: () => clearRemote(slot), destructive: true }], slot)}
								</div>
							</div>
						{:else}
							{@render empty()}
						{/each}
					</div>
					<div class="flex flex-col gap-2">
						<div class="mb-2 flex items-center justify-between text-sm font-semibold">
							<span>{m.view_favorite_worlds_local_favorites()}</span>
							{#if refreshingLocal}
								<Button size="sm" variant="ghost" onclick={cancelLocalRefresh}><Spinner /> {m.view_favorite_worlds_cancel_refresh()}</Button>
							{:else}
								<Button class="rounded-full" size="icon-sm" variant="ghost" aria-label={m.view_profile_refresh_tooltip()} onclick={refreshLocal}><RefreshCcw /></Button>
							{/if}
						</div>
						{#each localGroups as group (group.name)}
							<div
								class={['cursor-pointer rounded-lg border p-2.5 hover:shadow-sm', !hasSearchInput && selected?.kind === 'local' && selected.key === group.name ? 'border-primary bg-muted' : 'hover:bg-muted/50']}
								role="button"
								tabindex="0"
								onclick={() => choose('local', group.name)}
								onkeydown={(e) => e.key === 'Enter' && choose('local', group.name)}
							>
								<div class="flex items-start justify-between text-compact">
									<span class="font-semibold">{group.name}</span>
									<div class="flex flex-col items-center">
										<span class="text-xs">{group.count}</span>
										{@render groupMenu([
											{ get label() { return m.view_favorite_rename_tooltip(); }, run: () => renameLocal(group.name) },
											...(type === 'avatar' ? [{ get label() { return m.view_favorite_avatars_check_invalid(); }, run: () => checkInvalid(group.name) }] : []),
											{ get label() { return m.view_favorite_delete_tooltip(); }, run: () => deleteLocal(group.name), destructive: true }
										])}
									</div>
								</div>
							</div>
						{:else}
							{@render empty()}
						{/each}
						{#if type === 'avatar'}
							<div class="mt-1 mb-1 flex items-center justify-between text-sm font-semibold"><span>{m.view_favorite_avatars_local_history()}</span></div>
							<div
								class={['cursor-pointer rounded-lg border p-2.5 hover:shadow-sm', !hasSearchInput && selected?.kind === 'history' ? 'border-primary bg-muted' : 'hover:bg-muted/50']}
								role="button"
								tabindex="0"
								onclick={() => choose('history', 'local-history')}
								onkeydown={(e) => e.key === 'Enter' && choose('history', 'local-history')}
							>
								<div class="flex items-start justify-between text-compact">
									<span class="font-semibold">{m.view_favorite_avatars_local_history()}</span>
									<div class="flex flex-col items-center">
										<span class="text-xs">{history.length}/100</span>
										{@render groupMenu([{ get label() { return m.view_favorite_clear(); }, run: clearHistory, destructive: true }])}
									</div>
								</div>
							</div>
						{/if}
						{#if creating}
							<!-- svelte-ignore a11y_autofocus -->
							<Input
								bind:value={newGroup}
								class="h-8 w-full"
								placeholder={m.view_favorite_worlds_new_group()}
								autofocus
								onkeyup={(e: KeyboardEvent) => (e.key === 'Enter' ? createLocal() : e.key === 'Escape' && (creating = false))}
								onblur={() => (creating = false)}
							/>
						{:else}
							<button type="button" class="flex cursor-pointer items-center justify-center gap-2 rounded-lg border border-dashed p-2.5 text-sm hover:bg-muted/50" onclick={() => (creating = true)}>
								<Plus class="size-4" /><span>{m.view_favorite_worlds_new_group()}</span>
							</button>
						{/if}
					</div>
				</div>
				<div role="separator" aria-orientation="vertical" tabindex="-1" class="w-1.5 shrink-0 cursor-col-resize bg-border/40 hover:bg-border" onpointerdown={startDrag}></div>
				<div class="flex h-full min-h-0 min-w-0 flex-1 flex-col pl-6.5">
					<div class="mb-3 flex items-center justify-between gap-3">
						<div class="flex flex-col gap-0.5 pl-0.5 text-base font-semibold">
							{#if searching}<span>{m.view_favorite_worlds_search()}</span>
							{:else if activeSlot}<span>{activeSlot.displayName} <small class="text-xs font-normal">{activeSlot.count}/{activeSlot.capacity}</small></span>
							{:else if historySelected}<span>{m.view_favorite_avatars_local_history()} <small class="text-xs font-normal">{history.length}/100</small></span>
							{:else if activeLocal !== undefined}<span>{activeLocal} <small class="text-xs font-normal">{localCards.length}</small></span>
							{:else}<span>{m.view_favorite_avatars_no_group_selected()}</span>{/if}
						</div>
						<div class="flex items-center gap-2 text-compact">
							<span>{m.view_favorite_edit_mode()}</span>
							<Switch bind:checked={editMode} disabled={searching || historySelected || (!activeSlot && activeLocal === undefined)} aria-label={m.view_favorite_edit_mode()} />
						</div>
					</div>
					{#if editMode && !searching}
						<div class="mb-3 flex flex-wrap justify-end gap-2">
							<Button size="sm" variant="outline" onclick={() => (chosen = allChosen ? [] : cards.map((c) => c.id))}>{allChosen ? 'Deselect All' : 'Select All'}</Button>
							<Button size="sm" variant="secondary" disabled={!chosen.length} onclick={() => (chosen = [])}>{m.view_favorite_clear()}</Button>
							{#if activeLocal === undefined}<Button size="sm" variant="outline" disabled={!chosen.length} onclick={copyChosen}>{m.view_favorite_copy()}</Button>{/if}
							<Button size="sm" variant="outline" disabled={!chosen.length} onclick={bulkUnfavorite}>{m.view_favorite_bulk_unfavorite()}</Button>
						</div>
					{/if}
					<div class="min-h-0 flex-1 overflow-auto pr-2" bind:clientWidth={gridWidth}>
						{#if !searching && !activeSlot && !historySelected && activeLocal === undefined}
							{@render empty('No Group Selected')}
						{:else if cards.length}
							<div class="grid" style={gridStyle}>
								{#each cards as card (card.id)}{@render cardView(card)}{/each}
							</div>
						{:else}
							{@render empty(searching ? 'No matching records found' : 'No data')}
						{/if}
					</div>
				</div>
			</div>
		</div>
	{/if}
</Tooltip.Provider>

{#if editTarget}
	<FavoriteButton type={type} targetId={editTarget} showButton={false} bind:open={editOpen} onchange={load} />
{/if}

<Dialog.Root open={!!renaming} onOpenChange={(open) => !open && (renaming = undefined)}>
	<Dialog.Content class="bg-background sm:max-w-md" showCloseButton={false}>
		{#if renaming}
			{@const r = renaming}
			<Dialog.Header><Dialog.Title>{r.title}</Dialog.Title></Dialog.Header>
			<p class="text-sm">{m.prompt_change_favorite_group_name_description()}</p>
			<Input bind:value={r.value} />
			{#if !/\S+/.test(r.value)}<p class="text-xs text-destructive">{m.prompt_backup_name_input_error()}</p>{/if}
			<Dialog.Footer>
				<Button variant="secondary" onclick={() => (renaming = undefined)}>{m.view_login_qr_cancel()}</Button>
				<Button
					disabled={!/\S+/.test(r.value)}
					onclick={() => {
						const { save, value } = r;
						renaming = undefined;
						save(value.trim());
					}}>{m.prompt_change_favorite_group_name_change()}</Button
				>
			</Dialog.Footer>
		{/if}
	</Dialog.Content>
</Dialog.Root>

<FavoritesTransfer {type} {account} {slots} {localGroups} {favorites} {localFavorites} bind:importOpen bind:importText bind:exportOpen onchange={load} />

<ActionDialogs bind:confirming />
{#if boopFor}
	{@const target = boopFor}
	<BoopDialog bind:open={boopOpen} name={target.name} onsend={(emoji) => friendAction(() => api.post(accountPath(account, `/users/${encodeURIComponent(target.id)}/boop`), { emojiId: emoji || null }), 'Boop sent')} />
{/if}
