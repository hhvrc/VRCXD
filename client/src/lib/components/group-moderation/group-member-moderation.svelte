<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import BanImportDialog from '#lib/components/group-moderation/ban-import-dialog.svelte';
	import BansTab from '#lib/components/group-moderation/bans-tab.svelte';
	import BulkActions from '#lib/components/group-moderation/bulk-actions.svelte';
	import CsvExportDialog from '#lib/components/group-moderation/csv-export-dialog.svelte';
	import InvitesTab from '#lib/components/group-moderation/invites-tab.svelte';
	import LogsTab from '#lib/components/group-moderation/logs-tab.svelte';
	import MembersTab from '#lib/components/group-moderation/members-tab.svelte';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER } from '#lib/details.js';
	import { groupModeration as M, type AuditLog, type ModMember } from '#lib/groups/group-moderation.svelte.js';

	// The original's GroupMemberModerationDialog, shown in the main pop-up: Members, Bans, Invites
	// and Logs tabs on the left, the selection and bulk actions on the right. Nothing loads until a
	// tab's refresh is pressed, as in the original. userId, when given, is selected on open (the
	// usual way in is showGroupMemberModeration, which passes it through the shared state).
	let { groupId, userId = '' }: { groupId: string; userId?: string } = $props();

	let logsExportOpen = $state(false);
	let bansExportOpen = $state(false);
	let bansImportOpen = $state(false);

	$effect(() => {
		const id = groupId;
		untrack(() => {
			if (M.id !== id) {
				M.clearSelection();
				M.reset();
				M.id = id;
			}
			M.visible = true;
			M.loadGroup();
			const pending = userId || M.openWithUserId;
			M.openWithUserId = '';
			// VRChat can refuse to show the member (403 without the right to view membership details);
			// the original's request layer reports that, so it is reported here too.
			if (pending) M.addToSelection(pending).catch(toastError);
		});
		return () => {
			M.visible = false;
		};
	});

	const LOG_OPTIONS = [
		{ label: 'created_at', get text() { return m.dialog_group_member_moderation_created_at(); } },
		{ label: 'eventType', get text() { return m.dialog_group_member_moderation_type(); } },
		{ label: 'actorDisplayName', get text() { return m.dialog_group_member_moderation_display_name(); } },
		{ label: 'description', get text() { return m.dialog_group_member_moderation_description(); } },
		{ label: 'data', get text() { return m.dialog_group_member_moderation_data(); } }
	];
	const BAN_OPTIONS = [
		{ label: 'userId', get text() { return m.dialog_group_member_moderation_user_id(); } },
		{ label: 'displayName', get text() { return m.dialog_group_member_moderation_display_name(); } },
		{ label: 'roles', get text() { return m.dialog_group_member_moderation_roles(); } },
		{ label: 'managerNotes', get text() { return m.dialog_group_member_moderation_notes(); } },
		{ label: 'joinedAt', get text() { return m.dialog_group_member_moderation_joined_at(); } },
		{ label: 'bannedAt', get text() { return m.dialog_group_member_moderation_banned_at(); } }
	];

	function logValue(row: AuditLog, key: string): string {
		if (key === 'data') return JSON.stringify(row.data) ?? '';
		return String((row as Record<string, unknown>)[key] ?? '');
	}

	function banValue(row: ModMember, key: string): string {
		if (key === 'displayName') return row.user?.displayName ?? '';
		if (key === 'roles') return (row.roleIds ?? []).join(';');
		return String((row as Record<string, unknown>)[key] ?? '');
	}

	const tabs = $derived([
		{ value: 'members', get label() { return m.dialog_group_member_moderation_members(); }, disabled: false },
		{ value: 'bans', get label() { return m.dialog_group_member_moderation_bans(); }, disabled: !M.can('group-bans-manage') },
		{ value: 'invites', get label() { return m.dialog_group_member_moderation_invites(); }, disabled: !M.can('group-invites-manage') },
		{ value: 'logs', get label() { return m.dialog_group_member_moderation_logs(); }, disabled: !M.can('group-audit-view') }
	]);
</script>

<Tooltip.Provider>
	<div class="flex min-h-0 flex-1 flex-col">
		<h2 class="sr-only">{m.dialog_group_member_moderation_header()}</h2>
		<div class="flex h-[70vh] min-h-0 gap-4">
			<div class="flex min-h-0 min-w-0 flex-1 flex-col">
				<div class="mb-1 flex items-baseline gap-2">
					<h3 class="m-0 truncate text-lg font-semibold">{M.groupRef.name}</h3>
					<span class="shrink-0 text-sm text-muted-foreground">{M.groupRef.memberCount ?? ''} Members</span>
				</div>
				<TabsPrimitive.Root bind:value={M.activeTab} class="flex min-h-0 flex-1 flex-col">
					<div class="{TAB_LIST_WRAP} mb-2">
						<TabsPrimitive.List class={TAB_LIST}>
							{#each tabs as tab (tab.value)}
								<TabsPrimitive.Trigger value={tab.value} disabled={tab.disabled} class="{TAB_TRIGGER} disabled:cursor-not-allowed disabled:opacity-50">{tab.label}</TabsPrimitive.Trigger>
							{/each}
						</TabsPrimitive.List>
					</div>
					<TabsPrimitive.Content value="members" class="flex min-h-0 flex-1 flex-col"><MembersTab /></TabsPrimitive.Content>
					<TabsPrimitive.Content value="bans" class="flex min-h-0 flex-1 flex-col">
						<BansTab onexport={() => (bansExportOpen = true)} onimport={() => (bansImportOpen = true)} />
					</TabsPrimitive.Content>
					<TabsPrimitive.Content value="invites" class="flex min-h-0 flex-1 flex-col"><InvitesTab /></TabsPrimitive.Content>
					<TabsPrimitive.Content value="logs" class="flex min-h-0 flex-1 flex-col"><LogsTab onexport={() => (logsExportOpen = true)} /></TabsPrimitive.Content>
				</TabsPrimitive.Root>
			</div>
			<aside class="flex min-h-0 w-80 shrink-0 flex-col border-l pl-4">
				<BulkActions />
			</aside>
		</div>
	</div>
</Tooltip.Provider>

<CsvExportDialog bind:open={logsExportOpen} title={m.dialog_group_member_moderation_export_logs()} rows={M.logs} options={LOG_OPTIONS} value={logValue} />
<CsvExportDialog bind:open={bansExportOpen} title={m.dialog_group_member_moderation_export_bans()} rows={M.bans} options={BAN_OPTIONS} value={banValue} />
<BanImportDialog bind:open={bansImportOpen} groupId={M.id} onimported={() => M.getAllGroupBans()} />
