<script lang="ts">
	import { PERSON_NAME, PERSON_SUBTEXT } from '#lib/details.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import { m } from '#lib/paraglide/messages.js';
	import LocationWorld from '#lib/components/instance/location-world.svelte';
	import { untrack } from 'svelte';
	import Apple from '@lucide/svelte/icons/apple';
	import Home from '@lucide/svelte/icons/house';
	import IdCard from '@lucide/svelte/icons/id-card';
	import ImageIcon from '@lucide/svelte/icons/image';
	import Monitor from '@lucide/svelte/icons/monitor';
	import Smartphone from '@lucide/svelte/icons/smartphone';
	import UserIcon from '@lucide/svelte/icons/user';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc, userHref, worldHref } from '#lib/api/links.js';
	import { formatDateFilter, getFaviconUrl, statusClass, timeToText } from '#lib/helpers/format.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { languageClass, languagesOf, trustInfo } from '#lib/people/user-display.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's PlayerList over GET /1/accounts/{id}/player-list: the current world and who the
	// game log says is in the instance. Photon columns and the event table need the game mod's data,
	// which nothing here receives.
	type Player = {
		userId: string;
		displayName: string;
		joinedAt: string;
		trustLevel: string;
		tags: string[];
		developerType: string;
		status: string;
		statusDescription?: string | null;
		thumbnailImageUrl?: string | null;
		bioLinks: string[];
		note?: string | null;
		memo?: string | null;
		platform?: string | null;
		isFriend: boolean;
		isBlocked: boolean;
		isMuted: boolean;
		isAvatarInteractionDisabled: boolean;
		ageVerified: boolean;
	};
	type World = {
		id: string;
		name: string;
		description?: string | null;
		authorId?: string | null;
		authorName?: string | null;
		imageUrl?: string | null;
		thumbnailImageUrl?: string | null;
		releaseStatus?: string | null;
		tags: string[];
		platforms: string[];
		capacity?: number | null;
		recommendedCapacity?: number | null;
		createdAt?: string | null;
		updatedAt?: string | null;
		isHome: boolean;
	};
	type PlayerList = { location?: string | null; joinedAt?: string | null; world?: World | null; players: Player[] };

	let data = $state.raw<PlayerList>({ players: [] });
	let loading = $state(false);
	let loadError = $state('');
	let imageFailed = $state(false);
	const now = $derived(clock(1000).now);

	const account = $derived(vrchatAuth.ownerUserId);
	const friendCount = $derived(data.players.filter((player) => player.isFriend).length);

	function iconWeight(player: Player): number {
		let value = 0;
		if (player.isFriend) value += 200;
		if (player.isBlocked) value -= 100;
		if (player.isMuted) value -= 50;
		if (player.isAvatarInteractionDisabled) value -= 20;
		if (player.ageVerified) value += 5;
		return value;
	}

	async function load() {
		if (!account) return;
		loading = true;
		try {
			data = await api.get<PlayerList>(accountPath(account, '/player-list'));
			imageFailed = false;
			loadError = '';
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	$effect(() => {
		if (!account) return;
		untrack(load);
		// Their own presence moving is the game changing instance.
		return watchLive((event) => event.event.case === 'visit' || (event.event.case === 'presence' && event.event.value.userId === account), load, 150);
	});

	const elapsed = (from: string | null | undefined) => (from ? timeToText(Math.max(0, now - Date.parse(from)), true) : '');

	const columns: Column<Player>[] = [
		{ id: 'avatar', get label() { return m.table_playerList_avatar(); }, size: 70, cell: avatarCell },
		{ id: 'timer', get label() { return m.table_playerList_timer(); }, size: 90, sortable: true, compare: (a, b) => Date.parse(b.joinedAt) - Date.parse(a.joinedAt), cell: timerCell },
		{ id: 'displayName', get label() { return m.table_playerList_displayName(); }, size: 200, sortable: true, compare: (a, b) => a.displayName.toLowerCase().localeCompare(b.displayName.toLowerCase()), cell: nameCell },
		{ id: 'rank', get label() { return m.table_playerList_rank(); }, size: 110, sortable: true, compare: (a, b) => trustInfo(a.tags, a.developerType).sortNum - trustInfo(b.tags, b.developerType).sortNum, cell: rankCell },
		{ id: 'status', get label() { return m.table_import_status(); }, size: 200, minSize: 100, stretch: true, cell: statusCell },
		{ id: 'icon', get label() { return m.dialog_group_edit_icon(); }, size: 90, class: 'text-center', sortable: true, descFirst: true, compare: (a, b) => iconWeight(a) - iconWeight(b), cell: iconCell },
		{ id: 'platform', get label() { return m.table_playerList_platform(); }, size: 90, cell: platformCell },
		{ id: 'language', get label() { return m.view_login_language(); }, size: 100, cell: languageCell },
		{ id: 'bioLink', get label() { return m.table_playerList_bioLink(); }, size: 100, cell: bioCell },
		{ id: 'note', get label() { return m.table_import_note(); }, size: 150, minSize: 20, stretch: true, cell: noteCell },
		{ id: 'memo', get label() { return m.dialog_user_info_memo(); }, size: 150, minSize: 20, stretch: true, cell: memoCell }
	];
</script>

{#snippet avatarCell(row: Player)}
	<div class="flex items-center pl-2">
		{#if row.thumbnailImageUrl}
			<img src={imageSrc(row.thumbnailImageUrl)} alt="" class="h-4 w-4 rounded-sm object-cover" loading="lazy" />
		{:else}
			<div class="flex h-4 w-4 items-center justify-center rounded-sm bg-muted"><UserIcon class="h-3 w-3 text-muted-foreground" /></div>
		{/if}
	</div>
{/snippet}

{#snippet timerCell(row: Player)}
	<span>{elapsed(row.joinedAt)}</span>
{/snippet}

{#snippet nameCell(row: Player)}
	<a href={userHref(row.userId)}>{row.displayName || row.userId}</a>
{/snippet}

{#snippet rankCell(row: Player)}
	{@const trust = trustInfo(row.tags, row.developerType)}
	<span class="name" style="color: {trust.color}">{row.trustLevel}</span>
{/snippet}

{#snippet statusCell(row: Player)}
	<span class="flex w-full min-w-0 items-center gap-2">
		<i class={['x-user-status mr-1 shrink-0', statusClass(row.status)]}></i>
		<span class="min-w-0 truncate">{row.statusDescription ?? ''}</span>
	</span>
{/snippet}

{#snippet mark(label: string, text: string, className = '')}
	<Tooltip.Root>
		<Tooltip.Trigger class={className}>{text}</Tooltip.Trigger>
		<Tooltip.Content side="left">{label}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet iconCell(row: Player)}
	<div class="flex items-center justify-center gap-1">
		{#if row.isFriend}{@render mark('Friend', '💚')}{/if}
		{#if row.isBlocked}{@render mark('Blocked', '⛔', 'text-destructive')}{/if}
		{#if row.isMuted}{@render mark('Muted', '🔇', 'text-muted-foreground')}{/if}
		{#if row.isAvatarInteractionDisabled}{@render mark('Avatar Interaction Disabled', '🚫', 'text-muted-foreground')}{/if}
		{#if row.ageVerified}
			<Tooltip.Root>
				<Tooltip.Trigger><IdCard class="x-tag-age-verification h-4 w-4" /></Tooltip.Trigger>
				<Tooltip.Content side="left">18+ Verified</Tooltip.Content>
			</Tooltip.Root>
		{/if}
	</div>
{/snippet}

{#snippet platformCell(row: Player)}
	<div class="flex items-center gap-1">
		{#if row.platform === 'standalonewindows'}<Monitor class="x-tag-platform-pc h-4 w-4 shrink-0" />
		{:else if row.platform === 'android'}<Smartphone class="x-tag-platform-quest h-4 w-4 shrink-0" />
		{:else if row.platform === 'ios'}<Apple class="x-tag-platform-ios h-4 w-4 shrink-0" />
		{:else if row.platform}<span>{row.platform}</span>{/if}
	</div>
{/snippet}

{#snippet languageCell(row: Player)}
	<div class="flex items-center gap-0.5">
		{#each languagesOf(row.tags) as item (item.key)}
			<Tooltip.Root>
				<Tooltip.Trigger><span class={['flags mr-1 inline-block', languageClass(item.key)]}></span></Tooltip.Trigger>
				<Tooltip.Content side="top">{item.value} ({item.key})</Tooltip.Content>
			</Tooltip.Root>
		{/each}
	</div>
{/snippet}

{#snippet bioCell(row: Player)}
	<div class="flex items-center">
		{#each row.bioLinks.filter(Boolean) as link, index (index)}
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<button
							{...props}
							type="button"
							class="cursor-pointer"
							aria-label={link}
							onclick={(event) => {
								event.stopPropagation();
								window.open(link, '_blank', 'noopener,noreferrer');
							}}
						>
							<img src={getFaviconUrl(link)} alt="" class="mr-1 h-4 w-4 align-middle" loading="lazy" />
						</button>
					{/snippet}
				</Tooltip.Trigger>
				<Tooltip.Content>{link}</Tooltip.Content>
			</Tooltip.Root>
		{/each}
	</div>
{/snippet}

{#snippet noteCell(row: Player)}
	<span>{row.note ?? ''}</span>
{/snippet}

{#snippet memoCell(row: Player)}
	<span>{row.memo ?? ''}</span>
{/snippet}

{#snippet infoItem(label: string, value: string)}
	<div class="box-border flex cursor-default items-center p-1.5 text-compact">
		<div class="flex-1 overflow-hidden">
			<span class={PERSON_NAME}>{label}</span>
			<span class={PERSON_SUBTEXT}>{value}</span>
		</div>
	</div>
{/snippet}

<svelte:head>
	<title>Player List · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	{#if !account}
		<Card.Root>
			<Card.Header>
				<Card.Description>Log in to VRChat to see who is in your instance.</Card.Description>
			</Card.Header>
		</Card.Root>
	{:else}
		<div class="flex h-full min-h-0 flex-col overflow-x-hidden overflow-y-auto">
			{#if loadError}<p class="mb-2 text-sm text-destructive">Couldn't load the player list: {loadError}</p>{/if}
			{#if data.world}
				{@const world = data.world}
				<div class="mb-7 flex min-h-30">
					{#if !imageFailed && world.thumbnailImageUrl}
						<button type="button" class="flex-none cursor-pointer" aria-label="World image" onclick={() => imageViewer.show(imageSrc(world.imageUrl))}>
							<img src={imageSrc(world.thumbnailImageUrl)} alt="" class="h-30 w-40 rounded-md" loading="lazy" onerror={() => (imageFailed = true)} />
						</button>
					{:else}
						<div class="flex h-30 w-40 flex-none items-center justify-center rounded-md bg-muted"><ImageIcon class="size-8 text-muted-foreground" /></div>
					{/if}
					<div class="ml-2 flex w-full min-w-80 flex-col">
						<div class="flex items-center">
							<a class="cursor-pointer truncate font-bold" href={worldHref(world.id)}>
								{#if world.isHome}<Home class="inline-block" />{/if}
								{world.name || world.id}
							</a>
						</div>
						<div>
							{#if world.authorId}<a class="x-grey cursor-pointer font-mono" href={userHref(world.authorId)}>{world.authorName || world.authorId}</a>{/if}
						</div>
						<div class="mt-1.5">
							{#if world.tags.includes('system_labs')}<Badge class="mr-1.5" variant="outline">{m.dialog_world_tags_labs()}</Badge>
							{:else if world.releaseStatus === 'public'}<Badge class="mr-1.5" variant="outline">{m.dialog_world_tags_public()}</Badge>
							{:else if world.releaseStatus === 'private'}<Badge class="mr-1.5" variant="outline">{m.view_favorite_private()}</Badge>{/if}
							{#if world.platforms.includes('standalonewindows')}<Badge class="mr-1.5 border-platform-pc! text-platform-pc" variant="outline" title="PC"><Monitor class="h-4 w-4" /></Badge>{/if}
							{#if world.platforms.includes('android')}<Badge class="mr-1.5 border-platform-quest! text-platform-quest" variant="outline" title={m.dialog_group_event_edit_platform_values_android()}><Smartphone class="h-4 w-4" /></Badge>{/if}
							{#if world.platforms.includes('ios')}<Badge class="mr-1.5 border-platform-ios text-platform-ios" variant="outline" title={m.dialog_group_event_edit_platform_values_ios()}><Apple class="h-4 w-4" /></Badge>{/if}
							{#if world.tags.includes('feature_avatar_scaling_disabled')}<Badge class="mt-1.5 mr-1.5" variant="outline">{m.dialog_world_tags_avatar_scaling_disabled()}</Badge>{/if}
						</div>
						<div class="mt-1.5 flex items-center">
							<LocationWorld class="w-fit" location={data.location ?? ""} currentUserId={vrchatAuth.ownerUserId} />
							{#if data.players.length > 0}
								<span class="ml-1.5">
									{data.players.length}{#if friendCount > 0}&nbsp;({friendCount}){/if}
									&nbsp;&horbar; {elapsed(data.joinedAt)}
								</span>
							{/if}
						</div>
						<div class="mt-1.5">
							{#if world.name !== world.description}<span class="inline-block max-w-full align-middle text-xs wrap-break-word">{world.description}</span>{/if}
						</div>
					</div>
					<div class="ml-5 flex flex-col">
						{@render infoItem('Capacity', `${(world.recommendedCapacity ?? 0).toLocaleString()} (${(world.capacity ?? 0).toLocaleString()})`)}
						{@render infoItem('Last Updated', formatDateFilter(world.updatedAt, 'long'))}
						{@render infoItem('Created', formatDateFilter(world.createdAt, 'long'))}
					</div>
				</div>
			{/if}
			<div class="flex min-h-0 min-w-0 flex-1">
				<DataTable
					class="[&_td]:px-2.5! [&_td]:py-0.75! [&_th]:px-2.5! [&_th]:py-0.75! [&_tr]:h-7!"
					rows={data.players}
					{columns}
					{loading}
					paged={false}
					rowKey={(row) => row.userId}
					persistKey="playerList"
					onRowClick={(row) => userDialog.open(row.userId)}
				/>
			</div>
		</div>
	{/if}
</Tooltip.Provider>
