<script lang="ts">
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { nameColour } from '#lib/people/user-colour.js';
	import { onMount } from 'svelte';
	import Download from '@lucide/svelte/icons/download';
	import Eye from '@lucide/svelte/icons/eye';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import Pencil from '@lucide/svelte/icons/pencil';
	import RefreshCcw from '@lucide/svelte/icons/refresh-ccw';
	import Tag from '@lucide/svelte/icons/tag';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import OptionPicker, { type PickerOption } from '#lib/components/common/option-picker.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { imageSrc, userHref } from '#lib/api/links.js';
	import { ROW, PERSON_NAME, TAB_PANE } from '#lib/details.js';
	import { downloadJson, hasGroupPermission, mergeGroupMemberSearchResults, type Group, type GroupMember } from '#lib/groups/group-dialog.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupDialogMembersTab and useGroupMembers: the members a page of 100 at a time,
	// newest first unless sorted otherwise, one role's or everyone's, and VRChat's name search with
	// the loaded members it misses. The account itself comes first. Sort and filter outlive the tab.
	let {
		group,
		inGroup,
		sort = $bindable('joinedAt:desc'),
		filter = $bindable<string | null>(null),
		members = $bindable<GroupMember[]>([])
	}: { group: Group; inGroup: boolean; sort?: string; filter?: string | null; members?: GroupMember[] } = $props();

	const PAGE = 100;
	const SEARCH_MAX_PAGES = 10;
	const SORTS = [
		{ value: 'joinedAt:desc', get label() { return m.dialog_group_members_sorting_joined_at_desc(); } },
		{ value: 'joinedAt:asc', get label() { return m.dialog_group_members_sorting_joined_at_asc(); } }
	];

	let loading = $state(false);
	let done = $state(false);
	let error = $state('');
	let search = $state('');
	let results = $state<GroupMember[]>([]);
	let offset = 0;
	let searchRequest = 0;
	let searchTimer: ReturnType<typeof setTimeout> | undefined;
	let alive = true;

	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(`${accountPath(owner)}/groups/${encodeURIComponent(group.id)}`);
	const canManage = $derived(hasGroupPermission(group, 'group-members-manage'));
	const filterKey = $derived(filter === null ? 'everyone' : filter === '' ? 'usersWithNoRole' : `role:${filter}`);
	const filterOptions = $derived<PickerOption[]>([
		{ value: 'everyone', get label() { return m.dialog_group_members_filters_everyone(); }, section: 'Filter:' },
		{ value: 'usersWithNoRole', get label() { return m.dialog_group_members_filters_users_with_no_role(); }, section: 'Filter:' },
		...(group.roles ?? []).filter((r) => !r.defaultRole).map((r) => ({ value: `role:${r.id}`, label: r.name, section: 'Roles' }))
	]);
	const roleNames = $derived(new Map((group.roles ?? []).map((r) => [r.id, r.name])));

	const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
	const memberImage = (m: GroupMember) => m.user?.iconUrl || m.user?.thumbnailUrl || m.user?.currentAvatarThumbnailImageUrl || m.user?.currentAvatarImageUrl; // The original's userImage order.
	const roleText = (ids: string[] = []) => ids.map((id) => roleNames.get(id)).filter((name) => !!name).join(', ');

	/** getGroupDialogGroupMembers: start over, the account first when it is a member and no role is picked. */
	export async function reload() {
		members = [];
		done = false;
		offset = 0;
		error = '';
		const groupId = group.id;
		if (inGroup) {
			try {
				const me = await api.get<GroupMember | undefined>(`${base}/members/${encodeURIComponent(owner)}`);
				if (groupId !== group.id) return;
				// When filtered by a role the account is left out, as in the original.
				if (me && filter === null) {
					const self = sidebarData.me;
					members = [{ ...me, user: self ? { id: self.id, displayName: self.displayName, thumbnailUrl: self.imageUrl } : me.user }];
				}
			} catch (err) {
				error = ApiError.from(err).message;
			}
		}
		await loadMore();
	}

	async function loadMore(preserveSearch = false) {
		if (done || loading) return;
		const groupId = group.id;
		if (!preserveSearch) search = '';
		loading = true;
		try {
			// "Users With No Role" sends no roleId, as the original drops an empty one.
			const page = await api.get<GroupMember[]>(`${base}/members`, { n: PAGE, offset, sort, ...(filter ? { roleId: filter } : {}) });
			if (groupId !== group.id) return;
			let current = members;
			if (page.some((item) => item.userId === owner) && current[0]?.userId === owner) current = current.slice(1);
			if (page.length < PAGE) done = true;
			members = [...current, ...page];
			offset += PAGE;
		} catch (err) {
			done = true;
			error = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	/** loadAllGroupMembers: every page, a second apart. */
	async function loadAll() {
		if (loading) return;
		await reload();
		while (alive && !done) {
			loading = true;
			await wait(1000);
			loading = false;
			await loadMore();
		}
	}

	function setSort(value: string) {
		if (value === sort) return;
		sort = value;
		reload();
	}

	function setFilter(key: string) {
		const next = key === 'everyone' ? null : key === 'usersWithNoRole' ? '' : key.slice('role:'.length);
		if (next === filter) return;
		filter = next;
		reload();
	}

	function onSearch() {
		searchRequest += 1;
		clearTimeout(searchTimer);
		if (search.length < 3) {
			results = [];
			loading = false;
			return;
		}
		searchTimer = setTimeout(runSearch, 200);
	}

	async function runSearch() {
		const query = search;
		const request = searchRequest;
		results = [];
		if (query.length < 3) return;
		loading = true;
		try {
			const found = await api.get<{ results: GroupMember[] }>(`${base}/members/search`, { query, n: PAGE, offset: 0 });
			if (request !== searchRequest || query !== search) return;
			results = mergeGroupMemberSearchResults(found.results, members, query);
			await loadRemaining(request, query);
		} catch {
			// The original ignores search errors.
		} finally {
			if (request === searchRequest) loading = false;
		}
	}

	/** The search index can miss recent joins: page through the members until one turns up. */
	async function loadRemaining(request: number, query: string) {
		for (let page = 0; page < SEARCH_MAX_PAGES; ++page) {
			if (results.length || !alive || request !== searchRequest || query !== search || done) return;
			loading = true;
			await wait(1000);
			loading = false;
			if (!alive || request !== searchRequest || query !== search || done) return;
			await loadMore(true);
			if (request !== searchRequest || query !== search) return;
			results = mergeGroupMemberSearchResults(results, members, query);
		}
	}

	onMount(() => {
		reload();
		return () => {
			alive = false;
			clearTimeout(searchTimer);
		};
	});
</script>

{#snippet memberRow(member: GroupMember)}
	{@const known = sidebarData.friends.find((f) => f.userId === member.userId)}
	<a class={ROW} href={userHref(member.userId)}>
		<PersonAvatar src={imageSrc(memberImage(member))} iconFrame={member.user?.iconFrame ?? known?.iconFrame} />
		<div class="flex-1 overflow-hidden">
			<span class={PERSON_NAME} style:color={nameColour(known ?? { id: member.userId }, appearance.isDarkMode)}>{member.user?.displayName || member.userId}</span>
			<span class="flex items-center truncate text-xs">
				{#if canManage}
					{#if member.isRepresenting}<span title={m.dialog_group_members_representing()}><Tag class="mr-1.5 size-3.5" /></span>{/if}
					{#if member.visibility !== 'visible'}<span title="Visibility: {member.visibility}"><Eye class="mr-1.5 size-3.5" /></span>{/if}
					{#if !member.isSubscribedToAnnouncements}<span title={m.dialog_group_members_unsubscribed_announcements()}><MessageSquare class="mr-1.5 size-3.5" /></span>{/if}
					{#if member.managerNotes}
						<Tooltip.Root>
							<Tooltip.Trigger><Pencil class="mr-1.5 size-3.5" /></Tooltip.Trigger>
							<Tooltip.Content side="top"><span>{m.dialog_group_members_manager_notes()}</span><br /><span class="whitespace-pre-wrap">{member.managerNotes}</span></Tooltip.Content>
						</Tooltip.Root>
					{/if}
				{/if}
				<span class="truncate" title={roleText(member.roleIds)}>{roleText(member.roleIds)}</span>
			</span>
		</div>
	</a>
{/snippet}

<div class="mt-2 {TAB_PANE}">
	<div class="sticky top-0 z-10 pb-2">
		{#if hasGroupPermission(group, 'group-members-viewall')}
			<span class="p-1 text-base font-bold">{m.dialog_group_members_all_members()}</span>
		{:else}
			<span class="text-base font-bold">{m.dialog_group_members_friends_only()}</span>
		{/if}
		<div class="mt-2 flex flex-wrap items-center gap-y-2">
			<Button class="size-6 rounded-full" variant="ghost" size="icon-sm" aria-label="Load all members" onclick={loadAll}>
				{#if loading}<Spinner />{:else}<RefreshCcw />{/if}
			</Button>
			<Button class="ml-1.5 size-6 rounded-full" variant="ghost" size="icon-sm" aria-label="Download members" onclick={() => downloadJson(`${group.id}_members`, members)}><Download class="size-4" /></Button>
			<span class="mx-1.5 text-sm">{search.length ? results.length : members.length}/{group.memberCount ?? ''}</span>
			{#if canManage}
				<div class="ml-auto flex items-center">
					<span class="mr-1.5 text-sm">{m.dialog_group_members_sort_by()}</span>
					<Select.Root type="single" value={sort} disabled={loading || search.length > 0} onValueChange={setSort}>
						<Select.Trigger size="sm" class="mr-1 w-45">{SORTS.find((s) => s.value === sort)?.label ?? 'Sort By:'}</Select.Trigger>
						<Select.Content>
							{#each SORTS as option (option.value)}<Select.Item value={option.value} label={option.label} />{/each}
						</Select.Content>
					</Select.Root>
					<span class="mr-1 ml-2 text-sm">{m.dialog_group_members_filter()}</span>
					<div class="w-55">
						<!-- Not clearable, as in the original: picking the chosen filter again keeps it. -->
						<OptionPicker
							options={filterOptions}
							bind:values={() => [filterKey], (picked) => { if (picked[0]) setFilter(picked[0]); }}
							disabled={loading || search.length > 0}
							placeholder={m.dialog_group_members_filter()}
						/>
					</div>
				</div>
			{/if}
		</div>
		<ClearableInput bind:value={search} disabled={!canManage} placeholder={m.dialog_group_members_search()} class="mt-2 flex-1" oninput={onSearch} />
		{#if error}<p class="mt-1 text-xs text-destructive">{error}</p>{/if}
	</div>
	{#if search.length}
		<div class="flex min-h-0 min-w-[130px] flex-1 flex-wrap items-start overflow-x-hidden overflow-y-auto pt-2">
			{#each results as member (member.id)}{@render memberRow(member)}{/each}
		</div>
	{:else if members.length}
		<div class="flex min-h-0 min-w-[130px] flex-1 flex-wrap items-start overflow-x-hidden overflow-y-auto pt-2">
			{#each members as member (member.id)}{@render memberRow(member)}{/each}
			{#if !done}
				<button type="button" class="box-border flex h-[45px] w-full cursor-pointer items-center p-1.5 text-center text-compact" onclick={() => loadMore()}>
					{#if !loading}<span class="block flex-1 truncate leading-4.5 font-medium">{m.dialog_group_members_load_more()}</span>{/if}
				</button>
			{/if}
		</div>
	{/if}
</div>
