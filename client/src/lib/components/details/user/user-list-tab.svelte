<script lang="ts">
	import Download from '@lucide/svelte/icons/download';
	import ArrowUp from '@lucide/svelte/icons/arrow-up';
	import ArrowDown from '@lucide/svelte/icons/arrow-down';
	import { agentCan } from '#lib/instances/instance-launch.svelte.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import DataTableEmpty from '#lib/components/common/data-table-empty.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import { PERSON_NAME, PERSON_SUBTEXT, ROW, TAB_PANE } from '#lib/details.js';
	import { iconSrc, imageSrc } from '#lib/api/links.js';
	import { groupEdit } from '#lib/groups/group-dialog.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { shiftKey } from '#lib/settings/table-settings.svelte.js';
	import Eye from '@lucide/svelte/icons/eye';
	import ImageIcon from '@lucide/svelte/icons/image';
	import LogOut from '@lucide/svelte/icons/log-out';
	import Plus from '@lucide/svelte/icons/plus';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Tag from '@lucide/svelte/icons/tag';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import Users from '@lucide/svelte/icons/users';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { soleOwner, type TabRow, type UserTabs } from './user-tabs.svelte.js';

	// One of the user dialog's list tabs (UserDialogMutualFriendsTab, GroupsTab, WorldsTab and
	// AvatarsTab): the same toolbar and rows, with the groups tab's sections and edit mode.
	let { tab, tabs, isSelf }: { tab: string; tabs: UserTabs; isSelf: boolean } = $props();

	const AVATAR_GROUPS = [
		{ value: 'all', get label() { return m.dialog_user_avatars_all(); } },
		{ value: 'public', get label() { return m.dialog_user_avatars_public(); } },
		{ value: 'private', get label() { return m.dialog_user_avatars_private(); } },
		{ value: 'licensed', get label() { return m.dialog_user_avatars_purchased(); } }
	];
	// UserDialog's sort options. In-Game Order is VRChat's registry on your PC, so only on your own page with the agent.
	const SORT_OPTIONS: Record<string, { value: string; label: string }[]> = {
		mutuals: [
			{ value: 'alphabetical', get label() { return m.dialog_user_groups_sorting_alphabetical(); } },
			{ value: 'friendOrder', get label() { return m.dialog_user_mutual_friends_sorting_friend_order(); } }
		],
		groups: [
			{ value: 'alphabetical', get label() { return m.dialog_user_groups_sorting_alphabetical(); } },
			{ value: 'members', get label() { return m.dialog_user_groups_sorting_members(); } },
			{ value: 'inGame', get label() { return m.dialog_user_groups_sorting_in_game(); } }
		],
		worlds: [
			{ value: 'name', get label() { return m.dialog_user_worlds_sorting_name(); } },
			{ value: 'updated', get label() { return m.dialog_user_worlds_sorting_updated(); } },
			{ value: 'created', get label() { return m.dialog_user_worlds_sorting_created(); } },
			{ value: 'favorites', get label() { return m.dialog_user_worlds_sorting_favorites(); } },
			{ value: 'popularity', get label() { return m.dialog_user_worlds_sorting_popularity(); } }
		],
		avatars: [
			{ value: 'name', get label() { return m.dialog_user_worlds_sorting_name(); } },
			{ value: 'updated', get label() { return m.dialog_user_worlds_sorting_updated(); } },
			{ value: 'created', get label() { return m.dialog_user_avatars_sort_by_uploaded(); } }
		]
	};

	const state = $derived(tabs.lists[tab]);
	const needle = $derived(tabs.tabFilter.trim().toLowerCase());
	const grouped = $derived(tab === 'avatars' && isSelf ? (state?.rows ?? []).filter((row) => (tabs.avatarGroup === 'all' ? row.group !== 'licensed' : row.group === tabs.avatarGroup)) : (state?.rows ?? []));
	const rows = $derived(tabs.sortRows(tab, grouped.filter((row) => !needle || row.name.toLowerCase().includes(needle))));
</script>

