<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Timer from '#lib/components/common/timer.svelte';
	import DisplayName from '#lib/components/common/display-name.svelte';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import type { Group, GroupPost } from '#lib/groups/group-dialog.svelte.js';
	import Eye from '@lucide/svelte/icons/eye';

	// A group post's footer, on the announcement and in the Posts tab: who can see it, who wrote and
	// edited it, and when.
	let { entry, group }: { entry: GroupPost; group: Group } = $props();

	const roleName = (id: string) => group?.roles?.find((r) => r.id === id)?.name;
</script>

{#if entry.roleIds?.length}
	<Tooltip.Root>
		<Tooltip.Trigger><Eye class="size-3" /></Tooltip.Trigger>
		<Tooltip.Content side="top"><span>{m.dialog_group_posts_visibility()}</span><br />{entry.roleIds.map(roleName).filter(Boolean).join(', ')}</Tooltip.Content>
	</Tooltip.Root>
{/if}
{#if entry.authorId}<DisplayName userId={entry.authorId} />{/if}
{#if entry.editorId}<span>(Edited by: <DisplayName userId={entry.editorId} />)</span>{/if}
<Tooltip.Root>
	<Tooltip.Trigger><Timer epoch={entry.updatedAt} /></Tooltip.Trigger>
	<Tooltip.Content side="bottom">
		<span>Created At: {formatDateFilter(entry.createdAt, 'long')}</span>
		{#if entry.updatedAt !== entry.createdAt}<br /><span>Edited At: {formatDateFilter(entry.updatedAt, 'long')}</span>{/if}
	</Tooltip.Content>
</Tooltip.Root>
