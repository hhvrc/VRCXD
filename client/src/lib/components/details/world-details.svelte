<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import LocationWorld from '#lib/components/instance/location-world.svelte';
	import { openExternalLink } from '#lib/nav/external-link.svelte.js';
	import { loadFileAnalysis, type FileAnalysis } from '#lib/content/file-analysis.js';
	import CopyId from '#lib/components/common/copy-id.svelte';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { onItemChanged } from '#lib/api/live.svelte.js';
	import PreviousInstancesDialog from '#lib/components/instance/previous-instances-dialog.svelte';
	import FavoriteButton from '#lib/components/favorites/favorite-button.svelte';
	import { BundleCache } from '#lib/agent/bundle-cache.svelte.js';
	import { desktop } from '#lib/instances/instance-launch.svelte.js';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import Apple from '@lucide/svelte/icons/apple';
	import Eye from '@lucide/svelte/icons/eye';
	import Flag from '@lucide/svelte/icons/flag';
	import InstanceActions from '#lib/components/instance/instance-actions.svelte';
	import InstanceInfo from '#lib/components/instance/instance-info.svelte';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import { canOpenInGame, createAndSelfInvite, instanceDialogs, closedInstances, instanceCreator } from '#lib/instances/instance-launch.svelte.js';
	import Globe2 from '@lucide/svelte/icons/globe';
	import Home from '@lucide/svelte/icons/house';
	import ImageIcon from '@lucide/svelte/icons/image';
	import LineChart from '@lucide/svelte/icons/chart-line';
	import LockKeyhole from '@lucide/svelte/icons/lock-keyhole';
	import Monitor from '@lucide/svelte/icons/monitor';
	import MoreHorizontal from '@lucide/svelte/icons/ellipsis';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import ThumbsDown from '@lucide/svelte/icons/thumbs-down';
	import ThumbsUp from '@lucide/svelte/icons/thumbs-up';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import Upload from '@lucide/svelte/icons/upload';
	import Wand2 from '@lucide/svelte/icons/wand-sparkles';
	import ActionDialogs from '#lib/components/dialogs/action-dialogs.svelte';
	import CopyIdRow from '#lib/components/details/copy-id-row.svelte';
	import InfoRow from '#lib/components/details/info-row.svelte';
	import JsonCard from '#lib/components/details/json-card.svelte';
	import MemoCard from '#lib/components/details/memo-card.svelte';
	import SectionCard from '#lib/components/details/section-card.svelte';
	import ShareMenu from '#lib/components/details/share-menu.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { DetailActions } from '#lib/helpers/detail-actions.svelte.js';
	import { appearance, sortInstanceUsers } from '#lib/settings/appearance.svelte.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { formatDate, sanitizeEntity, timeAgo, timeToText } from '#lib/helpers/format.js';
	import { userHref, vrchatUrl } from '#lib/api/links.js';
	import type { Friend, World } from '#lib/api/types.js';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import { sdkUnityVersion } from '#lib/content/sdk-unity-version.svelte.js';
	import { authorTagsText, availablePlatforms, contentLabel, contentTagsOf, NONE, openPackage, platformCreatedAt, platformText, TAB_CONTENT, TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER, INFO_ROW, SECTION, CARD, PANEL, HEADER_CARD, RAIL, TITLE_BUTTON } from '#lib/details.js';
	import ContentImageUpload from '#lib/components/details/content-image-upload.svelte';
	import SetWorldTagsDialog from '#lib/components/details/set-world-tags-dialog.svelte';
	import WorldAllowedDomainsDialog from '#lib/components/details/world-allowed-domains-dialog.svelte';
	import WorldStoreTab from '#lib/components/details/world-store-tab.svelte';
	import Download from '@lucide/svelte/icons/download';
	import { parseLocation, resolveRegion } from '#lib/instances/location.js';
	import { userNames } from '#lib/people/names.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import PersonRow from '#lib/components/details/person-row.svelte';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's world dialog (WorldDialog.vue and its parts), class for class: a summary rail
	// with the image on top, and the instances beside it.
	let { worldId, location, shortName }: { worldId: string; location?: string; shortName?: string } = $props();


	type LiveWorld = {
		id: string;
		name: string;
		description?: string;
		authorId?: string;
		authorName?: string;
		capacity?: number;
		recommendedCapacity?: number;
		imageUrl?: string;
		thumbnailImageUrl?: string;
		releaseStatus?: string;
		tags?: string[];
		version?: number;
		visits?: number;
		favorites?: number;
		heat?: number;
		popularity?: number;
		publicOccupants?: number;
		privateOccupants?: number;
		previewYoutubeId?: string;
		labsPublicationDate?: string;
		publicationDate?: string;
		created_at?: string;
		updated_at?: string;
		instances?: [string, number][];
		unityPackages?: { platform?: string; unityVersion?: string; unitySortNumber?: number; variant?: string; created_at?: string; assetUrl?: string }[];
		assetUrl?: string;
		urlList?: string[] | null;
		disabledPropAbilities?: string[] | null;
		storeId?: string | null;
		unityPackageUrl?: string | null;
	};
	type Stats = { visitCount: number; lastVisit?: string; timeSpentMs: number };
	type Room = { location: string; occupants: number; friends: Friend[]; shortName?: string };
	type WorldRatingValue = 'like' | 'dislike';
	type WorldRating = { value: WorldRatingValue | null };

	let world = $state<LiveWorld | undefined>();
	let stats = $state<Stats | undefined>();
	let friends = $state<Friend[]>([]);
	let homeLocation = $state('');
	let hasPersistData = $state(false);
	// This account's like or dislike of the world.
	let rating = $state<WorldRatingValue | null>(null);
	let loadError = $state('');
	let refreshing = $state(false);
	let previousOpen = $state(false);
	let imageError = $state(false);
	let activeTab = $state('instances');
	let tagsOpen = $state(false);
	let domainsOpen = $state(false);
	let imageUpload = $state<ContentImageUpload | undefined>();
	// The original's Timer beside each friend in an instance: time there, every 15 seconds.
	const now = $derived(clock(15_000).now);

	const actions = new DetailActions(() => load(worldId));
	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(`${accountPath(owner)}/worlds/${encodeURIComponent(worldId)}`);
	const isAuthor = $derived(!!world && world.authorId === owner);
	const isHome = $derived(homeLocation === worldId);
	const tags = $derived(world?.tags ?? []);
	const isLabs = $derived(tags.includes('system_labs'));
	const isPublished = $derived(tags.includes('system_approved') || isLabs);
	const platforms = $derived(availablePlatforms(world?.unityPackages));
	const authorTags = $derived(authorTagsText(tags));
	const contentTags = $derived(contentTagsOf(tags));
	// The original's worldDialogPlatform: packages the SDK's Unity can open, variants included.
	const platformLines = $derived(platformText(world?.unityPackages, { sdkUnityVersion: sdkUnityVersion(owner), skipVariants: false }));
	const platformDates = $derived(platformCreatedAt(world?.unityPackages));
	const favoriteRate = $derived(world?.favorites && world.visits ? Math.round((world.favorites / world.visits) * 10000) / 100 : undefined);

	// VRChat's open instances, plus any instance a friend is in that it didn't list.
	const rooms = $derived.by((): Room[] => {
		const byLocation = new Map<string, Room>();
		for (const [instance, occupants] of world?.instances ?? []) {
			const location = `${worldId}:${instance}`;
			byLocation.set(location, { location, occupants, friends: [] });
		}
		const listed = new Set(byLocation.keys());
		for (const friend of friends) {
			if (!friend.location?.startsWith(`${worldId}:`)) continue;
			const room = byLocation.get(friend.location) ?? { location: friend.location, occupants: 0, friends: [] };
			room.friends.push(friend);
			byLocation.set(friend.location, room);
		}
		// The instance this was opened from comes first, even when VRChat does not list it.
		// As in the original, it carries the short name it was opened with only when VRChat did not list it.
		if (location && !listed.has(location)) byLocation.set(location, { ...(byLocation.get(location) ?? { location, occupants: 0, friends: [] }), shortName });
		// The original's room order: the instance this was opened from, then the game's own instance, then
		// by friends there (by players when neither has friends), and the instance id to keep it stable (upstream PR 1419).
		const opened = (room: Room) => (room.location === location ? 2 : room.location === sidebarData.currentLocation ? 1 : 0);
		const compareRooms = (a: Room, b: Room) => {
			if (opened(a) !== opened(b)) return opened(b) - opened(a);
			if (a.friends.length === 0 && b.friends.length === 0 && a.occupants !== b.occupants) return b.occupants - a.occupants;
			if (a.friends.length !== b.friends.length) return b.friends.length - a.friends.length;
			return a.location.localeCompare(b.location);
		};
		// The original's isRoomVisible: age-gated and closed instances follow their Display settings.
		const visible = (room: Room) => {
			if (!appearance.isAgeGatedInstancesVisible.value && room.location.includes('~ageGate')) return false;
			if (!appearance.isClosedInstancesVisible.value && closedInstances.get(room.location)) return false;
			return true;
		};
		for (const room of byLocation.values()) room.friends = sortInstanceUsers(room.friends);
		return [...byLocation.values()].filter(visible).sort(compareRooms);
	});

	const commaNumber = (value: number | undefined | null) => (value ?? 0).toLocaleString('en-US');
	const hasDate = (value: string | undefined) => !!value && value !== 'none';

	// The original's fileAnalysis: each platform's bundle size, shown on its badge.
	let fileAnalysis = $state<Record<string, FileAnalysis>>({});

	// The original's world cache row: VRChat's cached bundle on the PC.
	const bundle = new BundleCache();
	$effect(() => {
		const ref = world;
		const sdk = sdkUnityVersion(owner);
		bundle.refresh(ref, sdk);
	});

	async function load(id: string) {
		if (!owner || !id) return;
		loadError = '';
		try {
			// The stored row is what the memo hangs off, so make sure there is one.
			const stored = await api.get<World>(`/1/worlds/${encodeURIComponent(id)}`).catch((err) => {
				if (ApiError.from(err).status === 404) return api.get<World>(`/1/worlds/${encodeURIComponent(id)}`, { refresh: true, account: owner });
				throw err;
			});
			const path = `${accountPath(owner)}/worlds/${encodeURIComponent(id)}`;
			api.get<WorldRating>(`${path}/rating`)
				.then((found) => id === worldId && (rating = found?.value ?? null))
				.catch((err) => console.error('Failed to load world rating:', err));
			const [live, nextStats, nextFriends, accounts, persist] = await Promise.all([
				api.get<LiveWorld | null>(`${path}/live`).catch(() => null),
				api.get<Stats>(`${path}/stats`).catch(() => undefined),
				api.get<Friend[]>(accountPath(owner, '/friends')).catch(() => []),
				api.get<{ userId: string; homeLocation?: string }[]>('/1/accounts').catch(() => []),
				api.get<boolean>(`${path}/persist`).catch(() => false)
			]);
			if (id !== worldId) return;
			// The original's sanitizeEntityJson on a world it applies; the stored row is clean already.
			world = live ? sanitizeEntity(live, ['name', 'description']) : ({ ...stored, created_at: stored.createdAt, updated_at: stored.updatedAt } as unknown as LiveWorld);
			stats = nextStats;
			friends = nextFriends;
						homeLocation = accounts.find((a) => a.userId === owner)?.homeLocation ?? '';
			hasPersistData = persist;
			userDialog.setLabel(id, world.name);
			loadFileAnalysis(owner, 'worlds', id).then((found) => id === worldId && (fileAnalysis = found));
		} catch (err) {
			if (id === worldId) loadError = ApiError.from(err).message;
		}
	}

	async function refresh() {
		refreshing = true;
		await load(worldId);
		refreshing = false;
	}

	$effect(() => {
		const id = worldId;
		if (owner && id) {
			world = undefined;
			stats = undefined;
			rating = null;
			fileAnalysis = {};
			actions.reset();
			activeTab = 'instances';
			imageError = false;
			load(id);
		}
	});


	// Clicking the active one clears it, the other one sets it.
	function setRating(value: WorldRatingValue) {
		const id = worldId;
		const path = `${base}/rating`;
		const req = rating === value ? api.delete<WorldRating>(path) : api.put<WorldRating>(`${path}?value=${value}`);
		req.then((result) => id === worldId && (rating = result?.value ?? null)).catch(toastError);
	}

	function promptSave(title: string, description: string, errorMessage: string, field: string, value: string, done: string, options: { multiline?: boolean; numeric?: boolean } = {}) {
		actions.prompting = {
			title,
			description,
			errorMessage,
			value,
			...options,
			run: (text) => actions.act(() => api.put(base, { [field]: options.numeric ? Number(text) : text }), done)
		};
	}

	function promptYoutube() {
		actions.prompting = {
			get title() { return m.prompt_change_world_preview_header(); },
			get description() { return m.prompt_change_world_preview_description(); },
			errorMessage: 'Valid YouTube URL is required',
			allowEmpty: true,
			value: world?.previewYoutubeId ? `https://www.youtube.com/watch?v=${world.previewYoutubeId}` : '',
			run: async (raw) => {
				// An empty value removes the preview, as the original does since upstream 5fc0b0c3.
				const text = raw.trim();
				const current = world?.previewYoutubeId ?? '';
				const id = text.length <= 11 ? text : /(?:youtu\.be\/|[?&]v=|\/embed\/)([\w-]{11})/.exec(text)?.[1];
				if (id === current) {
					actions.prompting = undefined;
					return;
				}
				if (id === undefined) {
					actions.error = 'Invalid YouTube URL';
					actions.prompting = undefined;
					return;
				}
				await actions.act(() => api.put(base, { previewYoutubeId: id }), 'World YouTube preview changed');
			}
		};
	}

	$effect(() => onItemChanged('world', () => worldId, () => load(worldId)));
