<script lang="ts">
	import ArrowRight from '@lucide/svelte/icons/arrow-right';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { avatarHref, groupHref, imageSrc, userHref } from '#lib/api/links.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { formatDateFilter, statusClass } from '#lib/helpers/format.js';
	import { m } from '#lib/paraglide/messages.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { filterPhotonEvents, PHOTON_EVENT_TYPES, type PhotonEvent } from '#lib/photon/photon.svelte.js';

	// The original's PhotonEventTable: the Photon stream's rows for this instance and the last one,
	// searched by name or text and filtered by type.
	let {
		events,
		previousEvents,
		typeFilter,
		onTypeFilter,
		onShowChatboxBlacklist
	}: {
		events: PhotonEvent[];
		previousEvents: PhotonEvent[];
		typeFilter: string[];
		onTypeFilter: (types: string[]) => void;
		onShowChatboxBlacklist: () => void;
	} = $props();

	let query = $state('');
	let tab = $state('current');
	const current = $derived(filterPhotonEvents(events, query, typeFilter));
	const previous = $derived(filterPhotonEvents(previousEvents, query, typeFilter));

	const statusLabel = (key: string | null | undefined) =>
		key === 'active' ? m.dialog_user_status_active()
		: key === 'join me' ? m.dialog_user_status_join_me()
		: key === 'ask me' ? m.dialog_user_status_ask_me()
		: key === 'busy' ? m.dialog_user_status_busy()
		: m.dialog_user_status_offline();

	const columns: Column<PhotonEvent>[] = [
		{ id: 'created_at', get label() { return m.table_playerList_date(); }, size: 130, sortable: true, value: (r) => Date.parse(r.createdAt), cell: dateCell },
		{ id: 'user', get label() { return m.table_playerList_user(); }, size: 160, cell: userCell },
		{ id: 'type', get label() { return m.table_playerList_type(); }, size: 140, sortable: true, value: (r) => r.type },
		{ id: 'detail', get label() { return m.table_playerList_detail(); }, stretch: true, cell: detailCell }
	];

	const rowKey = (r: PhotonEvent) => `${r.photonId ?? ''}:${r.createdAt}:${r.type}:${r.text ?? ''}`;
</script>

