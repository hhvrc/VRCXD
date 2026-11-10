<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { goto } from '$app/navigation';
	import Bell from '@lucide/svelte/icons/bell';
	import CalendarDays from '@lucide/svelte/icons/calendar-days';
	import Check from '@lucide/svelte/icons/check';
	import Mail from '@lucide/svelte/icons/mail';
	import MessageCircle from '@lucide/svelte/icons/message-circle';
	import Send from '@lucide/svelte/icons/send';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import UserPlus from '@lucide/svelte/icons/user-plus';
	import Users from '@lucide/svelte/icons/users';
	import X from '@lucide/svelte/icons/x';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import InviteResponseDialog from '#lib/components/invite/invite-response-dialog.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as HoverCard from '#lib/components/ui/hover-card/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import * as Sheet from '#lib/components/ui/sheet/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { groupHref, iconSrc } from '#lib/api/links.js';
	import type { Notification } from '#lib/api/types.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { canInvite, desktop } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { notificationCenter } from '#lib/notifications/notification-center.svelte.js';
	import { canDecline, detailsOf, fromNow, isNotificationExpired, linkHref, notificationLabel, openLink, responseIcon } from '#lib/notifications/notification-meta.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { shiftKey } from '#lib/settings/table-settings.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's NotificationCenterSheet, NotificationList and NotificationItem.
	type Category = 'friend' | 'group' | 'other';
	const TYPE_ICONS: Record<string, typeof Bell> = {
		friendRequest: UserPlus,
		ignoredFriendRequest: UserPlus,
		invite: Send,
		requestInvite: Send,
		inviteResponse: Send,
		requestInviteResponse: Send,
		boop: MessageCircle,
		message: Mail
	};
	const FRIEND_TYPES = ['invite', 'requestInvite', 'inviteResponse', 'requestInviteResponse', 'friendRequest', 'ignoredFriendRequest', 'boop'];

	let tab = $state<Category>('friend');
	let confirming = $state<Confirming | undefined>();
	let inviteTarget = $state<string | undefined>();
	let requestTarget = $state<string | undefined>();
	let boopFor = $state<Notification | undefined>();
	let boopOpen = $state(false);
	let error = $state('');

	const account = $derived(vrchatAuth.ownerUserId);
	const center = notificationCenter;
	const location = $derived(sidebarData.currentLocation);
	const canSendInvite = $derived(!!location && desktop.gameRunning && canInvite(location, account, location));
	const lists = $derived({
		friend: { unseen: center.unseenIn('friend'), recent: center.recentIn('friend') },
		group: { unseen: center.unseenIn('group'), recent: center.recentIn('group') },
		other: { unseen: center.unseenIn('other'), recent: center.recentIn('other') }
	});

	// Opening picks the first tab with something new; whatever was shown is marked seen on leaving it.
	let wasOpen = false;
	$effect(() => {
		const open = center.open;
		if (open && !wasOpen) {
			center.load().then(() => {
				tab = center.unseenIn('friend').length ? 'friend' : center.unseenIn('group').length ? 'group' : center.unseenIn('other').length ? 'other' : 'friend';
			});
		} else if (!open && wasOpen) center.flushSeen();
		wasOpen = open;
	});
	$effect(() => {
		if (center.open) center.shown(lists[tab].unseen);
	});

	const isGroupNotice = (n: Notification) => n.type.startsWith('group.') || n.type === 'groupChange';
	const senderName = (n: Notification) => n.title || n.senderDisplayName || detailsOf(n).groupName || '';
	const message = (n: Notification) => {
		const d = detailsOf(n);
		return n.message || d.inviteMessage || d.requestMessage || d.responseMessage || d.worldName || '';
	};

	function imageOf(n: Notification): string | null {
		const d = detailsOf(n);
		if (n.senderUserId?.startsWith('grp_') || isGroupNotice(n)) return d.imageUrl || n.imageUrl || null;
		const friend = sidebarData.friends.find((f) => f.userId === n.senderUserId);
		return friend?.thumbnailImageUrl ?? null;
	}

	function openSender(n: Notification) {
		const sender = n.senderUserId ?? '';
		if (sender.startsWith('grp_') || isGroupNotice(n)) {
			const groupId = sender.startsWith('grp_') ? sender : detailsOf(n).groupId;
			if (groupId) {
				center.open = false;
				goto(groupHref(groupId));
				return;
			}
		}
		if (sender) {
			userDialog.open(sender);
			return;
		}
		const href = linkHref(n.link);
		if (href) {
			center.open = false;
			goto(href);
		} else openLink(n.link);
	}

	async function act(work: () => Promise<unknown>) {
		confirming = undefined;
		try {
			await work();
			error = '';
		} catch (err) {
			error = ApiError.from(err).message;
		}
		await center.load();
	}

	const path = (n: Notification, action = '') => accountPath(account, `/notifications/${encodeURIComponent(n.id)}${action}`);
	const accept = (n: Notification) => (confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_accept_friend_request(); }, run: () => act(() => api.post(path(n, '/accept'), {})) });
	const decline = (n: Notification) => (confirming = { get title() { return m.confirm_title(); }, description: `Continue? Decline ${n.type}`, run: () => act(() => api.post(path(n, '/hide'), {})) });
	const removeLog = (n: Notification) => (confirming = { get title() { return m.confirm_title(); }, description: `Continue? Delete ${n.type}`, run: () => act(() => api.delete(path(n))) });

	function sendInvite(n: Notification) {
		confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_send_invite(); },
			run: () =>
				act(async () => {
					if (!location || !n.senderUserId) return;
					await api.post(accountPath(account, `/invites/${encodeURIComponent(n.senderUserId)}`), { location, worldName: parseLocation(location).worldId });
					await api.post(path(n, '/hide'), {});
				})
		};
	}

	function respond(n: Notification, response: NonNullable<Notification['responses']>[number]) {
		if (response.type === 'link') {
			const href = linkHref(response.data);
			if (href) {
				center.open = false;
				goto(href);
			} else openLink(response.data);
			return;
		}
		if (response.icon === 'reply' && n.type === 'boop') {
			boopFor = n;
			boopOpen = true;
			return;
		}
		act(() => api.post(path(n, '/respond'), { responseType: response.type, responseData: response.data }));
	}

	function viewMore() {
		center.open = false;
		goto('/notification');
	}
