<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import Clock from '@lucide/svelte/icons/clock';
	import ExternalLink from '@lucide/svelte/icons/external-link';
	import Hand from '@lucide/svelte/icons/hand';
	import LogIn from '@lucide/svelte/icons/log-in';
	import Mail from '@lucide/svelte/icons/mail';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import type { Snippet } from 'svelte';
	import { toast } from 'svelte-sonner';
	import BoopDialog from '#lib/components/invite/boop-dialog.svelte';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import type { Instance } from '#lib/api/types.js';
	import { canInvite, canInviteSelf, desktop, instanceDialogs, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { isRealInstance } from '#lib/instances/location.js';
	import { recentActions } from '#lib/api/recent-actions.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's UserContextMenu.vue: View Details, Request Invite (online), Invite to your
	// instance (game running), Send Boop, and Launch/Invite and Invite Yourself when the user is online
	// in an instance; the clock marks a recent invite or request. Callers can append their own items.

	let {
		userId,
		name = '',
		state: userState = '',
		location = '',
		children,
		append
	}: { userId: string; name?: string; state?: string; location?: string; children: Snippet; append?: Snippet<[typeof ContextMenu]> } = $props();

	const account = $derived(vrchatAuth.ownerUserId);
	const isOnline = $derived(userState === 'online');
	const hasLocation = $derived(isRealInstance(location));
	const myLocation = $derived(sidebarData.currentLocation ?? '');
	const canInviteHere = $derived(!!myLocation && canInvite(myLocation, account, myLocation));
	const canJoin = $derived(hasLocation && canInviteSelf(location, account));
	let boopOpen = $state(false);

	async function run(work: () => Promise<unknown>, done: string) {
		try {
			await work();
			toast.success(done);
		} catch (err) {
			toastError(err);
		}
	}

	const requestInvite = () => run(() => api.post(accountPath(account, `/invite-requests/${encodeURIComponent(userId)}`), {}), 'Request invite sent');

	const invite = () =>
		run(async () => {
			const world = await api.get<Instance>(`/1/instances/${encodeURIComponent(myLocation)}`).catch(() => undefined);
			await api.post(accountPath(account, `/invites/${encodeURIComponent(userId)}`), { location: myLocation, worldName: world?.worldName ?? '' });
		}, 'Invite sent');
</script>

<ContextMenu.Root>
	<ContextMenu.Trigger>
		{#snippet child({ props })}
			<span {...props}>{@render children()}</span>
		{/snippet}
	</ContextMenu.Trigger>
	<ContextMenu.Content>
		<ContextMenu.Item onSelect={() => userDialog.open(userId)}>
			<ExternalLink class="size-4" />
			{m.view_charts_mutual_friend_context_menu_view_details()}
		</ContextMenu.Item>
		<ContextMenu.Separator />
		{#if isOnline}
			<ContextMenu.Item onSelect={requestInvite}>
				<Mail class="size-4" />
				Request Invite
				{#if recentActions.isRecent(userId, 'Request Invite')}<ContextMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></ContextMenu.Shortcut>{/if}
			</ContextMenu.Item>
		{/if}
		{#if desktop.gameRunning}
			<ContextMenu.Item disabled={!canInviteHere} onSelect={invite}>
				<MessageSquare class="size-4" />
				Invite
				{#if recentActions.isRecent(userId, 'Invite')}<ContextMenu.Shortcut><Clock class="size-3.5 text-muted-foreground" /></ContextMenu.Shortcut>{/if}
			</ContextMenu.Item>
		{/if}
		<ContextMenu.Item disabled={!sidebarData.me?.isBoopingEnabled} onSelect={() => (boopOpen = true)}>
			<Hand class="size-4" />
			{m.dialog_user_actions_send_boop()}
		</ContextMenu.Item>
		{#if isOnline && hasLocation}
			<ContextMenu.Separator />
			<ContextMenu.Item disabled={!canJoin} onSelect={() => (instanceDialogs.launch = { location })}>
				<LogIn class="size-4" />
				{m.dialog_user_info_launch_invite_tooltip()}
			</ContextMenu.Item>
			<ContextMenu.Item disabled={!canJoin} onSelect={() => run(() => selfInvite(location), 'Self invite sent')}>
				<Mail class="size-4" />
				{m.dialog_user_info_self_invite_tooltip()}
			</ContextMenu.Item>
		{/if}
		{@render append?.(ContextMenu)}
	</ContextMenu.Content>
</ContextMenu.Root>

<BoopDialog bind:open={boopOpen} name={name || userId} onsend={(emoji) => run(() => api.post(accountPath(account, `/users/${encodeURIComponent(userId)}/boop`), { emojiId: emoji || null }), 'Boop sent')} />
