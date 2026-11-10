<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { clock } from '#lib/helpers/clock.svelte.js';
	import Emoji from '#lib/components/common/emoji.svelte';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { untrack } from 'svelte';
	import Check from '@lucide/svelte/icons/check';
	import ImageIcon from '@lucide/svelte/icons/image';
	import MessageCircle from '@lucide/svelte/icons/message-circle';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import DateCell from '#lib/components/common/date-cell.svelte';
	import InviteResponseDialog from '#lib/components/invite/invite-response-dialog.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError, type PagedResult } from '#lib/api/client.js';
	import { groupHref, imageSrc, userHref } from '#lib/api/links.js';
	import type { Notification } from '#lib/api/types.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { canInvite, desktop } from '#lib/instances/instance-launch.svelte.js';
	import { watchLive } from '#lib/api/live.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { worldNames } from '#lib/people/names.svelte.js';
	import { readSetting, shiftKey, storeSetting } from '#lib/settings/table-settings.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { NOTIFICATION_LABELS, boopView, canDecline, detailsOf, linkHref, openLink, responseIcon } from '#lib/notifications/notification-meta.js';

	// The original's Notifications table over GET /1/accounts/{id}/notifications. Declining marks a
	// row hidden, and deleting takes it off this page. Neither removes the row from the database.

	const FILTER_TYPES = [
		'requestInvite',
		'invite',
		'requestInviteResponse',
		'inviteResponse',
		'friendRequest',
		'ignoredFriendRequest',
		'message',
		'boop',
		'event.announcement',
		'groupChange',
		'group.announcement',
		'group.informative',
		'group.invite',
		'group.joinRequest',
		'group.transfer',
		'group.queueReady',
		'group.event.created',
		'group.event.starting',
		'moderation.warning.group',
		'moderation.report.closed',
		'moderation.contentrestriction',
		'instance.closed',
		'economy.alert',
		'twitchdrop.fulfilled'
	].map((value) => ({ value, label: NOTIFICATION_LABELS[value] }));
	const FILTERS_KEY = 'VRCX_notificationTableFilters';

	let rows = $state.raw<Notification[]>([]);
	let loading = $state(false);
	let refreshing = $state(false);
	let loadError = $state('');
	let types = $state<string[]>(readSetting<string[]>(FILTERS_KEY, []));
	let search = $state('');
	let confirming = $state<Confirming | undefined>();
	let inviteTarget = $state<string | undefined>();
	let requestTarget = $state<string | undefined>();
	let boop = $state<Notification | undefined>();
	let currentLocation = $state<string | null>(null);
	const now = $derived(clock(60000).now);

	const account = $derived(vrchatAuth.ownerUserId);
	const canSendInvite = $derived(!!currentLocation && desktop.gameRunning && canInvite(currentLocation, account, currentLocation));

	const shown = $derived.by(() => {
		const term = search.trim().toLowerCase();
		return rows.filter(
			(row) =>
				(types.length === 0 || types.includes(row.type)) &&
				(!term || [row.senderDisplayName, row.title, row.message, row.type].some((value) => (value ?? '').toLowerCase().includes(term)))
		);
	});

	const isGroupId = (id: string | null | undefined) => !!id?.startsWith('grp_');
	const isExpired = (row: Notification) => row.hidden || (!!row.expiresAt && now >= Date.parse(row.expiresAt));

	function groupLabel(row: Notification): string {
		const details = detailsOf(row);
		return row.senderDisplayName || details.groupName || row.linkText || '';
	}

	function setTypes(value: string[]) {
		types = value;
		storeSetting(FILTERS_KEY, value);
	}

	async function load() {
		if (!account) return;
		loading = true;
		try {
			// Up to Table Max Entries (Settings > Interface), in the server's pages of at most 500.
			const max = appearance.maxTableSize.value;
			const all: Notification[] = [];
			for (let pageNumber = 1; all.length < max; pageNumber++) {
				const page = await api.get<PagedResult<Notification>>(accountPath(account, '/notifications'), { page: pageNumber, pageSize: 500, includeHidden: true });
				all.push(...page.items);
				if (pageNumber >= page.totalPages || page.items.length === 0) break;
			}
			rows = all.slice(0, max);
			loadError = '';
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	async function act(work: () => Promise<unknown>) {
		confirming = undefined;
		try {
			await work();
		} catch (err) {
			loadError = ApiError.from(err).message;
		}
		await load();
	}

	const path = (row: Notification, action = '') => accountPath(account, `/notifications/${encodeURIComponent(row.id)}${action}`);

	async function refresh() {
		refreshing = true;
		await act(() => api.post(accountPath(account, '/notifications/refresh'), {}));
		refreshing = false;
	}

	function accept(row: Notification) {
		confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_accept_friend_request(); }, run: () => act(() => api.post(path(row, '/accept'), {})) };
	}

	function acceptRequestInvite(row: Notification) {
		confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_send_invite(); },
			run: () =>
				act(async () => {
					const location = currentLocation;
					if (!location || !row.senderUserId) return;
					await api.post(accountPath(account, `/invites/${encodeURIComponent(row.senderUserId)}`), { location, worldName: worldNames.get(parseLocation(location).worldId) ?? '' });
					await api.post(path(row, '/hide'), {});
				})
		};
	}

	const hide = (row: Notification) => act(() => api.post(path(row, '/hide'), {}));
	const removeLog = (row: Notification) => {
		rows = rows.filter((r) => r.id !== row.id);
		return act(() => api.delete(path(row)));
	};

	function respond(row: Notification, response: NonNullable<Notification['responses']>[number]) {
		if (response.type === 'link') {
			const href = linkHref(response.data);
			if (href) window.location.href = href;
			else openLink(response.data);
			return;
		}
		if (response.icon === 'reply' && row.type === 'boop') {
			boop = row;
			return;
		}
		act(() => api.post(path(row, '/respond'), { responseType: response.type, responseData: response.data }));
	}

	$effect(() => {
		if (!account) return;
		untrack(() => {
			load().then(() => api.post(accountPath(account, '/notifications/see-all'), {}).catch(() => {}));
		});
		api.get<{ currentLocation?: string | null }>(accountPath(account, '/instances/options')).then(
			(data) => (currentLocation = data.currentLocation ?? null),
			() => (currentLocation = null)
		);
		return watchLive(['notification'], load, 500);
	});

	const columns: Column<Notification>[] = [
		{ id: 'spacer', size: 20, minSize: 0, maxSize: 20 },
		{ id: 'created_at', get label() { return m.table_notification_date(); }, size: 120, sortable: true, value: (row) => row.createdAt, cell: dateCell },
		{ id: 'type', get label() { return m.table_notification_type(); }, size: 180, cell: typeCell },
		{ id: 'senderUsername', get label() { return m.table_notification_user(); }, size: 150, class: 'overflow-hidden', cell: userCell },
		{ id: 'groupName', get label() { return m.table_notification_group(); }, size: 150, class: 'overflow-hidden', cell: groupCell },
		{ id: 'photo', get label() { return m.table_notification_photo(); }, size: 80, cell: photoCell },
		{ id: 'message', get label() { return m.table_notification_message(); }, minSize: 100, stretch: true, class: 'min-w-0 overflow-hidden', cell: messageCell },
		{ id: 'action', get label() { return m.table_notification_action(); }, size: 120, minSize: 120, maxSize: 120, class: 'text-right', cell: actionCell },
		{ id: 'trailing', size: 5, enableResizing: false }
	];