</script>

{#snippet action(label: string, run: () => void, Icon: typeof Bell, danger = false)}
	<Tooltip.Root>
		<Tooltip.Trigger>
			{#snippet child({ props })}
				<button
					{...props}
					type="button"
					aria-label={label}
					class={['inline-flex size-5 cursor-pointer items-center justify-center rounded text-muted-foreground hover:bg-muted', danger ? 'hover:text-destructive' : 'hover:text-foreground']}
					onclick={(e) => {
						e.stopPropagation();
						run();
					}}><Icon class="size-3" /></button
				>
			{/snippet}
		</Tooltip.Trigger>
		<Tooltip.Content side="top">{label}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet avatar(n: Notification, size: string, rounded = 'rounded-full')}
	{@const Icon = TYPE_ICONS[n.type] ?? (isGroupNotice(n) ? Users : Bell)}
	<Avatar.Root class={[size, rounded]}>
		{#if imageOf(n)}<Avatar.Image src={iconSrc(imageOf(n))} class="object-cover" />{/if}
		<Avatar.Fallback class={['text-muted-foreground', rounded]}><Icon class="size-4" /></Avatar.Fallback>
	</Avatar.Root>
{/snippet}

{#snippet item(n: Notification)}
	{@const d = detailsOf(n)}
	{@const expired = isNotificationExpired(n, center.now)}
	{@const created = n.createdAt}
	<HoverCard.Root openDelay={400} closeDelay={100}>
		<HoverCard.Trigger>
			{#snippet child({ props })}
				<div {...props} class="mb-1.5 flex items-center gap-2.5 rounded-md bg-muted/50 px-3 py-2">
					<button type="button" class="size-8 shrink-0 cursor-pointer" onclick={() => openSender(n)}>{@render avatar(n, 'size-8')}</button>
					<div class="min-w-0 flex-1">
						<div class="flex w-full min-w-0 items-center gap-2 text-sm font-medium">
							<button type="button" class="cursor-pointer truncate" onclick={() => openSender(n)}>{senderName(n)}</button>
							<Badge variant="secondary" class="shrink-0 text-3xs text-muted-foreground">{notificationLabel(n.type)}</Badge>
							{#if !expired && !n.seen}<span class="ml-auto size-2 shrink-0 rounded-full bg-blue-500"></span>{/if}
						</div>
						{#if n.type === 'invite' && d.worldId}
							<div class="text-xs"><Location location={d.worldId} hint={d.worldName ?? ''} grouphint={d.groupName ?? ''} /></div>
						{:else if (n.type === 'group.queueReady' || n.type === 'instance.closed') && d.location}
							<div class="text-xs"><Location location={d.location} hint={d.worldName ?? ''} grouphint={d.groupName ?? ''} /></div>
						{/if}
						{#if message(n)}<div class="truncate text-xs text-muted-foreground select-none">{message(n)}</div>{/if}
					</div>
					<div class="flex h-full shrink-0 flex-col items-end justify-between gap-1">
						{#if created}<span class="text-3xs whitespace-nowrap text-muted-foreground" title={formatDateFilter(created, 'long')}>{fromNow(created, center.now)}</span>{/if}
						<div class="flex items-center gap-1">
							{#if !expired}
								{#if n.type === 'friendRequest'}{@render action('Accept', () => accept(n), Check)}{/if}
								{#if n.type === 'invite'}{@render action('Decline with message', () => (inviteTarget = n.id), MessageCircle)}{/if}
								{#if n.type === 'requestInvite'}
									{#if canSendInvite}{@render action('Invite', () => sendInvite(n), Check)}{/if}
									{@render action('Decline with message', () => (requestTarget = n.id), MessageCircle)}
								{/if}
								{#each n.responses ?? [] as response (`${response.text}:${response.type}`)}
									{@render action(response.text, () => respond(n, response), responseIcon(n, response))}
								{/each}
								{#if canDecline(n)}{@render action('Decline', () => (shiftKey.held ? act(() => api.post(path(n, '/hide'), {})) : decline(n)), X, true)}{/if}
							{/if}
							{#if n.type !== 'friendRequest' && n.type !== 'ignoredFriendRequest'}{@render action('Delete log', () => removeLog(n), Trash2, true)}{/if}
						</div>
					</div>
				</div>
			{/snippet}
		</HoverCard.Trigger>
		<HoverCard.Content side="left" sideOffset={8} class="w-100 p-3">
			<div class="mb-2 flex items-center gap-2">
				{@render avatar(n, 'size-8 shrink-0', isGroupNotice(n) ? 'rounded' : 'rounded-full')}
				<div class="min-w-0">
					<p class="truncate text-sm font-medium">{senderName(n) || n.type}</p>
					<p class="text-xs text-muted-foreground">{notificationLabel(n.type)}</p>
				</div>
			</div>
			{#if isGroupNotice(n)}
				{#if n.title}<p class="mb-1 text-sm font-medium">{n.title}</p>{/if}
				{#if n.message}<p class="text-xs leading-relaxed wrap-break-word whitespace-pre-line text-muted-foreground">{n.message}</p>{/if}
			{:else if FRIEND_TYPES.includes(n.type)}
				{#if d.worldId}<div class="mb-1 text-xs"><Location location={d.worldId} hint={d.worldName ?? ''} /></div>{/if}
				{#if message(n)}<p class="text-xs leading-relaxed wrap-break-word text-muted-foreground">{n.message || d.inviteMessage || d.requestMessage || d.responseMessage}</p>{/if}
			{:else}
				{#if n.title}<p class="mb-1 text-sm font-medium">{n.title}</p>{/if}
				{#if message(n)}<p class="text-xs leading-relaxed wrap-break-word whitespace-pre-line text-muted-foreground">{message(n)}</p>{/if}
			{/if}
			{#if created}
				<Separator class="my-2" />
				<div class="flex items-center gap-2 text-3xs text-muted-foreground"><CalendarDays class="size-3" />{formatDateFilter(created, 'long')}</div>
			{/if}
		</HoverCard.Content>
	</HoverCard.Root>
{/snippet}

<Sheet.Root bind:open={center.open}>
	<Sheet.Content side="right" class="flex flex-col px-1 p-0 sm:max-w-md" onOpenAutoFocus={(e) => e.preventDefault()}>
		<Sheet.Header class="border-b px-4 pt-4 pb-3">
			<div class="flex items-center pr-6"><Sheet.Title>{m.side_panel_notification_center_title()}</Sheet.Title></div>
		</Sheet.Header>
		<Tooltip.Provider>
			<Tabs.Root bind:value={() => tab, (v) => (tab = v as Category)} class="flex min-h-0 flex-1 flex-col">
				<Tabs.List class="mt-2 mr-4 ml-2 grid w-auto grid-cols-3">
					{#each [['friend', 'Friend'], ['group', 'Group'], ['other', 'Other']] as [value, label] (value)}
						<Tabs.Trigger {value}>
							{label}
							{#if lists[value as Category].unseen.length}<span class="ml-1 text-xs text-muted-foreground">({lists[value as Category].unseen.length})</span>{/if}
						</Tabs.Trigger>
					{/each}
				</Tabs.List>
				{#each ['friend', 'group', 'other'] as value (value)}
					{@const list = lists[value as Category]}
					<Tabs.Content {value} class="mt-0 min-h-0 flex-1 overflow-hidden">
						<div class="flex h-full flex-col overflow-hidden">
							<div class="flex-1 overflow-y-auto">
								{#if list.unseen.length || list.recent.length}
									<div class="w-full px-2 py-2">
										{#each list.unseen as n (n.id)}{@render item(n)}{/each}
										{#if list.recent.length}
											<div class="flex items-center gap-2 px-2.5 py-2">
												<Separator class="flex-1" />
												<span class="shrink-0 text-3xs tracking-wider text-muted-foreground uppercase">{m.side_panel_notification_center_past_notifications()}</span>
												<Separator class="flex-1" />
											</div>
											{#each list.recent as n (n.id)}{@render item(n)}{/each}
										{/if}
									</div>
								{:else}
									<div class="flex items-center justify-center p-8 text-sm text-muted-foreground">{m.side_panel_notification_center_no_new_notifications()}</div>
								{/if}
								{#if error}<p class="px-4 text-xs text-destructive">{error}</p>{/if}
								<div class="flex justify-center py-3"><Button variant="secondary" size="sm" class="text-xs" onclick={viewMore}>{m.side_panel_notification_center_view_more()}</Button></div>
							</div>
						</div>
					</Tabs.Content>
				{/each}
			</Tabs.Root>
		</Tooltip.Provider>
	</Sheet.Content>
</Sheet.Root>

<ActionDialogs bind:confirming />
<InviteResponseDialog bind:target={inviteTarget} type="response" onsent={() => center.load()} />
<InviteResponseDialog bind:target={requestTarget} type="requestResponse" onsent={() => center.load()} />
{#if boopFor}
	{@const target = boopFor}
	<BoopDialog bind:open={boopOpen} name={target.senderDisplayName ?? ''} onsend={(emoji) => target.senderUserId && act(() => api.post(accountPath(account, `/users/${encodeURIComponent(target.senderUserId!)}/boop`), { emojiId: emoji || null }))} />
{/if}
