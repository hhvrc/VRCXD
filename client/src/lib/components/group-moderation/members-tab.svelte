<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ArrowDown from '@lucide/svelte/icons/arrow-down';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import MemberTable from '#lib/components/group-moderation/member-table.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { FILTER_OPTIONS, groupModeration as M, SORTING_OPTIONS } from '#lib/groups/group-moderation.svelte.js';

	// The original's GroupModerationMembersTab.
	const canSearch = $derived(M.can('group-bans-manage'));
	const sortFilterDisabled = $derived(M.loading || !!M.memberSearch.length || !canSearch);
</script>

<div class="flex h-full min-h-0 flex-col gap-2">
	<div class="flex items-center gap-2">
		<Button class="rounded-full" variant="outline" size="icon-sm" aria-label={m.dialog_note_export_refresh()} disabled={M.loading} onclick={() => M.loadAllGroupMembers()}>
			{#if M.loading}<Spinner />{:else}<RefreshCw />{/if}
		</Button>
		<span class="shrink-0 text-sm tabular-nums">{M.members.length}/{M.groupRef.memberCount ?? ''}</span>
		<ClearableInput bind:value={M.memberSearch} disabled={!canSearch} class="flex-1" placeholder={m.dialog_group_members_search()} oninput={() => M.search()} />
		<DropdownMenu.Root>
			<DropdownMenu.Trigger disabled={sortFilterDisabled}>
				{#snippet child({ props })}
					<Button {...props} size="sm" variant="outline" disabled={sortFilterDisabled}>
						<span class="text-muted-foreground">{m.dialog_group_members_sort_by()}</span>
						{M.memberSortOrder.name}
						<ArrowDown />
					</Button>
				{/snippet}
			</DropdownMenu.Trigger>
			<DropdownMenu.Content>
				{#each SORTING_OPTIONS as item (item.name)}
					<DropdownMenu.Item onSelect={() => M.setSortOrder(item)}>{item.name}</DropdownMenu.Item>
				{/each}
			</DropdownMenu.Content>
		</DropdownMenu.Root>
		<DropdownMenu.Root>
			<DropdownMenu.Trigger disabled={sortFilterDisabled}>
				{#snippet child({ props })}
					<Button {...props} size="sm" variant="outline" disabled={sortFilterDisabled}>
						<span class="text-muted-foreground">{m.dialog_group_members_filter()}</span>
						{M.memberFilter.name}
						<ArrowDown />
					</Button>
				{/snippet}
			</DropdownMenu.Trigger>
			<DropdownMenu.Content>
				{#each FILTER_OPTIONS as item (item.name)}
					<DropdownMenu.Item onSelect={() => M.setFilter(item)}>{item.name}</DropdownMenu.Item>
				{/each}
				{#each M.groupRef.roles ?? [] as role (role.id)}
					{#if !role.defaultRole}
						<DropdownMenu.Item onSelect={() => M.setFilter({ name: role.name, id: role.id })}>{role.name}</DropdownMenu.Item>
					{/if}
				{/each}
			</DropdownMenu.Content>
		</DropdownMenu.Root>
		<Button size="sm" variant="outline" onclick={() => M.selectAll(M.members)}>{m.view_favorite_select_all()}</Button>
	</div>
	{#if M.members.length}
		<MemberTable rows={M.members} kind="members" persistKey="group-moderation:members" loading={M.loading} />
	{/if}
</div>
