<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { sidebarSettings } from '#lib/settings/sidebar-settings.svelte.js';
	import { untrack } from 'svelte';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import Loader2 from '@lucide/svelte/icons/loader-2';
	import SettingsIcon from '@lucide/svelte/icons/settings';
	import FriendLocationCard from '#lib/components/instance/friend-location-card.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { ApiError } from '#lib/api/client.js';
	import type { Friend } from '#lib/api/types.js';
	import { loadFriendsWithGroups, searchFriends, sameInstanceGroups, type FriendGroup } from '#lib/people/friend-groups.js';
	import { friendComparator } from '#lib/people/friend-sort.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { worldNames } from '#lib/people/names.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's FriendsLocations over GET /friends and the friend favorites. Rows are skipped by
	// the browser while off screen, which stands in for the original's virtual list.
	type Group = FriendGroup;
	type Row =
		| { type: 'header'; key: string; instanceId: string; count: number }
		| { type: 'group-header'; key: string; label: string; count: number; groupKey: string; collapsed: boolean }
		| { type: 'divider'; key: string }
		| { type: 'cards'; key: string; items: Friend[] };

	const BASE_WIDTH = 220;
	const BASE_GAP = 14;

	let friends = $state.raw<Friend[]>([]);
	let groups = $state.raw<Group[]>([]);
	let ready = $state(false);
	let loadError = $state('');
	let segment = $state('online');
	let search = $state('');
	let gridWidth = $state(0);
	let collapsed = $state<Record<string, boolean>>({});
	let showSameInstance = $state(readSetting('VRCX_FriendLocationShowSameInstance', false));
	let showCosmetics = $state(readSetting('VRCX_FriendLocationShowCosmetics', true));
	let cardScale = $state(Math.max(0.6, Number(readSetting<number | string>('VRCX_FriendLocationCardScale', 1)) || 1));
	let cardSpacing = $state(Number(readSetting<number | string>('VRCX_FriendLocationCardSpacing', 1)) || 1);
	// The sidebar's group settings, live: changing them there regroups this page too.
	const divideByGroup = $derived(sidebarSettings.divideByFriendGroup.value);
	const selectedGroups = $derived(sidebarSettings.favoriteGroups);
	const groupOrder = $derived(sidebarSettings.favoriteGroupOrder);

	const options = $derived(
		[
			{ get label() { return m.view_friends_locations_online(); }, value: 'online' },
			{ get label() { return m.view_friends_locations_favorite(); }, value: 'favorite' },
			{ get label() { return m.view_friends_locations_same_instance(); }, value: 'same-instance' },
			{ get label() { return m.view_friends_locations_active(); }, value: 'active' },
			{ get label() { return m.view_friends_locations_offline(); }, value: 'offline' }
		].filter((option) => showSameInstance || option.value !== 'same-instance')
	);

	const term = $derived(search.trim().toLowerCase());
	const sorted = $derived([...friends].sort(friendComparator()));
	const allFavorites = $derived(new Set(groups.flatMap((group) => [...group.members])));
	const shownFavorites = $derived(
		selectedGroups.length === 0 ? allFavorites : new Set(groups.filter((group) => selectedGroups.includes(group.key)).flatMap((group) => [...group.members]))
	);
	const online = $derived(sorted.filter((friend) => friend.state === 'online'));
	const active = $derived(sorted.filter((friend) => friend.state === 'active'));
	const offline = $derived(sorted.filter((friend) => friend.state !== 'online' && friend.state !== 'active'));
	const favoriteOnline = $derived(online.filter((friend) => shownFavorites.has(friend.userId)));
	const onlineRest = $derived(online.filter((friend) => !shownFavorites.has(friend.userId)));

	// Two or more friends in one real instance, biggest first.
	const sameInstance = $derived(sameInstanceGroups(online, (friend) => sidebarData.sameInstanceLocation(friend)));

	const layout = $derived.by(() => {
		const minWidth = BASE_WIDTH * cardScale;
		const gap = Math.max(6, (BASE_GAP + (cardScale - 1) * 10) * cardSpacing);
		const width = Math.max(gridWidth, 0);
		const columns = Math.max(1, Math.floor((width + gap) / (minWidth + gap)) || 1);
		const cardWidth = Math.max(minWidth, (width - gap * (columns - 1)) / columns || minWidth);
		return { minWidth, gap, columns, cardWidth };
	});

	function chunk(items: Friend[], prefix: string): Row[] {
		const rows: Row[] = [];
		for (let index = 0; index < items.length; index += layout.columns) {
			rows.push({ type: 'cards', key: `${prefix}:${index}`, items: items.slice(index, index + layout.columns) });
		}
		return rows;
	}

	function instanceRows(prefix: string): Row[] {
		return sameInstance.flatMap(({ instanceId, members }): Row[] => [
			{ type: 'header', key: `h:${instanceId}`, instanceId, count: members.length },
			...chunk(members, `${prefix}:${instanceId}`)
		]);
	}

	const rows = $derived.by((): Row[] => {
		if (term) {
			const found = searchFriends([favoriteOnline, online, active, offline], term, (friend) => worldNames.get(parseLocation(friend.location ?? '').worldId) ?? '');
			return chunk(found, 'search');
		}
		switch (segment) {
			case 'online': {
				if (showSameInstance) return chunk(onlineRest, 'online');
				const merged = instanceRows('merged');
				const grouped = new Set(sameInstance.flatMap(({ members }) => members.map((friend) => friend.userId)));
				const rest = onlineRest.filter((friend) => !grouped.has(friend.userId));
				if (merged.length > 0 && rest.length > 0) merged.push({ type: 'divider', key: 'divider' });
				return [...merged, ...chunk(rest, 'online')];
			}
			case 'favorite': {
				if (!divideByGroup) return chunk(favoriteOnline, 'favorite');
				const position = (key: string) => groupOrder.indexOf(key);
				return groups
					.filter((group) => selectedGroups.length === 0 || selectedGroups.includes(group.key))
					.map((group) => ({ group, members: favoriteOnline.filter((friend) => group.members.has(friend.userId)) }))
					.filter(({ members }) => members.length > 0)
					.sort((a, b) => {
						const [left, right] = [position(a.group.key), position(b.group.key)];
						if (left !== -1 && right !== -1) return left - right;
						if (left !== -1) return -1;
						if (right !== -1) return 1;
						return a.group.key.localeCompare(b.group.key);
					})
					.flatMap(({ group, members }): Row[] => [
						{ type: 'group-header', key: `gh:${group.key}`, label: group.name, count: members.length, groupKey: group.key, collapsed: !!collapsed[group.key] },
						...(collapsed[group.key] ? [] : chunk(members, `group:${group.key}`))
					]);
			}
			case 'same-instance':
				return instanceRows('same');
			case 'active':
				return chunk(active, 'active');
			case 'offline':
				return chunk(offline, 'offline');
			default:
				return [];
		}
	});

	const showLocation = $derived(segment !== 'active' && segment !== 'offline');

	async function load() {
		if (!vrchatAuth.ownerUserId) return;
		try {
			({ friends, groups } = await loadFriendsWithGroups());
			loadError = '';
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			ready = true;
		}
	}

	function setShowSameInstance(value: boolean) {
		showSameInstance = value;
		storeSetting('VRCX_FriendLocationShowSameInstance', value);
		if (!value && segment === 'same-instance') segment = 'online';
	}

	// The slider and its label follow the drag; the cards rescale once it settles (upstream b9d6e089).
	let cardScaleSlider = $state(untrack(() => cardScale));
	let scaleTimer: ReturnType<typeof setTimeout> | undefined;
	function setScale(value: number) {
		cardScaleSlider = value;
		clearTimeout(scaleTimer);
		scaleTimer = setTimeout(() => {
			cardScale = value;
			storeSetting('VRCX_FriendLocationCardScale', value);
		}, 500);
	}

	function setSpacing(value: number) {
		cardSpacing = value;
		storeSetting('VRCX_FriendLocationCardSpacing', value);
	}

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(load);
		void sidebarData.loadGameInstance();
		const stopGame = watchLive(['visit', 'desktop'], () => sidebarData.loadGameInstance(), 1500);
		const stopFriends = watchLive(['presence', 'user', 'friendship'], load, 1500);
		return () => {
			stopGame();
			stopFriends();
		};
	});