<div class="overflow-hidden {TAB_PANE}">
	<div class="flex shrink-0 items-center justify-between">
		<div class="flex items-center">
			<Button class="rounded-full" variant="ghost" size="icon-sm" aria-label={m.dialog_user_actions_refresh()} disabled={state?.loading} onclick={() => tabs.loadTab(tab, true)}>
				{#if state?.loading}<Spinner />{:else}<RefreshCw />{/if}
			</Button>
			<span class="ml-1.5 inline-flex items-center gap-1 text-sm">
				{#if tab === 'mutuals'}<Users class="size-3.5 text-muted-foreground" />{/if}
				Total {tab === 'groups' ? new Set(grouped.map((row) => row.id)).size : grouped.length}
			</span>
			{#if tab === 'groups' && tabs.groupEditMode}<span class="ml-2 text-3xs">{m.dialog_user_groups_hold_shift()}</span>{/if}
		</div>
		<div class="flex items-center">
			{#if tab === 'avatars'}<Input bind:value={tabs.tabFilter} class="mr-2 h-8 w-40" placeholder={m.dialog_user_avatars_search_placeholder()} />{/if}
			<!-- The original sorts and groups avatars only on your own page. -->
			{#if (tab !== 'avatars' || isSelf) && !(tab === 'groups' && tabs.groupEditMode)}
			<span class="mr-1.5 text-sm">{m.dialog_user_groups_sort_by()}</span>
			<Select.Root type="single" bind:value={tabs.tabSort[tab]}>
				<Select.Trigger size="sm" class="mr-2">{SORT_OPTIONS[tab].find((option) => option.value === tabs.tabSort[tab])?.label}</Select.Trigger>
				<Select.Content>
					{#each SORT_OPTIONS[tab] as option (option.value)}
						<Select.Item value={option.value} label={option.label} disabled={option.value === 'inGame' && (!isSelf || !agentCan('registry'))}>{option.label}</Select.Item>
					{/each}
				</Select.Content>
			</Select.Root>
			{/if}
			{#if tab === 'avatars' && isSelf}
				<span class="mr-1 ml-2 text-sm">{m.dialog_user_avatars_group_by()}</span>
				<Select.Root type="single" bind:value={tabs.avatarGroup}>
					<Select.Trigger size="sm">{AVATAR_GROUPS.find((option) => option.value === tabs.avatarGroup)?.label}</Select.Trigger>
					<Select.Content>
						{#each AVATAR_GROUPS as option (option.value)}
							<Select.Item value={option.value} label={option.label}>{option.label}</Select.Item>
						{/each}
					</Select.Content>
				</Select.Root>
			{/if}
			{#if tab === 'worlds'}
				<Select.Root type="single" bind:value={tabs.worldOrder}>
					<Select.Trigger size="sm" class="mr-2">{tabs.worldOrder === 'descending' ? 'Descending' : 'Ascending'}</Select.Trigger>
					<Select.Content>
						<Select.Item value="descending" label={m.dialog_user_worlds_order_descending()}>{m.dialog_user_worlds_order_descending()}</Select.Item>
						<Select.Item value="ascending" label={m.dialog_user_worlds_order_ascending()}>{m.dialog_user_worlds_order_ascending()}</Select.Item>
					</Select.Content>
				</Select.Root>
			{/if}
			{#if tab !== 'avatars' && !(tab === 'groups' && tabs.groupEditMode)}<Input bind:value={tabs.tabFilter} class="mr-2 h-8 w-40" placeholder={m.view_favorite_worlds_search()} />{/if}
			{#if tab === 'groups' && tabs.groupEditMode}
				<Button variant="outline" size="sm" onclick={() => tabs.exitGroupEdit()}>{m.dialog_user_groups_exit_edit_mode()}</Button>
			{:else if tab === 'groups' && isSelf}
				<Button variant="outline" size="sm" class="ml-2" disabled={state?.loading} onclick={() => tabs.enterGroupEdit()}>{m.dialog_user_groups_edit_mode()}</Button>
			{/if}
		</div>
	</div>
	<div class="min-h-0 flex-1 overflow-auto">
		{#if state?.error}
			<p class="p-1.5 text-sm text-destructive">{state.error}</p>
		{:else}
			{#snippet rowItem(row: TabRow)}
				{#snippet body()}
					<PersonAvatar src={imageSrc(row.image)} iconFrame={row.iconFrame} fallback={ImageIcon} />
					<div class="flex-1 overflow-hidden">
						<span class={PERSON_NAME}>{row.name}</span>
						{#if row.detail}<span class={PERSON_SUBTEXT}>{row.detail}</span>{/if}
					</div>
				{/snippet}
				{#if row.href}
					<a class={ROW} href={row.href}>{@render body()}</a>
				{:else}
					<div class={ROW}>{@render body()}</div>
				{/if}
			{/snippet}
			{#if tab === 'groups' && tabs.groupEditMode && isSelf}
				<div class="flex flex-wrap items-start" style="margin-top: 8px; min-height: 60px">
					<div class="mb-2 flex w-full items-center gap-2">
						<Select.Root type="single" value={tabs.groupBulkValue} onValueChange={(value) => tabs.bulkGroupAction(value)}>
							<Select.Trigger size="sm" disabled={!tabs.groupSelected.length}>{m.dialog_group_actions_manage_selected()}</Select.Trigger>
							<Select.Content>
								<Select.Item value="visibility:visible">{m.dialog_group_actions_visibility_everyone()}</Select.Item>
								<Select.Item value="visibility:friends">{m.dialog_group_actions_visibility_friends()}</Select.Item>
								<Select.Item value="visibility:hidden">{m.dialog_group_actions_visibility_hidden()}</Select.Item>
								<Select.Item value="leave">{m.dialog_user_groups_leave_group_tooltip()}</Select.Item>
							</Select.Content>
						</Select.Root>
						<Button size="sm" variant="outline" onclick={() => tabs.selectAllGroups()}>{tabs.groupSelected.length === tabs.editGroups.length && tabs.editGroups.length ? 'Deselect All' : 'Select All'}</Button>
					</div>
					{#each tabs.editGroups as row (row.id)}
						{@const g = row.groupInfo!}
						{@const sole = soleOwner(g)}
						<div class="box-border flex w-full items-center p-1.5 text-compact hover:rounded-[25px_5px_5px_25px] hover:bg-muted">
							{#if agentCan('registry')}
								<!-- The original's in-game order arrows: top and bottom, then up and down. -->
								<div class="mr-1 flex flex-col gap-1">
									<Button size="icon-sm" variant="outline" class="size-6" aria-label="Move to top" title="Move to top" onclick={() => tabs.moveGroup(row.id, 'top')}><Download class="size-3.5 rotate-180" /></Button>
									<Button size="icon-sm" variant="outline" class="size-6" aria-label="Move to bottom" title="Move to bottom" onclick={() => tabs.moveGroup(row.id, 'bottom')}><Download class="size-3.5" /></Button>
								</div>
								<div class="mr-2 flex flex-col gap-1">
									<Button size="icon-sm" variant="outline" class="size-6" aria-label="Move up" title="Move up" onclick={() => tabs.moveGroup(row.id, 'up')}><ArrowUp class="size-3.5" /></Button>
									<Button size="icon-sm" variant="outline" class="size-6" aria-label="Move down" title="Move down" onclick={() => tabs.moveGroup(row.id, 'down')}><ArrowDown class="size-3.5" /></Button>
								</div>
							{/if}
							<span class="mr-2.5 flex items-center">
								<Checkbox checked={tabs.groupSelected.includes(row.id)} onCheckedChange={(v) => (tabs.groupSelected = v ? [...tabs.groupSelected, row.id] : tabs.groupSelected.filter((id) => id !== row.id))} aria-label="Select {row.name}" />
							</span>
							<a class="flex min-w-0 flex-1 items-center" href={row.href}>
								<PersonAvatar src={iconSrc(row.image)} fallback={Users} />
								<div class="flex-1 overflow-hidden">
									<span class={PERSON_NAME}>{row.name}</span>
									<span class="flex items-center truncate text-xs">
										{#if g.isRepresenting}<span title={m.dialog_group_members_representing()}><Tag class="mr-1.5 size-3.5" /></span>{/if}
										{#if g.memberVisibility && g.memberVisibility !== 'visible'}<span title="Visibility: {g.memberVisibility}"><Eye class="mr-1.5 size-3.5" /></span>{/if}
										<span>({g.memberCount})</span>
									</span>
								</div>
							</a>
							{#if g.memberVisibility}
								<!-- VRChat fixes membership visibility on groups that aren't default privacy (upstream issue 1518). -->
								<span title={g.privacy && g.privacy !== 'default' ? 'This group sets member visibility' : undefined}>
									<Select.Root type="single" value={g.memberVisibility} disabled={!!g.privacy && g.privacy !== 'default'} onValueChange={(v) => v && v !== g.memberVisibility && tabs.setGroupVisibility(row.id, v)}>
										<Select.Trigger size="sm">{g.memberVisibility === 'visible' ? 'Visible' : g.memberVisibility === 'friends' ? 'Friends' : g.memberVisibility === 'hidden' ? 'Hidden' : g.memberVisibility}</Select.Trigger>
										<Select.Content>
											<Select.Item value="visible">{m.dialog_group_actions_visibility_everyone()}</Select.Item>
											<Select.Item value="friends">{m.dialog_group_actions_visibility_friends()}</Select.Item>
											<Select.Item value="hidden">{m.dialog_group_actions_visibility_hidden()}</Select.Item>
										</Select.Content>
									</Select.Root>
								</span>
							{/if}
							<Button
								class={['ml-1.5 h-6 w-6 rounded-full', !shiftKey.held && 'text-red-600']}
								size="icon-sm"
								variant="outline"
								title={sole ? 'Delete Group' : 'Leave Group'}
								aria-label={sole ? 'Delete Group' : 'Leave Group'}
								onclick={() => tabs.leaveGroupPrompt(row.id)}
							>
								{#if sole}<Trash2 />{:else}<LogOut />{/if}
							</Button>
						</div>
					{/each}
				</div>
			{:else if tab === 'groups'}
				<!-- UserDialogGroupsTab: Own Groups (of VRChat's MAX_OWNED), Mutual Groups, and Groups (of MAX_JOINED, or MAX_JOINED_PLUS with VRC+, on your own page), the limits read from VRChat's config. -->
				{@const own = rows.filter((row) => row.group === 'own')}
				{@const mutual = rows.filter((row) => row.group === 'mutual')}
				{@const other = rows.filter((row) => row.group === 'other')}
				<div style="margin-top: 8px">
					<div class="flex justify-between align-bottom">
						<span class="text-base font-bold">{m.dialog_user_groups_own_groups()}<span class="ml-1.5 text-xs">{own.length}/{tabs.groupLimits.maxOwned}</span></span>
						{#if isSelf}
							{@const createBlocked = !sidebarData.me?.isVrcPlus ? 'Creating a group requires VRC+' : tabs.ownedGroups >= tabs.groupLimits.maxOwned ? 'You have reached the owned group limit' : ''}
							<span class="ml-2" title={createBlocked || undefined}>
								<Button size="sm" variant="outline" disabled={state?.loading || !!createBlocked} onclick={() => groupEdit.showCreate()}><Plus /> {m.dialog_group_edit_create()}</Button>
							</span>
						{/if}
					</div>
					<div class="flex flex-wrap items-start" style="margin-top: 8px; margin-bottom: 16px">
						{#each own as row (row.id)}{@render rowItem(row)}{/each}
					</div>
					{#if mutual.length}
						<span class="text-base font-bold">{m.dialog_user_tags_mutual_groups()}</span>
						<span class="ml-1.5 text-xs">{mutual.length}</span>
						<div class="flex flex-wrap items-start" style="margin-top: 8px; margin-bottom: 16px">
							{#each mutual as row (row.id)}{@render rowItem(row)}{/each}
						</div>
					{/if}
					{#if other.length}
						<span class="text-base font-bold">{m.dialog_user_groups_header()}</span>
						<span class="ml-1.5 text-xs">{other.length}{#if isSelf} / {sidebarData.me?.isVrcPlus ? tabs.groupLimits.maxJoinedPlus : tabs.groupLimits.maxJoined}{/if}</span>
						<div class="flex flex-wrap items-start" style="margin-top: 8px; margin-bottom: 16px">
							{#each other as row (row.id)}{@render rowItem(row)}{/each}
						</div>
					{/if}
				</div>
			{:else}
			<div class="flex flex-wrap items-start" style="margin-top: 8px; min-height: 60px">
				{#each rows as row, index (`${row.id}:${index}`)}
					{@render rowItem(row)}
				{:else}
					<!-- The original's DataTableEmpty on the worlds and avatars tabs. -->
					{#if (tab === 'worlds' || tab === 'avatars') && state && !state.loading}
						<div class="flex min-h-30 w-full items-center justify-center">
							<DataTableEmpty />
						</div>
					{/if}
				{/each}
			</div>
			{/if}
		{/if}
	</div>
</div>
