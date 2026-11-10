<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import UserIcon from '@lucide/svelte/icons/user';
	import DataTable, { type Column } from '#lib/components/common/data-table.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { imageSrc } from '#lib/api/links.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { groupModeration, resolveRoleNames, userImage, userImageFull, type ModMember } from '#lib/groups/group-moderation.svelte.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';

	// The original's groupMemberModeration{Members,Bans,Invites,JoinRequests,Blocked}Columns: a
	// select box, the avatar, the name, then what the table has: roles, note, joined, visibility or
	// banned at.
	let {
		rows,
		persistKey,
		kind,
		loading = false
	}: { rows: ModMember[]; persistKey: string; kind: 'members' | 'bans' | 'invites'; loading?: boolean } = $props();

	const roles = $derived(groupModeration.groupRef.roles ?? []);
	const rolesText = (row: ModMember) => resolveRoleNames(row.roleIds, roles);

	const columns = $derived.by<Column<ModMember>[]>(() => {
		const base: Column<ModMember>[] = [
			{ id: 'selected', size: 55, enableResizing: false, cell: selectCell },
			{ id: 'avatar', get label() { return m.dialog_group_member_moderation_avatar(); }, size: 70, cell: avatarCell },
			{ id: 'displayName', get label() { return m.dialog_group_member_moderation_display_name(); }, size: 160, sortable: true, value: (row) => row.user?.displayName ?? '', cell: nameCell }
		];
		if (kind !== 'invites') base.push({ id: 'roles', get label() { return m.dialog_group_member_moderation_roles(); }, sortable: true, value: rolesText, cell: rolesCell });
		base.push({ id: 'managerNotes', get label() { return m.dialog_group_member_moderation_notes(); }, sortable: true, value: (row) => row.managerNotes ?? '', cell: noteCell });
		if (kind !== 'invites') base.push({ id: 'joinedAt', get label() { return m.dialog_group_member_moderation_joined_at(); }, size: 170, sortable: true, value: (row) => row.joinedAt ?? '', cell: joinedCell });
		if (kind === 'members') base.push({ id: 'visibility', get label() { return m.dialog_group_member_moderation_visibility(); }, size: 120, sortable: true, value: (row) => row.visibility ?? '', cell: visibilityCell });
		if (kind === 'bans') base.push({ id: 'bannedAt', get label() { return m.dialog_group_member_moderation_banned_at(); }, size: 170, sortable: true, value: (row) => row.bannedAt ?? '', cell: bannedCell });
		return base;
	});
</script>

{#snippet selectCell(row: ModMember)}
	<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
	<div class="flex items-center justify-center" onclick={(event) => event.stopPropagation()}>
		<Checkbox checked={groupModeration.selected.has(row.userId)} aria-label="Select {row.user?.displayName ?? row.userId}" onCheckedChange={(value) => groupModeration.toggle(row, value)} />
	</div>
{/snippet}

{#snippet avatarCell(row: ModMember)}
	<button type="button" class="flex cursor-pointer items-center" aria-label="Show picture" onclick={() => imageViewer.show(imageSrc(userImageFull(row.user)))}>
		<Avatar.Root class="size-6 rounded-full">
			<Avatar.Image src={imageSrc(userImage(row.user))} class="object-cover" loading="lazy" />
			<Avatar.Fallback><UserIcon class="size-3 text-muted-foreground" /></Avatar.Fallback>
		</Avatar.Root>
	</button>
{/snippet}

{#snippet nameCell(row: ModMember)}
	<button type="button" class="cursor-pointer truncate text-left" onclick={() => userDialog.open(row.userId)}>{row.user?.displayName}</button>
{/snippet}

{#snippet rolesCell(row: ModMember)}
	<span>{rolesText(row)}</span>
{/snippet}

{#snippet noteCell(row: ModMember)}
	{#if row.managerNotes}
		<Tooltip.Root>
			<Tooltip.Trigger>
				{#snippet child({ props })}
					<span {...props} class="inline-block max-w-full truncate align-middle">{row.managerNotes}</span>
				{/snippet}
			</Tooltip.Trigger>
			<Tooltip.Content class="max-w-100 whitespace-pre-wrap">{row.managerNotes}</Tooltip.Content>
		</Tooltip.Root>
	{/if}
{/snippet}

{#snippet joinedCell(row: ModMember)}
	<span>{formatDateFilter(row.joinedAt, 'long')}</span>
{/snippet}

{#snippet visibilityCell(row: ModMember)}
	<span>{row.visibility}</span>
{/snippet}

{#snippet bannedCell(row: ModMember)}
	<span>{formatDateFilter(row.bannedAt, 'long')}</span>
{/snippet}

<!-- The original's group moderation tables start at 15 rows until one has its own size. -->
<DataTable {rows} {columns} {loading} {persistKey} pageSize={15} rowKey={(row) => row.userId} />