</script>

<svelte:head>
	<title>Friends Locations · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	{#if !vrchatAuth.ownerUserId}
		<Card.Root>
			<Card.Header>
				<Card.Description>Log in to VRChat to see where your friends are.</Card.Description>
			</Card.Header>
		</Card.Root>
	{:else}
		<div class="grid h-full min-h-0 grid-rows-[auto_1fr] gap-4 overflow-hidden">
			<div class="flex items-center gap-5 px-0.5 pt-2">
				<Tabs.Root bind:value={segment} class="gap-0">
					<Tabs.List>
						{#each options as option (option.value)}
							<Tabs.Trigger value={option.value}>{option.label}</Tabs.Trigger>
						{/each}
					</Tabs.List>
				</Tabs.Root>
				<div class="flex flex-1 flex-wrap items-center justify-end gap-3">
					<Input bind:value={search} class="h-9 w-60 flex-1" placeholder={m.view_friends_locations_search_placeholder()} />
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props: tooltip })}
								<div {...tooltip}>
									<Popover.Root>
										<Popover.Trigger>
											{#snippet child({ props })}
												<Button {...props} class="mr-2 rounded-full" size="icon" variant="ghost" aria-label={m.view_login_settings()}><SettingsIcon /></Button>
											{/snippet}
										</Popover.Trigger>
										<Popover.Content side="bottom" class="w-87.5">
											<div class="grid gap-3">
												<div class="flex items-center justify-between">
													<span class="mr-2 text-compact font-medium">{m.view_friends_locations_separate_same_instance_friends()}</span>
													<Switch checked={showSameInstance} onCheckedChange={setShowSameInstance} />
												</div>
												<div class="flex items-center justify-between">
													<span class="mr-2 text-compact font-medium">{m.view_settings_appearance_appearance_show_cosmetics()}</span>
													<Switch
														checked={showCosmetics}
														onCheckedChange={(value) => {
															showCosmetics = value;
															storeSetting('VRCX_FriendLocationShowCosmetics', value);
														}}
													/>
												</div>
												<div class="flex items-center justify-between">
													<span class="mr-2 text-compact font-medium">{m.view_friends_locations_scale()}</span>
													<div class="flex min-w-40 items-center gap-2">
														<span class="text-xs">{Math.round(cardScaleSlider * 100)}%</span>
														<Slider type="single" class="mr-3 w-40" value={cardScaleSlider} min={0.6} max={1} step={0.01} onValueChange={setScale} />
													</div>
												</div>
												<div class="flex items-center justify-between">
													<span class="mr-2 text-compact font-medium">{m.view_friends_locations_spacing()}</span>
													<div class="flex min-w-40 items-center gap-2">
														<span class="text-xs">{Math.round(cardSpacing * 100)}%</span>
														<Slider type="single" class="mr-3 w-40" value={cardSpacing} min={0.25} max={1} step={0.05} onValueChange={setSpacing} />
													</div>
												</div>
											</div>
										</Popover.Content>
									</Popover.Root>
								</div>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">{m.view_login_settings()}</Tooltip.Content>
					</Tooltip.Root>
				</div>
			</div>
			<div class="h-full min-h-0 overflow-auto" bind:clientWidth={gridWidth}>
				{#if loadError}<p class="mb-2 text-sm text-destructive">Couldn't load friends: {loadError}</p>{/if}
				{#if !ready}
					<div class="grid min-h-60 place-items-center"><Loader2 class="animate-spin" size={22} /></div>
				{:else if rows.length > 0}
					<div
						class="w-full p-0.5"
						style="--friend-card-min-width: {Math.round(layout.minWidth)}px; --friend-card-gap: {Math.round(layout.gap)}px; --friend-card-target-width: {Math.round(layout.cardWidth)}px; --friend-grid-columns: {layout.columns}"
					>
						{#each rows as row (row.key)}
							{#if row.type === 'header'}
								<header class="flex items-center gap-1 px-0.5 py-1 text-compact font-semibold">
									<Location class="inline text-xs" location={row.instanceId} />
									<span class="text-xs">({row.count})</span>
								</header>
							{:else if row.type === 'group-header'}
								<button
									type="button"
									class="flex w-full cursor-pointer items-center gap-1.5 px-1 py-1.5 text-left text-compact font-semibold select-none"
									onclick={() => (collapsed[row.groupKey] = !row.collapsed)}
								>
									<ChevronDown class={['size-4 shrink-0 transition-transform duration-200 ease-in-out', row.collapsed && '-rotate-90']} />
									<span class="flex-none">{row.label}</span>
									<span class="text-xs font-normal opacity-70">({row.count})</span>
								</button>
							{:else if row.type === 'divider'}
								<div class="my-2 border-t border-border"></div>
							{:else}
								<div class="friend-row grid justify-start p-0.5" style="contain-intrinsic-size: auto {Math.round((showLocation ? 150 : 110) * cardScale * cardSpacing)}px">
									{#each row.items as friend (friend.userId)}
										<FriendLocationCard {friend} scale={cardScale} spacing={cardSpacing} {showLocation} {showCosmetics} />
									{/each}
								</div>
							{/if}
						{/each}
					</div>
				{:else}
					<div class="grid min-h-60 place-items-center text-[15px] tracking-[0.5px] text-muted-foreground">No matching results</div>
				{/if}
			</div>
		</div>
	{/if}
</Tooltip.Provider>

<style>
	.friend-row {
		content-visibility: auto;
		grid-template-columns: repeat(var(--friend-grid-columns, 1), minmax(var(--friend-card-min-width, 200px), var(--friend-card-target-width, 220px)));
		gap: var(--friend-card-gap, 14px);
		margin-bottom: var(--friend-card-gap, 14px);
	}
</style>
