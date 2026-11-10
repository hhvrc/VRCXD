<script lang="ts" module>
	// What the page held when last left, for coming back to it.
	const kept = new Map<string, unknown>();
</script>

<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { launchHandoff } from '#lib/instances/launch-handoff.svelte.js';
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import { toast } from 'svelte-sonner';
	import { untrack } from 'svelte';
	import { page } from '$app/state';
	import ArrowLeft from '@lucide/svelte/icons/arrow-left';
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import SettingsIcon from '@lucide/svelte/icons/settings';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import UserIcon from '@lucide/svelte/icons/user';
	import Users from '@lucide/svelte/icons/users';
	import X from '@lucide/svelte/icons/x';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { avatarHref, groupHref, imageSrc, worldHref } from '#lib/api/links.js';
	import { replaceBioSymbols } from '#lib/helpers/format.js';
	import { readSetting, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { languageClass, languagesOf, trustInfo } from '#lib/people/user-display.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Search page: users, worlds, a remote avatar database, groups and the avatar
	// marketplace, each asked through GET /1/accounts/{id}/search/*.
	type Tab = 'user' | 'world' | 'avatar' | 'group' | 'marketplace';
	type Json = Record<string, any>; // eslint-disable-line @typescript-eslint/no-explicit-any
	type WorldRow = { index: number; name: string; sortHeading?: string; sortOrder?: string; sortOwnership?: string; tag?: string };

	const PAGE = 60;
	const PROVIDERS_KEY = 'VRCX_avatarRemoteDatabaseProviderList';
	const PROVIDER_KEY = 'VRCX_avatarRemoteDatabaseProvider';
	const DEFAULT_PROVIDERS = ['https://api.avtrdb.com/v3/avatar/search/vrcx'];
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);

	let tab = $state<Tab>(kept.has('tab') ? (kept.get('tab') as never) : 'user');
	let text = $state(kept.has('text') ? (kept.get('text') as never) : '');
	let loading = $state(false);

	let users = $state.raw<Json[]>(kept.has('users') ? (kept.get('users') as never) : []);
	let userOffset = $state(kept.has('userOffset') ? (kept.get('userOffset') as never) : 0);
	let userQuery = $state(kept.has('userQuery') ? (kept.get('userQuery') as never) : { search: '', sort: 'relevance' });
	let sortByLastLogin = $state(kept.has('sortByLastLogin') ? (kept.get('sortByLastLogin') as never) : false);

	let worlds = $state.raw<Json[]>(kept.has('worlds') ? (kept.get('worlds') as never) : []);
	let worldParams = $state<Record<string, string | number | boolean | undefined>>(kept.has('worldParams') ? (kept.get('worldParams') as never) : { offset: 0 });
	let worldLabs = $state(kept.has('worldLabs') ? (kept.get('worldLabs') as never) : false);
	let worldRows = $state.raw<WorldRow[]>([]);
	let worldCategory = $state<string>(kept.has('worldCategory') ? (kept.get('worldCategory') as never) : '');

	let avatars = $state.raw<Json[]>(kept.has('avatars') ? (kept.get('avatars') as never) : []);
	let avatarPage = $state(kept.has('avatarPage') ? (kept.get('avatarPage') as never) : 0);
	let providers = $state<string[]>(readSetting<string[]>(PROVIDERS_KEY, DEFAULT_PROVIDERS));
	let provider = $state(readSetting<string>(PROVIDER_KEY, '') || readSetting<string[]>(PROVIDERS_KEY, DEFAULT_PROVIDERS)[0] || '');
	let providerDialog = $state(false);
	// A vrcx://addavatardb/... launch command: the provider is saved; show the list, as the original does.
	$effect(() => {
		if (!launchHandoff.showProviders) return;
		launchHandoff.showProviders = false;
		providers = readSetting<string[]>(PROVIDERS_KEY, DEFAULT_PROVIDERS);
		tab = 'avatar';
		providerDialog = true;
	});

	let groups = $state.raw<Json[]>(kept.has('groups') ? (kept.get('groups') as never) : []);
	let groupOffset = $state(kept.has('groupOffset') ? (kept.get('groupOffset') as never) : 0);
	let groupQuery = $state(kept.has('groupQuery') ? (kept.get('groupQuery') as never) : '');

	let market = $state.raw<Json[]>(kept.has('market') ? (kept.get('market') as never) : []);
	let marketOffset = $state(kept.has('marketOffset') ? (kept.get('marketOffset') as never) : 0);
	let marketQuery = $state(kept.has('marketQuery') ? (kept.get('marketQuery') as never) : '');
	let marketHasNext = $state(kept.has('marketHasNext') ? (kept.get('marketHasNext') as never) : false);

	const account = $derived(vrchatAuth.ownerUserId);
	const path = (rest: string) => accountPath(account, `/search${rest}`);
	const avatarSlice = $derived(avatars.slice(avatarPage * 10, avatarPage * 10 + 10));

	const pager = $derived.by(() => {
		switch (tab) {
			case 'user':
				return { show: users.length > 0 && !loading, prev: !userOffset, next: users.length < PAGE, go: (n: number) => loadUsers(n) };
			case 'world':
				return { show: worlds.length > 0 && !loading, prev: !worldParams.offset, next: worlds.length < PAGE, go: (n: number) => loadWorlds(n) };
			case 'avatar':
				return {
					show: avatarSlice.length > 0 && !loading,
					prev: !avatarPage,
					next: avatars.length < 10 || (avatarPage + 1) * 10 >= avatars.length,
					go: (n: number) => (avatarPage = Math.max(0, avatarPage + n))
				};
			case 'marketplace':
				return { show: market.length > 0 && !loading, prev: !marketOffset, next: !marketHasNext, go: (n: number) => loadMarket(n) };
			case 'group':
				return { show: groups.length > 0 && !loading, prev: !groupOffset, next: groups.length < PAGE, go: (n: number) => loadGroups(n) };
			default:
				return { show: false, prev: true, next: true, go: () => {} };
		}
	});

	async function run<T>(work: () => Promise<T>): Promise<T | undefined> {
		loading = true;
		try {
			return await work();
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}

	const dedupe = (rows: Json[]) => [...new Map(rows.map((row) => [row.id, row])).values()];

	async function loadUsers(go = 0) {
		userOffset = Math.max(0, userOffset + PAGE * go);
		const rows = await run(() => api.get<Json[]>(path('/users'), { ...userQuery, offset: userOffset }));
		// The original drops rows VRChat sends without a name.
		if (rows) users = dedupe(rows.filter((row) => row.displayName));
	}

	function searchWorld(row: Partial<WorldRow>) {
		const params: Record<string, string | number | boolean | undefined> = { offset: 0 };
		switch (row.sortHeading) {
			case 'featured':
				params.sort = 'order';
				params.featured = true;
				break;
			case 'trending':
				params.sort = 'popularity';
				params.featured = false;
				break;
			case 'updated':
			case 'created':
			case 'shuffle':
			case 'heat':
				params.sort = row.sortHeading;
				if (row.sortHeading === 'heat') params.featured = false;
				break;
			case 'publication':
				params.sort = 'publicationDate';
				break;
			case 'active':
				params.option = 'active';
				break;
			case 'recent':
				params.option = 'recent';
				break;
			case 'favorite':
				params.option = 'favorites';
				break;
			case 'labs':
				params.sort = 'labsPublicationDate';
				break;
			default:
				params.sort = 'relevance';
				params.search = replaceBioSymbols(text);
		}
		params.order = row.sortOrder || 'descending';
		if (row.sortOwnership === 'mine') {
			params.user = 'me';
			params.releaseStatus = 'all';
		}
		if (row.tag) params.tag = row.tag;
		if (!worldLabs) params.tag = params.tag ? `${params.tag},system_approved` : 'system_approved';
		worldParams = params;
		loadWorlds();
	}

	async function loadWorlds(go = 0) {
		worldParams.offset = Math.max(0, Number(worldParams.offset ?? 0) + PAGE * go);
		const rows = await run(() => api.get<Json[]>(path('/worlds'), worldParams));
		if (rows) worlds = dedupe(rows);
	}

	async function loadAvatars() {
		const query = text;
		const found = await run(async () =>
			query.length >= 3 && provider ? api.get<Json[]>(path('/avatars'), { provider, search: query }) : ([] as Json[])
		);
		if (!found) toast.error(`Avatar search failed for ${query} with ${provider}`);
		const rows = (found ?? []).map((avatar) => ({ authorName: '', name: '', imageUrl: '', thumbnailImageUrl: '', ...avatar, id: avatar.id ?? avatar.Id }));
		avatarPage = 0;
		avatars = dedupe(rows);
	}

	async function loadGroups(go = 0) {
		groupOffset = Math.max(0, groupOffset + PAGE * go);
		const rows = await run(() => api.get<Json[]>(path('/groups'), { query: groupQuery, offset: groupOffset }));
		if (rows) groups = dedupe(rows);
	}

	async function loadMarket(go = 0) {
		marketOffset = Math.max(0, marketOffset + 50 * go);
		const page = await run(() => api.get<{ results?: Json[]; hasNext?: boolean }>(path('/marketplace'), { search: marketQuery, offset: marketOffset }));
		if (!page) return;
		market = dedupe((page.results ?? []).map((a) => ({ ...a, name: replaceBioSymbols(a.name), authorName: replaceBioSymbols(a.authorName) })));
		marketHasNext = Boolean(page.hasNext);
	}

	function search() {
		if (tab === 'avatar' && text.length < 3) {
			toast.warning(m.view_search_avatar_min_chars_warning());
			return;
		}
		switch (tab) {
			case 'user':
				userQuery = { search: text, sort: sortByLastLogin ? 'last_login' : 'relevance' };
				userOffset = 0;
				loadUsers();
				break;
			case 'world':
				worldCategory = '';
				searchWorld({});
				break;
			case 'avatar':
				loadAvatars();
				break;
			case 'marketplace':
				marketQuery = replaceBioSymbols(text);
				marketOffset = 0;
				loadMarket();
				break;
			case 'group':
				groupQuery = replaceBioSymbols(text);
				groupOffset = 0;
				loadGroups();
				break;
		}
	}

	function clearAll() {
		users = [];
		userOffset = 0;
		worlds = [];
		worldParams = { offset: 0 };
		avatars = [];
		avatarPage = 0;
		groups = [];
		groupOffset = 0;
		market = [];
		marketOffset = 0;
		marketHasNext = false;
		text = '';
	}

	function chooseCategory(value: string) {
		worldCategory = value;
		searchWorld(worldRows.find((row) => String(row.index) === value) ?? {});
	}

	function setProvider(value: string) {
		provider = value;
		storeSetting(PROVIDER_KEY, value);
	}

	function saveProviders() {
		providers = providers.map((p) => p.trim()).filter(Boolean);
		storeSetting(PROVIDERS_KEY, providers);
		setProvider(providers[0] ?? '');
	}

	const price = (value: number) => `${value.toLocaleString()}V`;

	function marketPrice(avatar: Json): string {
		const prices = (avatar.publishedListings ?? []).map((l: Json) => l.priceTokens).filter((p: unknown) => typeof p === 'number');
		const low = prices.length ? Math.min(...prices) : avatar.lowestPrice;
		const high = prices.length ? Math.max(...prices) : avatar.highestPrice;
		if (low == null) return '';
		return high == null || high === low ? price(low) : `${price(low)} – ${price(high)}`;
	}

	function userImage(user: Json): string {
		return user.iconUrl || user.userIcon || user.profilePicOverrideThumbnail || user.profilePicOverride || user.currentAvatarThumbnailImageUrl || '';
	}

	$effect(() => {
		if (!account) return;
		api.get<WorldRow[]>(path('/world-rows')).then(
			(rows) => (worldRows = rows),
			() => (worldRows = [])
		);
	});

	// lookupUser's fallback: a name no exact match was found for is searched for here.
	$effect(() => {
		const name = page.url.searchParams.get('user');
		if (!name || !account) return;
		untrack(() => {
			tab = 'user';
			text = name;
			search();
		});
	});

	// The original keeps Search alive between pages: the tab, the text and the results stay.
	$effect(() => {
		kept.set('tab', $state.snapshot(tab));
		kept.set('text', $state.snapshot(text));
		kept.set('users', $state.snapshot(users));
		kept.set('userOffset', $state.snapshot(userOffset));
		kept.set('userQuery', $state.snapshot(userQuery));
		kept.set('sortByLastLogin', $state.snapshot(sortByLastLogin));
		kept.set('worlds', $state.snapshot(worlds));
		kept.set('worldParams', $state.snapshot(worldParams));
		kept.set('worldLabs', $state.snapshot(worldLabs));
		kept.set('worldCategory', $state.snapshot(worldCategory));
		kept.set('avatars', $state.snapshot(avatars));
		kept.set('avatarPage', $state.snapshot(avatarPage));
		kept.set('groups', $state.snapshot(groups));
		kept.set('groupOffset', $state.snapshot(groupOffset));
		kept.set('groupQuery', $state.snapshot(groupQuery));
		kept.set('market', $state.snapshot(market));
		kept.set('marketOffset', $state.snapshot(marketOffset));
		kept.set('marketQuery', $state.snapshot(marketQuery));
		kept.set('marketHasNext', $state.snapshot(marketHasNext));
	});

	function onKey(event: KeyboardEvent) {
		if (!event.altKey || (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight')) return;
		const current = pager;
		if (!current.show) return;
		if (event.key === 'ArrowLeft' && !current.prev) current.go(-1);
		if (event.key === 'ArrowRight' && !current.next) current.go(1);
	}
</script>

<svelte:window onkeydown={onKey} />

<svelte:head>
	<title>Search · VRCXD</title>
</svelte:head>

{#snippet empty()}
	<div class="flex h-full items-center justify-center p-6 text-sm text-muted-foreground">{m.common_no_data()}</div>
{/snippet}

{#snippet spinner()}
	<div class="flex h-full items-center justify-center"><Spinner class="text-2xl" /></div>
{/snippet}

{#snippet pagination()}
	{#if pager.show}
		<div class="flex h-15 shrink-0 items-center justify-center">
			<div class="flex rounded-lg shadow-lg">
				<Button variant="outline" size="sm" class="rounded-r-none" disabled={pager.prev} onclick={() => pager.go(-1)}>
					<ArrowLeft />
					<kbd class="ml-1 rounded bg-muted px-1 text-xs">{isMac ? '⌥' : 'Alt'}</kbd><kbd class="rounded bg-muted px-1 text-xs">←</kbd>
				</Button>
				<Button variant="outline" size="sm" class="rounded-l-none border-l-0" disabled={pager.next} onclick={() => pager.go(1)}>
					<kbd class="ml-1 rounded bg-muted px-1 text-xs">{isMac ? '⌥' : 'Alt'}</kbd><kbd class="rounded bg-muted px-1 text-xs">→</kbd>
					<ArrowRight />
				</Button>
			</div>
		</div>
	{/if}
{/snippet}

{#snippet card(href: string, image: string, name: string, lines: string[], aspect = 'aspect-16/10')}
	<a {href} class="block cursor-pointer overflow-hidden rounded-lg border p-3 hover:bg-muted/50">
		{#if image}<img src={imageSrc(image)} alt={name} loading="lazy" class={['w-full rounded-lg object-cover', aspect]} />{/if}
		<Tooltip.Root>
			<Tooltip.Trigger class="mt-2 block w-full truncate text-left text-sm font-medium">{name}</Tooltip.Trigger>
			<Tooltip.Content side="top">{name}</Tooltip.Content>
		</Tooltip.Root>
		{#each lines.filter(Boolean) as line, index (index)}<p class="line-clamp-1 text-xs text-muted-foreground">{line}</p>{/each}
	</a>
{/snippet}

<Tooltip.Provider>
	{#if !account}
		<Card.Root>
			<Card.Header><Card.Description>Log in to VRChat to search.</Card.Description></Card.Header>
		</Card.Root>
	{:else}
		<Tabs.Root bind:value={tab} class="flex h-full min-h-0 flex-1 flex-col overflow-hidden">
			<div class="mx-0 mt-0 mb-2 flex items-center gap-5">
				<Tabs.List>
					<Tabs.Trigger value="user">{m.view_search_user_header()}</Tabs.Trigger>
					<Tabs.Trigger value="world">{m.view_search_world_header()}</Tabs.Trigger>
					<Tabs.Trigger value="avatar">{m.view_feed_filters_Avatar()}</Tabs.Trigger>
					<Tabs.Trigger value="group">{m.view_tools_group_header()}</Tabs.Trigger>
					<Tabs.Trigger value="marketplace">{m.view_search_marketplace_header()}</Tabs.Trigger>
				</Tabs.List>
				<div class="flex min-w-0 flex-1 items-center">
					<div class="relative flex-1">
						<Input
							bind:value={text}
							placeholder={tab === 'avatar' ? 'Type at least 3 characters' : 'Search'}
							class="pr-8"
							onkeyup={(event: KeyboardEvent) => event.key === 'Enter' && search()}
						/>
						{#if text}
							<button type="button" aria-label={m.view_favorite_clear()} class="absolute top-1/2 right-2 -translate-y-1/2 cursor-pointer text-muted-foreground" onclick={() => (text = '')}>
								<X class="size-4" />
							</button>
						{/if}
					</div>
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="ml-2 rounded-full" size="icon" variant="ghost" aria-label={m.view_search_clear_results_tooltip()} onclick={clearAll}><Trash2 /></Button>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="bottom">{m.view_search_clear_results_tooltip()}</Tooltip.Content>
					</Tooltip.Root>
				</div>
			</div>

			<Tabs.Content value="user" class="flex min-h-0 flex-1 flex-col">
				<div class="flex min-h-0 flex-9 flex-col">
					<div class="mb-3 flex shrink-0 justify-end">
						<label class="ml-2 inline-flex items-center gap-2 text-sm">
							<Checkbox bind:checked={sortByLastLogin} />
							<span>{m.view_search_user_sort_by_last_logged_in()}</span>
						</label>
					</div>
					<div class="min-h-0 flex-1 overflow-y-auto">
						{#if loading}{@render spinner()}
						{:else if users.length > 0}
							{#each users as user (user.id)}
								{@const trust = trustInfo(user.tags ?? [], user.developerType)}
								<button type="button" class="x-hover-list flex w-full cursor-pointer items-center gap-2 rounded-md px-3 py-2 text-left hover:bg-muted" onclick={() => userDialog.open(user.id)}>
									<div class="relative size-10 shrink-0">
										<Avatar.Root class="size-full">
											<Avatar.Image src={imageSrc(userImage(user))} loading="lazy" />
											<Avatar.Fallback><UserIcon class="size-5 text-muted-foreground" /></Avatar.Fallback>
										</Avatar.Root>
										<IconFrame iconFrame={user.iconFrame} />
									</div>
									<div class="min-w-0">
										<div class="flex max-w-full items-center gap-1.5 text-sm font-medium">
											<span class="truncate">{user.displayName}</span>
											<span class="shrink-0 text-xs font-normal" style="color: {trust.color}">{trust.label}</span>
											{#each languagesOf(user.tags ?? []) as item (item.key)}
												<span class={['flags shrink-0', languageClass(item.key)]} title={item.value}></span>
											{/each}
										</div>
										{#if user.bio}<p class="line-clamp-1 text-xs text-muted-foreground">{replaceBioSymbols(user.bio)}</p>{/if}
									</div>
								</button>
							{/each}
						{:else}{@render empty()}{/if}
					</div>
				</div>
				{@render pagination()}
			</Tabs.Content>

			<Tabs.Content value="world" class="flex min-h-0 flex-1 flex-col">
				<div class="flex min-h-0 flex-9 flex-col">
					<div class="mb-4 inline-flex w-full shrink-0 justify-end gap-2">
						<label class="inline-flex items-center gap-2 text-sm">
							<Checkbox bind:checked={worldLabs} />
							<span>{m.view_search_world_community_lab()}</span>
						</label>
						<Select.Root type="single" value={worldCategory} onValueChange={chooseCategory}>
							<Select.Trigger size="sm">{worldRows.find((row) => String(row.index) === worldCategory)?.name ?? 'Search by category'}</Select.Trigger>
							<Select.Content>
								{#each worldRows as row (row.index)}<Select.Item value={String(row.index)}>{row.name}</Select.Item>{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="min-h-0 flex-1 overflow-y-auto">
						{#if loading}{@render spinner()}
						{:else if worlds.length > 0}
							<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
								{#each worlds as world (world.id)}
									{@render card(worldHref(world.id), world.thumbnailImageUrl, world.name, [world.occupants ? `${world.authorName} (${world.occupants})` : world.authorName])}
								{/each}
							</div>
						{:else}{@render empty()}{/if}
					</div>
				</div>
				{@render pagination()}
			</Tabs.Content>

			<Tabs.Content value="avatar" class="flex min-h-0 flex-1 flex-col">
				<div class="flex min-h-0 flex-9 flex-col">
					<div class="mb-3 flex shrink-0 items-center justify-end gap-2">
						{#if providers.length > 0}
							<Select.Root type="single" value={provider} onValueChange={setProvider}>
								<Select.Trigger size="sm">{provider || 'Search Provider'}</Select.Trigger>
								<Select.Content>
									{#each providers.filter(Boolean) as item (item)}<Select.Item value={item}>{item}</Select.Item>{/each}
								</Select.Content>
							</Select.Root>
						{:else}
							<span class="text-sm text-muted-foreground">{m.view_search_avatar_no_provider()}</span>
						{/if}
						<Button size="sm" variant="outline" aria-label={m.dialog_avatar_database_provider_header()} onclick={() => (providerDialog = true)}><SettingsIcon class="size-4" /></Button>
					</div>
					<div class="mt-2 min-h-0 flex-1 overflow-y-auto">
						{#if loading}{@render spinner()}
						{:else if avatarSlice.length > 0}
							<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
								{#each avatarSlice as avatar (avatar.id)}
									{@render card(avatarHref(avatar.id), avatar.thumbnailImageUrl || avatar.imageUrl, avatar.name, [avatar.authorName])}
								{/each}
							</div>
						{:else}{@render empty()}{/if}
					</div>
				</div>
				{@render pagination()}
			</Tabs.Content>

			<Tabs.Content value="marketplace" class="flex min-h-0 flex-1 flex-col">
				<div class="min-h-0 flex-1 flex-9 overflow-y-auto">
					{#if loading}{@render spinner()}
					{:else if market.length > 0}
						<div class="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3">
							{#each market as avatar (avatar.id)}
								{@render card(avatarHref(avatar.id), avatar.thumbnailImageUrl || avatar.imageUrl, avatar.name, [avatar.authorName, marketPrice(avatar)])}
							{/each}
						</div>
					{:else}{@render empty()}{/if}
				</div>
				{@render pagination()}
			</Tabs.Content>

			<Tabs.Content value="group" class="flex min-h-0 flex-1 flex-col">
				<div class="min-h-0 flex-1 flex-9 overflow-y-auto">
					{#if loading}{@render spinner()}
					{:else if groups.length > 0}
						{#each groups as group (group.id)}
							<a href={groupHref(group.id)} class="x-hover-list flex cursor-pointer items-center gap-2 rounded-md px-3 py-2 hover:bg-muted">
								<Avatar.Root class="size-10 rounded-sm">
									<Avatar.Image src={imageSrc(group.iconUrl)} loading="lazy" />
									<Avatar.Fallback class="rounded-sm"><Users class="size-5 text-muted-foreground" /></Avatar.Fallback>
								</Avatar.Root>
								<div class="min-w-0">
									<div class="max-w-full truncate text-sm font-medium">
										{group.name}
										<span class="font-normal">({group.memberCount})</span>
										<span class="font-mono text-xs font-normal text-muted-foreground">{group.shortCode}.{group.discriminator}</span>
									</div>
									<p class="truncate text-xs text-muted-foreground">{group.description}</p>
								</div>
							</a>
						{/each}
					{:else}{@render empty()}{/if}
				</div>
				{@render pagination()}
			</Tabs.Content>
		</Tabs.Root>
	{/if}
</Tooltip.Provider>

<Dialog.Root bind:open={providerDialog} onOpenChange={(open) => !open && saveProviders()}>
	<Dialog.Content class="bg-background">
		<Dialog.Header><Dialog.Title>{m.dialog_avatar_database_provider_header()}</Dialog.Title></Dialog.Header>
		<div>
			{#each providers as _, index (index)}
				<div class="relative mt-1.5">
					<Input bind:value={providers[index]} class="h-8 pr-8" onchange={saveProviders} />
					<button type="button" aria-label={m.common_actions_remove()} class="absolute top-1/2 right-2 -translate-y-1/2 cursor-pointer opacity-80 hover:opacity-100" onclick={() => { providers = providers.filter((_, i) => i !== index); saveProviders(); }}>
						<Trash2 class="size-4" />
					</button>
				</div>
			{/each}
			<Button size="sm" class="mt-1.5" onclick={() => (providers = [...providers, ''])}>{m.dialog_avatar_database_provider_add_provider()}</Button>
		</div>
	</Dialog.Content>
</Dialog.Root>