{#snippet dateCell(r: PhotonEvent)}
	<Tooltip.Root>
		<Tooltip.Trigger><span>{formatDateFilter(r.createdAt, 'short')}</span></Tooltip.Trigger>
		<Tooltip.Content side="right">{formatDateFilter(r.createdAt, 'long')}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet userCell(r: PhotonEvent)}
	{#if r.userId}
		<button type="button" class="cursor-pointer pr-2.5" onclick={(e) => { e.stopPropagation(); userDialog.open(r.userId); }}>{r.displayName}</button>
	{:else}
		<span class="pr-2.5">{r.displayName}</span>
	{/if}
{/snippet}

{#snippet avatarDetail(r: PhotonEvent)}
	{#if r.avatar?.id}<a href={avatarHref(r.avatar.id)}>{r.avatar.name}</a>{:else}<span>{r.avatar?.name}</span>{/if}
	{#if r.avatar?.releaseStatus === 'public'}&nbsp;<span class="avatar-info-public">{m.dialog_avatar_labels_public()}</span>
	{:else if r.avatar?.releaseStatus === 'private'}&nbsp;<span class="avatar-info-own">{m.dialog_avatar_labels_private()}</span>{/if}
{/snippet}

{#snippet detailCell(r: PhotonEvent)}
	{#if r.type === 'ChangeAvatar'}
		{@render avatarDetail(r)}
		{#if tab === 'previous' && r.avatar?.description && r.avatar.name !== r.avatar.description}{' | - '}{r.avatar.description}{/if}
	{:else if r.type === 'ChangeStatus'}
		{#if r.status !== r.previousStatus}
			<Tooltip.Root><Tooltip.Trigger><i class={['x-user-status', statusClass(r.previousStatus ?? '')]}></i></Tooltip.Trigger><Tooltip.Content side="top">{statusLabel(r.previousStatus)}</Tooltip.Content></Tooltip.Root>
			<ArrowRight class="inline h-4 w-4" />
			<Tooltip.Root><Tooltip.Trigger><i class={['x-user-status mr-1.25', statusClass(r.status ?? '')]}></i></Tooltip.Trigger><Tooltip.Content side="top">{statusLabel(r.status)}</Tooltip.Content></Tooltip.Root>
		{/if}
		{#if r.statusDescription !== r.previousStatusDescription}<span>{r.statusDescription}</span>{/if}
	{:else if r.type === 'ChangeGroup'}
		{#if r.previousGroupId}<a class="mr-1.25" href={groupHref(r.previousGroupId)}>{r.previousGroupName || r.previousGroupId}</a>{/if}
		<ArrowRight class="inline h-4 w-4" />
		{#if r.groupId}<a class="ml-1.25" href={groupHref(r.groupId)}>{r.groupName || r.groupId}</a>{/if}
	{:else if r.type === 'PortalSpawn'}
		<Location location={r.location} hint={r.worldName ?? ''} grouphint={r.groupName ?? ''} />
	{:else if r.type === 'OnPlayerJoined'}
		{#if r.platform === 'Desktop'}<span>Desktop&nbsp;</span>{:else if r.platform === 'VR'}<span>VR&nbsp;</span>{:else if r.platform === 'Android' || r.platform === 'iOS'}<span>{r.platform}&nbsp;</span>{/if}
		{@render avatarDetail(r)}
	{:else if r.type === 'SpawnEmoji' && r.imageUrl}
		<Tooltip.Root>
			<Tooltip.Trigger><span>{r.fileId}</span></Tooltip.Trigger>
			<Tooltip.Content side="right">
				<button type="button" class="cursor-pointer" onclick={() => imageViewer.show(imageSrc(r.imageUrl))}>
					<img src={imageSrc(r.imageUrl)} alt="" class="h-125" loading="lazy" />
				</button>
			</Tooltip.Content>
		</Tooltip.Root>
	{:else if r.color === 'yellow'}
		<span class="text-yellow-400">{r.text}</span>
	{:else}
		<span>{r.text}</span>
	{/if}
{/snippet}

<div class="mt-5">
	<div class="mb-2 flex flex-wrap items-center gap-2">
		<MultiSelect
			class="w-55"
			values={typeFilter}
			options={PHOTON_EVENT_TYPES.map((type) => ({ value: type, label: type }))}
			placeholder={m.view_player_list_photon_filter_placeholder()}
			onchange={onTypeFilter}
		/>
		<Input class="w-37.5" bind:value={query} placeholder={m.view_player_list_photon_search_placeholder()} />
		<Button variant="outline" onclick={onShowChatboxBlacklist}>{m.view_player_list_photon_chatbox_blacklist()}</Button>
	</div>
	<Tabs.Root bind:value={tab}>
		<Tabs.List>
			<Tabs.Trigger value="current">{m.view_player_list_photon_current()}</Tabs.Trigger>
			<Tabs.Trigger value="previous">{m.view_player_list_photon_previous()}</Tabs.Trigger>
		</Tabs.List>
		<Tabs.Content value="current">
			<DataTable class="mb-2 max-h-80 min-w-0" rows={current} {columns} {rowKey} persistKey="photonEventTable:current" initialSort={{ id: 'created_at', desc: true }} pageSize={10} onRowClick={(r) => r.userId && userDialog.open(r.userId)} />
		</Tabs.Content>
		<Tabs.Content value="previous">
			<DataTable class="mb-2 max-h-80 min-w-0" rows={previous} {columns} {rowKey} persistKey="photonEventTable:previous" initialSort={{ id: 'created_at', desc: true }} pageSize={10} onRowClick={(r) => r.userId && userDialog.open(r.userId)} />
		</Tabs.Content>
	</Tabs.Root>
</div>
