<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import AlertTriangle from '@lucide/svelte/icons/triangle-alert';
	import Plus from '@lucide/svelte/icons/plus';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import { Separator } from '#lib/components/ui/separator/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { NONE } from '#lib/details.js';
	import { groupModeration as M } from '#lib/groups/group-moderation.svelte.js';

	// The original's GroupModerationBulkActions: the selected users, roles, the manager note and
	// kick, ban and unban, each run over the selection one user at a time.
	let selectUserId = $state('');
	let selectedRoles = $state<string[]>([]);
	let note = $state('');

	const users = $derived(M.selectedUsers);
	const roleOptions = $derived((M.groupRef.roles ?? []).map((r) => ({ value: r.id, label: r.name })));
	const progressPercent = $derived(M.progressTotal ? Math.round((M.progressCurrent / M.progressTotal) * 100) : 0);

	function isActionDisabled(permission: string) {
		return !users.length || !!M.progressCurrent || !M.can(permission);
	}
	const rolesDisabled = $derived(!selectedRoles.length || isActionDisabled('group-roles-assign'));

	async function selectUser() {
		await M.selectUserIds(selectUserId);
		selectUserId = '';
	}
</script>

<div class="flex min-h-0 flex-1 flex-col gap-3">
	<div class="flex items-center justify-between">
		<span class="text-sm font-semibold">
			{m.dialog_invite_to_group_selected_users()}
			<span class="ml-1 font-normal text-muted-foreground">{users.length}</span>
		</span>
		<Button class="rounded-full" size="icon-sm" variant="ghost" disabled={!users.length} aria-label={m.dialog_world_actions_delete()} onclick={() => M.clearSelection()}>
			<Trash2 />
		</Button>
	</div>

	<div class="flex gap-2">
		<ClearableInput bind:value={selectUserId} class="flex-1" placeholder={m.dialog_group_member_moderation_user_id_placeholder()} onenter={() => selectUserId && selectUser()} />
		<Tooltip.Root>
			<Tooltip.Trigger>
				{#snippet child({ props })}
					<Button {...props} size="icon-sm" variant="outline" aria-label={m.dialog_group_member_moderation_select_user()} disabled={!selectUserId} onclick={selectUser}><Plus /></Button>
				{/snippet}
			</Tooltip.Trigger>
			<Tooltip.Content side="top">{m.dialog_group_member_moderation_select_user()}</Tooltip.Content>
		</Tooltip.Root>
	</div>

	<div class="min-h-24 flex-1 overflow-y-auto rounded-md border">
		{#if users.length}
			<div class="p-1">
				{#each users as user (user.userId)}
					<div class="flex items-center gap-1.5 rounded px-2 py-1 text-sm hover:bg-muted">
						{#if user.membershipStatus !== 'member'}
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}
										<span {...props}><AlertTriangle class="size-3.5 shrink-0 text-amber-500" /></span>
									{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="top">{m.dialog_group_member_moderation_user_isnt_in_group()}</Tooltip.Content>
							</Tooltip.Root>
						{/if}
						<span class="flex-1 truncate font-medium">{user.user?.displayName || user.userId}</span>
						<button type="button" class="inline-flex shrink-0 cursor-pointer items-center text-muted-foreground hover:text-foreground" aria-label={m.dialog_world_actions_delete()} onclick={() => M.toggle(user, false)}>
							<X class="size-3.5" />
						</button>
					</div>
				{/each}
			</div>
		{:else}
			<div class="p-3 text-center text-sm text-muted-foreground">{NONE}</div>
		{/if}
	</div>

	<Separator />

	<div class="flex flex-col gap-2">
		<span class="text-sm font-semibold">{m.dialog_group_info_roles()}</span>
		<MultiSelect values={selectedRoles} options={roleOptions} placeholder={m.dialog_new_instance_role_placeholder()} class="w-full" onchange={(values) => (selectedRoles = values)} />
		<div class="grid grid-cols-2 gap-2">
			<Button size="sm" variant="outline" disabled={rolesDisabled} onclick={() => M.addRoles(selectedRoles)}>{m.dialog_group_member_moderation_add_roles()}</Button>
			<Button size="sm" variant="secondary" disabled={rolesDisabled} onclick={() => M.removeRoles(selectedRoles)}>{m.dialog_group_member_moderation_remove_roles()}</Button>
		</div>
	</div>

	<Separator />

	<div class="flex flex-col gap-2">
		<span class="text-sm font-semibold">{m.dialog_group_member_moderation_notes()}</span>
		<Textarea bind:value={note} rows={2} class="min-h-0 resize-none text-xs" placeholder={m.dialog_user_info_note_placeholder()} />
		<Button size="sm" variant="outline" disabled={isActionDisabled('group-members-manage')} onclick={() => M.saveNote(note)}>{m.dialog_group_member_moderation_save_note()}</Button>
	</div>

	<Separator />

	<div class="flex flex-col gap-2">
		<span class="text-sm font-semibold">{m.dialog_group_member_moderation_actions()}</span>
		<div class="grid grid-cols-3 gap-2">
			<Button size="sm" variant="outline" disabled={isActionDisabled('group-members-remove')} onclick={() => M.kick()}>{m.dialog_group_member_moderation_kick()}</Button>
			<Button size="sm" variant="destructive" disabled={isActionDisabled('group-bans-manage')} onclick={() => M.ban()}>{m.dialog_group_member_moderation_ban()}</Button>
			<Button size="sm" variant="outline" disabled={isActionDisabled('group-bans-manage')} onclick={() => M.unban()}>{m.dialog_group_member_moderation_unban()}</Button>
		</div>
	</div>

	{#if M.progressCurrent}
		<div class="flex flex-col gap-2 rounded-md border p-2">
			<div class="flex items-center gap-2 text-sm">
				<Spinner class="size-4" />
				<span class="flex-1">Progress: {M.progressCurrent}/{M.progressTotal}</span>
				<Button size="sm" variant="secondary" onclick={() => M.cancelProgress()}>{m.common_actions_cancel()}</Button>
			</div>
			<Progress value={progressPercent} />
		</div>
	{/if}
</div>
