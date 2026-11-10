<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import MemberTable from '#lib/components/group-moderation/member-table.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER } from '#lib/details.js';
	import { groupModeration as M } from '#lib/groups/group-moderation.svelte.js';

	// The original's GroupModerationInvitesTab: sent invites, join requests and blocked requests.
	const actionDisabled = $derived(!!M.progressCurrent || !M.can('group-invites-manage'));
</script>

<div class="flex h-full min-h-0 flex-col">
	<div class="flex items-center">
		<Button class="rounded-full" variant="outline" size="icon-sm" aria-label={m.dialog_note_export_refresh()} disabled={M.loading} onclick={() => M.getAllGroupInvitesAndJoinRequests()}>
			{#if M.loading}<Spinner />{:else}<RefreshCw />{/if}
		</Button>
	</div>
	<TabsPrimitive.Root bind:value={M.activeInviteTab} class="flex min-h-0 flex-1 flex-col">
		<div class="{TAB_LIST_WRAP} my-2">
			<TabsPrimitive.List class={TAB_LIST}>
				<TabsPrimitive.Trigger value="sent" class={TAB_TRIGGER}>
					<span class="text-base font-bold">{m.dialog_group_member_moderation_sent_invites()}</span><span class="ml-1.5 text-xs text-muted-foreground">{M.invites.length}</span>
				</TabsPrimitive.Trigger>
				<TabsPrimitive.Trigger value="join" class={TAB_TRIGGER}>
					<span class="text-base font-bold">{m.dialog_group_member_moderation_join_requests()}</span><span class="ml-1.5 text-xs text-muted-foreground">{M.joinRequests.length}</span>
				</TabsPrimitive.Trigger>
				<TabsPrimitive.Trigger value="blocked" class={TAB_TRIGGER}>
					<span class="text-base font-bold">{m.dialog_group_member_moderation_blocked_requests()}</span><span class="ml-1.5 text-xs text-muted-foreground">{M.blocked.length}</span>
				</TabsPrimitive.Trigger>
			</TabsPrimitive.List>
		</div>
		<TabsPrimitive.Content value="sent" class="flex min-h-0 flex-1 flex-col gap-2">
			<div class="flex items-center justify-between gap-2">
				<Button size="sm" variant="outline" onclick={() => M.selectAll(M.invites)}>{m.view_favorite_select_all()}</Button>
				<Button size="sm" variant="outline" disabled={actionDisabled} onclick={() => M.deleteSentInvites()}>{m.dialog_group_member_moderation_delete_sent_invite()}</Button>
			</div>
			<MemberTable rows={M.invites} kind="invites" persistKey="group-moderation:invites" loading={M.loading} />
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="join" class="flex min-h-0 flex-1 flex-col gap-2">
			<div class="flex items-center justify-between gap-2">
				<Button size="sm" variant="outline" onclick={() => M.selectAll(M.joinRequests)}>{m.view_favorite_select_all()}</Button>
				<div class="flex gap-2">
					<Button size="sm" variant="outline" disabled={actionDisabled} onclick={() => M.acceptJoinRequests()}>{m.dialog_group_member_moderation_accept_join_requests()}</Button>
					<Button size="sm" variant="outline" disabled={actionDisabled} onclick={() => M.rejectJoinRequests()}>{m.dialog_group_member_moderation_reject_join_requests()}</Button>
					<Button size="sm" variant="outline" disabled={actionDisabled} onclick={() => M.blockJoinRequests()}>{m.dialog_group_member_moderation_block_join_requests()}</Button>
				</div>
			</div>
			<MemberTable rows={M.joinRequests} kind="invites" persistKey="group-moderation:join-requests" loading={M.loading} />
		</TabsPrimitive.Content>
		<TabsPrimitive.Content value="blocked" class="flex min-h-0 flex-1 flex-col gap-2">
			<div class="flex items-center justify-between gap-2">
				<Button size="sm" variant="outline" onclick={() => M.selectAll(M.blocked)}>{m.view_favorite_select_all()}</Button>
				<Button size="sm" variant="outline" disabled={actionDisabled} onclick={() => M.deleteBlockedRequests()}>{m.dialog_group_member_moderation_delete_blocked_requests()}</Button>
			</div>
			<MemberTable rows={M.blocked} kind="invites" persistKey="group-moderation:blocked" loading={M.loading} />
		</TabsPrimitive.Content>
	</TabsPrimitive.Root>
</div>