</script>

{#snippet dateCell(row: Notification)}
	<DateCell value={row.createdAt} />
{/snippet}

{#snippet tip(text: string)}
	<Tooltip.Root delayDuration={500}>
		<Tooltip.Trigger class="block w-full min-w-0 truncate text-left">{text}</Tooltip.Trigger>
		<Tooltip.Content>{text}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet typeCell(row: Notification)}
	{@const label = NOTIFICATION_LABELS[row.type] ?? row.type}
	{@const details = detailsOf(row)}
	<Badge variant="outline" class="text-muted-foreground">
		{#if row.type === 'group.queueReady' || row.type === 'instance.closed'}
			<!-- The original opens the world and shows the instance on hover for these two. -->
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<button {...props} type="button" class="cursor-pointer" onclick={() => details.location && userDialog.openWorld(details.location)}>{label}</button>
					{/snippet}
				</Tooltip.Trigger>
				{#if details.location}
					<Tooltip.Content side="top"><Location location={details.location} hint={details.worldName ?? ''} grouphint={details.groupName ?? ''} /></Tooltip.Content>
				{/if}
			</Tooltip.Root>
		{:else if row.link && row.type !== 'invite'}
			<Tooltip.Root>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<svelte:element this={linkHref(row.link) ? 'a' : 'button'} {...props} class="cursor-pointer" href={linkHref(row.link)} onclick={() => openLink(row.link)} role={linkHref(row.link) ? undefined : 'button'}>{label}</svelte:element>
					{/snippet}
				</Tooltip.Trigger>
				<Tooltip.Content side="top">{row.linkText}</Tooltip.Content>
			</Tooltip.Root>
		{:else}
			{label}
		{/if}
	</Badge>
{/snippet}

{#snippet userCell(row: Notification)}
	{#if row.senderUserId && !isGroupId(row.senderUserId)}
		<a class="block w-full min-w-0 cursor-pointer truncate" href={userHref(row.senderUserId)}>{row.senderDisplayName}</a>
	{:else if row.link?.startsWith('user:')}
		<a class="block w-full min-w-0 cursor-pointer truncate" href={linkHref(row.link)}>{row.linkText || row.senderDisplayName}</a>
	{:else if row.senderDisplayName && !isGroupId(row.senderUserId)}
		<span class="block w-full min-w-0 truncate">{row.senderDisplayName}</span>
	{/if}
{/snippet}

{#snippet groupCell(row: Notification)}
	{@const label = groupLabel(row)}
	{#if row.senderUserId && (row.type === 'groupChange' || isGroupId(row.senderUserId))}
		<a class="block w-full min-w-0 cursor-pointer truncate" href={groupHref(row.senderUserId)}>{label}</a>
	{:else if row.type === 'groupChange' && row.senderDisplayName}
		<span class="block w-full min-w-0 truncate">{row.senderDisplayName}</span>
	{:else if row.link?.startsWith('group:') || row.link?.startsWith('event:')}
		<a class="block w-full min-w-0 cursor-pointer truncate" href={linkHref(row.link)}>{detailsOf(row).groupName || label}</a>
	{:else if detailsOf(row).groupName}
		<span class="block w-full min-w-0 truncate">{detailsOf(row).groupName}</span>
	{/if}
{/snippet}

{#snippet photoCell(row: Notification)}
	{@const url = detailsOf(row).imageUrl || row.imageUrl || boopView(row)?.imageUrl}
	{#if url && row.type === 'boop' && !url.startsWith('default_')}
		<!-- A boop's emoji plays when animated, as the original's Emoji component. -->
		<button type="button" class="block size-7.5 cursor-pointer" aria-label={m.dialog_gallery_icons_emoji()} onclick={() => imageViewer.show(url)}><Emoji {url} /></button>
	{:else if url && row.type !== 'boop'}
		<button type="button" class="cursor-pointer" aria-label={m.table_notification_photo()} onclick={() => imageViewer.show(url)}>
			<Avatar.Root class="size-7.5 rounded">
				<Avatar.Image src={imageSrc(url)} class="object-cover" />
				<Avatar.Fallback class="rounded"><ImageIcon class="size-4 text-muted-foreground" /></Avatar.Fallback>
			</Avatar.Root>
		</button>
	{/if}
{/snippet}

{#snippet messageCell(row: Notification)}
	{@const details = detailsOf(row)}
	<div class="w-full min-w-0">
		{#if row.type === 'invite' && details.worldId}
			<div class="w-full min-w-0"><Location location={details.worldId} hint={details.worldName ?? ''} grouphint={details.groupName ?? ''} /></div>
		{/if}
		{#if boopView(row)}
			{@render tip(boopView(row)!.text)}
		{:else if row.message && row.title}
			{@render tip(`${row.title}, ${row.message}`)}
		{:else if !row.message && row.title}
			{@render tip(row.title)}
		{:else if row.message && row.message !== `This is a generated invite to ${details.worldName}`}
			{@render tip(row.message)}
		{/if}
		{#if !row.message && details.inviteMessage}{@render tip(details.inviteMessage)}{/if}
		{#if !row.message && details.requestMessage}{@render tip(details.requestMessage)}{/if}
		{#if !row.message && details.responseMessage}{@render tip(details.responseMessage)}{/if}
	</div>
{/snippet}

{#snippet iconButton(label: string, run: () => void, icon: typeof Check, danger = false)}
	{@const Icon = icon}
	<Tooltip.Root>
		<Tooltip.Trigger>
			{#snippet child({ props })}
				<button {...props} type="button" aria-label={label} class="ml-1 inline-flex h-6 cursor-pointer items-center justify-center text-muted-foreground hover:text-foreground" onclick={run}>
					<Icon class={['h-4 w-4', danger && 'text-red-600']} />
				</button>
			{/snippet}
		</Tooltip.Trigger>
		<Tooltip.Content side="top">{label}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet actionCell(row: Notification)}
	{@const showDecline = canDecline(row)}
	<div class="flex items-center justify-end gap-2">
		{#if row.senderUserId !== account && !isExpired(row)}
			<span class="inline-flex items-center gap-2">
				{#if row.type === 'friendRequest'}{@render iconButton('Accept', () => accept(row), Check)}{/if}
				{#if row.type === 'invite'}{@render iconButton('Decline with message', () => (inviteTarget = row.id), MessageCircle)}{/if}
				{#if row.type === 'requestInvite'}
					<span class="inline-flex items-center">
						{#if canSendInvite}{@render iconButton('Invite', () => acceptRequestInvite(row), Check)}{/if}
						{@render iconButton('Decline with message', () => (requestTarget = row.id), MessageCircle)}
					</span>
				{/if}
				{#each row.responses ?? [] as response (`${response.text}:${response.type}`)}
					{@render iconButton(
						response.text,
						() => respond(row, response),
						responseIcon(row, response)
					)}
				{/each}
				{#if showDecline}
					{@render iconButton('Decline', () => (shiftKey.held ? hide(row) : (confirming = { get title() { return m.confirm_title(); }, description: `Continue? Decline ${row.type}`, run: () => hide(row) })), X, shiftKey.held)}
				{/if}
				{#if row.type === 'group.queueReady'}
					{@render iconButton('Delete log', () => (shiftKey.held ? removeLog(row) : (confirming = { get title() { return m.confirm_title(); }, description: `Continue? ${row.type}`, run: () => removeLog(row) })), shiftKey.held ? X : Trash2, shiftKey.held)}
				{/if}
			</span>
		{/if}
		{#if row.type !== 'friendRequest' && row.type !== 'ignoredFriendRequest'}
			{@render iconButton('Delete log', () => (shiftKey.held ? removeLog(row) : (confirming = { get title() { return m.confirm_title(); }, description: `Continue? ${row.type}`, run: () => removeLog(row) })), shiftKey.held ? X : Trash2, shiftKey.held)}
		{/if}
	</div>
{/snippet}

<svelte:head>
	<title>Notifications · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	<div class="flex h-full min-h-0 w-full flex-col">
		{#if !account}
			<Card.Root>
				<Card.Header>
					<Card.Description>Log in to VRChat to see your notifications.</Card.Description>
				</Card.Header>
			</Card.Root>
		{:else}
			<DataTable rows={shown} {columns} {loading} rowKey={(row) => row.id} persistKey="notifications" filtered={rows.length > 0} initialSort={{ id: 'created_at', desc: true }}>
				{#snippet toolbar()}
					<div class="mb-2 flex items-center justify-between gap-2">
						<MultiSelect class="max-w-1/2 min-w-50" values={types} options={FILTER_TYPES} placeholder={m.view_my_avatars_filter()} onchange={setTypes} />
						<div class="flex flex-row justify-end">
							<Input class="mx-2 my-0 h-8 min-w-50 flex-[0.4]" placeholder={m.view_favorite_worlds_search()} bind:value={search} />
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<Button {...props} class="flex-none rounded-full" variant="ghost" size="icon-sm" disabled={refreshing} aria-label={m.dialog_note_export_refresh()} onclick={refresh}>
											{#if refreshing}<Spinner />{:else}<RefreshCw />{/if}
										</Button>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="bottom">{m.dialog_note_export_refresh()}</Tooltip.Content>
							</Tooltip.Root>
						</div>
					</div>
					{#if loadError}<p class="mb-2 text-sm text-destructive">{loadError}</p>{/if}
				{/snippet}
			</DataTable>
		{/if}
	</div>
</Tooltip.Provider>

<ActionDialogs bind:confirming />
<InviteResponseDialog bind:target={inviteTarget} type="response" onsent={load} />
<InviteResponseDialog bind:target={requestTarget} type="requestResponse" onsent={load} />
<BoopDialog
	bind:open={() => !!boop, (open) => { if (!open) boop = undefined; }}
	name={boop?.senderDisplayName ?? ''}
	onsend={(emojiId) => {
		const sender = boop?.senderUserId;
		boop = undefined;
		if (sender) act(() => api.post(accountPath(account, `/users/${encodeURIComponent(sender)}/boop`), { emojiId: emojiId || null }));
	}}
/>