</script>

{#if loadError && !world}
	<div class="flex flex-col items-start gap-2 p-1.5">
		<p class="text-sm text-destructive">{loadError}</p>
		<CopyId id={worldId} />
		<Button variant="outline" size="sm" disabled={refreshing} onclick={refresh}>{m.dialog_world_actions_refresh()}</Button>
	</div>
{:else if !world}
	<div class="flex h-40 items-center justify-center"><Spinner /></div>
{:else}
	<div class="flex min-h-0 min-w-0 flex-1 flex-row" data-slot="world-details">
		<div class={RAIL}>
			<div class={HEADER_CARD}>
				<div class="relative aspect-4/3">
					{#if world.thumbnailImageUrl && !imageError}
						<ViewableImage src={world.thumbnailImageUrl} full={world.imageUrl} class="absolute inset-0" imageClass="size-full object-cover" onerror={() => (imageError = true)} />
					{:else}
						<div class="absolute inset-0 flex items-center justify-center bg-muted"><ImageIcon class="size-8 text-muted-foreground" /></div>
					{/if}
				</div>
				<div class="flex flex-col p-3">
					<div class="flex justify-between gap-2">
						<div class="min-w-0 flex-1">
							<button type="button" class={TITLE_BUTTON} title={world.name || worldId} onclick={() => actions.copy(world?.name ?? '', 'World name copied to clipboard')}>
								{#if isHome}<Home class="mr-1 inline-block size-4" />{/if}{world.name || worldId}
							</button>
							{#if world.authorId}
								<a class="flex max-w-30 cursor-pointer flex-wrap font-mono text-xs text-muted-foreground" href={userHref(world.authorId)}>{world.authorName || world.authorId}</a>
							{/if}
						</div>
						{#if bundle.inCache}
							<Button variant="outline" size="icon" class="flex-none self-start rounded-lg" title={m.dialog_world_actions_delete_cache_tooltip()} aria-label={m.dialog_world_actions_delete_cache_tooltip()} disabled={desktop.gameRunning && bundle.locked} onclick={() => bundle.delete(world, sdkUnityVersion(owner))}><Trash2 /></Button>
						{/if}
						<FavoriteButton type="world" targetId={worldId} class="flex-none self-start" />
						<DropdownMenu.Root>
							<DropdownMenu.Trigger>
								{#snippet child({ props })}
									<Button {...props} variant="outline" size="icon" class="flex-none self-start rounded-lg" aria-label={m.nav_tooltip_manage()}><MoreHorizontal /></Button>
								{/snippet}
							</DropdownMenu.Trigger>
							<DropdownMenu.Content class="w-auto whitespace-nowrap">
								<DropdownMenu.Item disabled={refreshing} onSelect={refresh}><RefreshCw class="size-4" /> {m.dialog_world_actions_refresh()}</DropdownMenu.Item>
								<ShareMenu kind="world" url={vrchatUrl('world', worldId)} name={world.name} id={worldId} {actions} />
								<DropdownMenu.Separator />
								<DropdownMenu.Item onSelect={() => (instanceDialogs.newInstance = { worldId })}><Flag class="size-4" /> {m.dialog_world_actions_new_instance()}</DropdownMenu.Item>
								<DropdownMenu.Item onSelect={() => actions.act(() => createAndSelfInvite(worldId), canOpenInGame() ? 'Opened in VRChat' : 'Self invite sent', false)}><MessageSquare class="size-4" /> {canOpenInGame() ? 'New Instance and Open In-game' : 'New Instance and Self Invite'}</DropdownMenu.Item>
								<DropdownMenu.Item onSelect={() => (previousOpen = true)}><LineChart class="size-4" /> {m.dialog_world_actions_show_previous_instances()}</DropdownMenu.Item>
								<DropdownMenu.Separator />
								{#if isHome}
									<DropdownMenu.Item onSelect={() => (actions.confirming = { command: 'Reset Home', run: () => actions.act(() => api.delete(accountPath(owner, '/home')), 'Home reset') })}><Wand2 class="size-4" /> {m.dialog_world_actions_reset_home()}</DropdownMenu.Item>
								{:else}
									<DropdownMenu.Item onSelect={() => (actions.confirming = { command: 'Make Home', run: () => actions.act(() => api.put(accountPath(owner, `/home/${encodeURIComponent(worldId)}`)), 'Home updated') })}><Home class="size-4" /> {m.dialog_world_actions_make_home()}</DropdownMenu.Item>
								{/if}
								{#if !isAuthor}
									<DropdownMenu.Item disabled={!hasPersistData} onSelect={() => (actions.confirming = { command: 'Delete Persistent Data', run: () => actions.act(() => api.delete(`${base}/persist`), 'Persistent data deleted') })}><Upload class="size-4" /> {m.dialog_world_actions_delete_persistent_data()}</DropdownMenu.Item>
								{:else}
									<DropdownMenu.Item onSelect={() => promptSave('Rename World', 'Enter world name', 'Valid name is required', 'name', world?.name ?? '', 'World renamed')}><Pencil class="size-4" /> {m.dialog_world_actions_rename()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => promptSave('Change Description', 'Enter world description', 'Valid description is required', 'description', world?.description ?? '', 'World description changed', { multiline: true })}><Pencil class="size-4" /> {m.dialog_world_actions_change_description()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => promptSave('Change Capacity', 'Enter world maximum capacity (hard cap), Max: 80', 'Valid number is required', 'capacity', String(world?.capacity ?? ''), 'World capacity changed', { numeric: true })}><Pencil class="size-4" /> {m.dialog_world_actions_change_capacity()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => promptSave('Change Recommended Capacity', 'Enter world recommended capacity (soft cap)', 'Valid number is required', 'recommendedCapacity', String(world?.recommendedCapacity ?? ''), 'World recommended capacity changed', { numeric: true })}><Pencil class="size-4" /> {m.dialog_world_actions_change_recommended_capacity()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={promptYoutube}><Pencil class="size-4" /> {m.dialog_world_actions_change_preview()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => (tagsOpen = true)}><Pencil class="size-4" /> {m.dialog_world_actions_change_warnings_settings_tags()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => (domainsOpen = true)}><Pencil class="size-4" /> {m.dialog_world_actions_change_allowed_video_player_domains()}</DropdownMenu.Item>
									<DropdownMenu.Item onSelect={() => imageUpload?.pick()}><ImageIcon class="size-4" /> {m.dialog_world_actions_change_image()}</DropdownMenu.Item>
									{#if world.unityPackageUrl}
										<DropdownMenu.Item onSelect={() => openPackage(world?.unityPackageUrl)}><Download class="size-4" /> {m.dialog_world_actions_download_package()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Separator />
									{#if isPublished}
										<DropdownMenu.Item onSelect={() => (actions.confirming = { command: 'Unpublish', run: () => actions.act(() => api.delete(`${base}/publish`), 'World unpublished') })}><Eye class="size-4" /> {m.dialog_world_actions_unpublish()}</DropdownMenu.Item>
									{:else}
										<DropdownMenu.Item onSelect={() => (actions.confirming = { command: 'Publish To Labs', run: () => actions.act(() => api.put(`${base}/publish`), 'World published') })}><Eye class="size-4" /> {m.dialog_world_actions_publish_to_labs()}</DropdownMenu.Item>
									{/if}
									<DropdownMenu.Item disabled={!hasPersistData} onSelect={() => (actions.confirming = { command: 'Delete Persistent Data', run: () => actions.act(() => api.delete(`${base}/persist`), 'Persistent data deleted') })}><Upload class="size-4" /> {m.dialog_world_actions_delete_persistent_data()}</DropdownMenu.Item>
									<DropdownMenu.Item
										variant="destructive"
										onSelect={() =>
											(actions.confirming = {
												command: 'Delete',
												run: async () => {
													await actions.act(() => api.delete(base), 'World deleted', false);
													if (!actions.error) userDialog.back();
												}
											})}><Trash2 class="size-4" /> {m.dialog_world_actions_delete()}</DropdownMenu.Item
									>
								{/if}
							</DropdownMenu.Content>
						</DropdownMenu.Root>
						<div class="flex size-9 flex-none flex-col self-start">
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<Button
											{...props}
											class="h-1/2 w-full rounded-t-lg rounded-b-none border p-0 has-[>svg]:px-0 {rating === 'like' ? 'border-primary' : ''}"
											variant={rating === 'like' ? 'default' : 'outline'}
											aria-label={m.dialog_world_actions_like()}
											onclick={() => setRating('like')}><ThumbsUp class="text-xs" /></Button
										>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="top">{m.dialog_world_actions_like()}</Tooltip.Content>
							</Tooltip.Root>
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<Button
											{...props}
											class="h-1/2 w-full rounded-t-none rounded-b-lg border-t-0 border p-0 has-[>svg]:px-0 {rating === 'dislike' ? 'border-primary' : ''}"
											variant={rating === 'dislike' ? 'default' : 'outline'}
											aria-label={m.dialog_world_actions_dislike()}
											onclick={() => setRating('dislike')}><ThumbsDown class="text-xs" /></Button
										>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="bottom">{m.dialog_world_actions_dislike()}</Tooltip.Content>
							</Tooltip.Root>
						</div>
					</div>

					<div class="mt-2 flex flex-wrap items-center gap-1">
						<Badge variant="outline">{isLabs ? 'Labs' : world.releaseStatus === 'public' ? 'Public' : 'Private'}</Badge>
						{#if platforms.has('standalonewindows')}
							<Badge variant="outline" class="text-platform-pc border-platform-pc!" title="PC"><Monitor class="text-platform-pc h-4 w-4" />{#if fileAnalysis.standalonewindows?._fileSize}<span class="text-platform-pc ml-1.5 border-l-[0.8px] border-solid pl-1.5">{fileAnalysis.standalonewindows._fileSize}</span>{/if}</Badge>
						{/if}
						{#if platforms.has('android')}
							<Badge variant="outline" class="text-platform-quest border-platform-quest!" title="Quest"><Smartphone class="text-platform-quest h-4 w-4" />{#if fileAnalysis.android?._fileSize}<span class="text-platform-quest ml-1.5 border-l-[0.8px] border-solid pl-1.5">{fileAnalysis.android._fileSize}</span>{/if}</Badge>
						{/if}
						{#if platforms.has('ios')}
							<Badge variant="outline" class="text-platform-ios border-platform-ios!" title={m.dialog_group_event_edit_platform_values_ios()}><Apple class="text-platform-ios h-4 w-4" />{#if fileAnalysis.ios?._fileSize}<span class="text-platform-ios ml-1.5 border-l-[0.8px] border-solid pl-1.5">{fileAnalysis.ios._fileSize}</span>{/if}</Badge>
						{/if}
						{#if tags.includes('feature_avatar_scaling_disabled')}<Badge variant="outline">{m.dialog_world_tags_avatar_scaling_disabled()}</Badge>{/if}
						{#if tags.includes('feature_focus_view_disabled')}<Badge variant="outline">{m.dialog_world_tags_focus_view_disabled()}</Badge>{/if}
						{#if world.unityPackageUrl}<Badge variant="outline">{m.dialog_world_tags_future_proofing()}</Badge>{/if}
						{#if bundle.inCache}<Badge variant="outline" class="cursor-pointer" onclick={() => bundle.open()}>{bundle.size} | {m.dialog_world_tags_cache()}</Badge>{/if}
					</div>
					{#if contentTags.length}
						<div class="mt-1 flex flex-wrap gap-1">
							{#each contentTags as tag (tag)}
								<Badge variant="outline">{contentLabel(tag)}</Badge>
							{/each}
						</div>
					{/if}
					{#if actions.error}
						<p class="mt-2 text-xs text-destructive">{actions.error}</p>
					{/if}
				</div>
			</div>

			<div class="mt-2.5 flex flex-col gap-2.5">
				<SectionCard title={m.dialog_user_info_vrcx_info()}>
					<div class="flex flex-col gap-1.5">
						<InfoRow label={m.dialog_world_info_last_visited()} value={timeAgo(stats?.lastVisit)} title={formatDate(stats?.lastVisit)} truncate />
						<button type="button" class="{INFO_ROW} cursor-pointer text-left" title={m.dialog_user_info_open_previous_instance()} onclick={() => (previousOpen = true)}>
							<span class="shrink-0 text-muted-foreground">{m.dialog_world_info_visit_count()}</span>
							<span class="text-right text-muted-foreground">{stats?.visitCount ? String(stats.visitCount) : NONE}</span>
						</button>
						<InfoRow label={m.dialog_world_info_time_spent()} value={stats?.timeSpentMs ? timeToText(stats.timeSpentMs) : NONE} truncate />
					</div>
				</SectionCard>
				<SectionCard title={m.dialog_world_info_header()}>
					<div class="flex flex-col gap-1.5">
						{#if world.previewYoutubeId}
							<a class={INFO_ROW} href={`https://www.youtube.com/watch?v=${world.previewYoutubeId}`} target="_blank" rel="noreferrer" onclick={(event) => { event.preventDefault(); openExternalLink(event.currentTarget.href); }}>
								<span class="shrink-0 text-muted-foreground">{m.dialog_world_info_youtube_preview()}</span>
								<span class="truncate text-right text-muted-foreground">{world.previewYoutubeId}</span>
							</a>
						{/if}
						<InfoRow label={m.dialog_world_info_created()} value={timeAgo(world.created_at)} title={formatDate(world.created_at)} truncate />
						<div class={INFO_ROW}>
							<span class="inline-flex shrink-0 items-center text-muted-foreground">
								{m.dialog_world_info_last_updated()}
								{#if Object.keys(fileAnalysis).length}
									<!-- The original's worldDialogPlatformCreatedAt: each platform's newest build. -->
									<Tooltip.Root>
										<Tooltip.Trigger><ChevronDown class="size-3" /></Tooltip.Trigger>
										<Tooltip.Content side="right">
											{#each Object.entries(platformDates ?? {}) as [platform, createdAt] (platform)}
												<div class="flex w-full justify-between">
													<span class="mr-1">{platform}:</span>
													<span>{formatDate(createdAt)}</span>
												</div>
											{/each}
										</Tooltip.Content>
									</Tooltip.Root>
								{/if}
							</span>
							<span class="truncate text-right text-muted-foreground" title={formatDate(world.updated_at)}>{timeAgo(world.updated_at)}</span>
						</div>
						<InfoRow label={m.dialog_world_info_capacity()} value={`${commaNumber(world.recommendedCapacity)} (${commaNumber(world.capacity)})`} truncate />
						<InfoRow label={m.dialog_world_info_author_tags()} value={authorTags || NONE} title={authorTags} truncate />
						<InfoRow label={m.dialog_world_info_visits()} value={commaNumber(world.visits)} truncate />
						<InfoRow label={m.dialog_world_info_favorites()} value={`${commaNumber(world.favorites)}${favoriteRate !== undefined ? ` (${favoriteRate}%)` : ''}`} truncate />
						{#if hasDate(world.labsPublicationDate)}
							<InfoRow label={m.dialog_world_info_labs_publication_date()} value={timeAgo(world.labsPublicationDate)} title={formatDate(world.labsPublicationDate)} truncate />
						{/if}
						{#if hasDate(world.publicationDate)}
							<InfoRow
								label={m.dialog_world_info_publication_date()}
								value={timeAgo(world.publicationDate)}
								title={hasDate(world.labsPublicationDate) ? `${formatDate(world.publicationDate)}\nTime In Labs: ${timeToText(new Date(world.publicationDate!).getTime() - new Date(world.labsPublicationDate!).getTime())}` : formatDate(world.publicationDate)}
								truncate
							/>
						{/if}
						<InfoRow label={m.dialog_world_info_heat()} value={commaNumber(world.heat)} truncate />
						<InfoRow label={m.dialog_world_info_popularity()} value={commaNumber(world.popularity)} truncate />
						<InfoRow label={m.dialog_world_info_version()} value={String(world.version ?? NONE)} truncate />
						<InfoRow label={m.dialog_world_info_platform()} value={platformLines ? platformLines.split('\n')[0] : NONE} title={platformLines} truncate />
						<CopyIdRow label={m.dialog_world_info_id()} oncopy={() => actions.copy(worldId, 'World ID copied to clipboard')} />
					</div>
				</SectionCard>
			</div>
		</div>

		<div class="flex min-h-0 min-w-0 flex-1 flex-col">
			<TabsPrimitive.Root bind:value={activeTab} class="flex min-h-0 w-full min-w-0 flex-1 flex-col">
				<div class={TAB_LIST_WRAP}>
					<TabsPrimitive.List class={TAB_LIST}>
						<TabsPrimitive.Trigger value="instances" class={TAB_TRIGGER}>{m.dialog_world_instances_header()}</TabsPrimitive.Trigger>
						{#if world.storeId}<TabsPrimitive.Trigger value="store" class={TAB_TRIGGER}>{m.dialog_world_store_header()}</TabsPrimitive.Trigger>{/if}
						<TabsPrimitive.Trigger value="json" class={TAB_TRIGGER}>{m.dialog_world_json_header()}</TabsPrimitive.Trigger>
					</TabsPrimitive.List>
				</div>

				<TabsPrimitive.Content value="instances" class={TAB_CONTENT}>
					<div class="my-2 {PANEL}">
						<div class="mb-2 flex items-start justify-between border-b border-border pb-2">
							<div class={SECTION}>{m.dialog_world_instances_header()}</div>
							<div class="flex items-center gap-1 text-[12px] text-muted-foreground">
								<span class="inline-flex items-center gap-1 leading-none"><Globe2 class="size-3" /> Public {world.publicOccupants ?? 0}</span>
								<span class="ml-2 inline-flex items-center gap-1 leading-none"><LockKeyhole class="size-3" /> Private {world.privateOccupants ?? 0}</span>
							</div>
						</div>
						{#each rooms as room (room.location)}
							{@const L = parseLocation(room.location)}
							{@const region = resolveRegion(L)}
							{@const creatorId = instanceCreator(room.location, L.userId)}
							<div style="margin: 6px 0">
								<div class="flex flex-wrap items-center gap-2 overflow-hidden text-sm text-ellipsis whitespace-nowrap">
									<LocationWorld class="w-fit max-w-full" location={room.location} shortName={room.shortName} worldDialogShortName={shortName} currentUserId={vrchatAuth.ownerUserId} bold={room.location === location} />
									{#if room.occupants}<span class="text-muted-foreground">({room.occupants})</span>{/if}
									<InstanceActions location={room.location} shortName={room.shortName} friendIds={new Set(friends.map((f) => f.userId))} />
									<InstanceInfo location={room.location} friendCount={room.friends.length} />
								</div>
								{#if creatorId || room.friends.length}
									<div class="flex flex-wrap items-start" style="margin: 8px 0">
										{#if creatorId}
											{@const creator = friends.find((f) => f.userId === creatorId)}
											<PersonRow userId={creatorId} name={creator?.displayName ?? userNames.get(creatorId) ?? creatorId} image={creator?.thumbnailImageUrl} friend={creator} detail="Instance Creator" />
										{/if}
										{#each room.friends as friend (friend.userId)}
											<PersonRow userId={friend.userId} name={friend.displayName} image={friend.thumbnailImageUrl} {friend} detail={friend.locationSince ? undefined : NONE} timed />
										{/each}
									</div>
								{/if}
							</div>
						{:else}
							<p class="text-xs text-muted-foreground">{NONE}</p>
						{/each}
					</div>
					{#if world.description && world.name !== world.description}
						<div class={CARD}>
							<div class="flex justify-between border-b border-border pb-2"><div class={SECTION}>{m.dialog_world_info_description()}</div></div>
							<div class="flex items-start"><span class="flex-1 py-2 wrap-break-word whitespace-pre-wrap">{world.description}</span></div>
						</div>
					{/if}
					<MemoCard path={`/1/worlds/${encodeURIComponent(worldId)}/memo`} />
				</TabsPrimitive.Content>

				{#if world.storeId}
					<TabsPrimitive.Content value="store" class={TAB_CONTENT}>
						<WorldStoreTab path={base} storeId={world.storeId} />
					</TabsPrimitive.Content>
				{/if}

				<TabsPrimitive.Content value="json" class={TAB_CONTENT}>
					<JsonCard value={world} fileName={worldId} extra={fileAnalysis} onrefresh={refresh} />
				</TabsPrimitive.Content>
			</TabsPrimitive.Root>
		</div>
	</div>

	{#if isAuthor}
		<SetWorldTagsDialog bind:open={tagsOpen} path={base} oldTags={world.tags ?? []} oldDisabledPropAbilities={world.disabledPropAbilities ?? []} onsaved={refresh} />
		<WorldAllowedDomainsDialog bind:open={domainsOpen} path={base} urlList={world.urlList ?? []} onsaved={refresh} />
		<ContentImageUpload bind:this={imageUpload} path={base} imageUrl={world.imageUrl ?? ''} title={m.dialog_change_content_image_world()} onuploaded={refresh} />
	{/if}
	<PreviousInstancesDialog bind:open={previousOpen} kind="world" id={worldId} name={world?.name} />
	<ActionDialogs bind:confirming={actions.confirming} bind:prompting={actions.prompting} busy={actions.busy} />

{/if}
