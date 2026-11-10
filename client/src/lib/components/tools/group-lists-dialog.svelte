<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { groupHref, imageSrc } from '#lib/api/links.js';
	import Ban from '@lucide/svelte/icons/ban';
	import Check from '@lucide/svelte/icons/check';
	import Users from '@lucide/svelte/icons/users';
	import X from '@lucide/svelte/icons/x';
	import { untrack } from 'svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import ToolRefreshButton from './tool-refresh-button.svelte';
	import { api, ApiError } from '#lib/api/client.js';
	import { LoadState } from '#lib/helpers/load-state.svelte.js';
	import type { GroupByStatus } from '#lib/nav/tools.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's BlockedGroupsDialog and GroupInvitesDialog: the same list of groups in one
	// membership state with the actions that state allows. `mode` picks which dialog this is.

	let { mode = $bindable(null) }: { mode?: 'blocked-groups' | 'group-invites' | null } = $props();

	let groups = $state<GroupByStatus[]>([]);
	const loader = new LoadState();
	let activeTab = $state<'invited' | 'requested'>('invited');
	let notice = $state('');
	let confirm = $state<{ description: string; destructive: boolean; run: () => void } | null>(null);

	const status = $derived(mode === 'blocked-groups' ? 'userblocked' : activeTab);
	const groupPath = (group: GroupByStatus, rest: string) => ownPath(`/groups/${encodeURIComponent(group.groupId)}${rest}`);

	async function load() {
		groups = [];
		await loader.run(async () => {
			groups = await api.get<GroupByStatus[]>(ownPath(`/groups/${status}`));
		});
	}

	$effect(() => {
		if (mode && status) {
			notice = '';
			untrack(load);
		}
	});

	async function act(group: GroupByStatus, send: () => Promise<unknown>, success = '') {
		loader.error = '';
		try {
			await send();
			groups = groups.filter((g) => g.id !== group.id);
			notice = success;
		} catch (err) {
			loader.error = ApiError.from(err).message;
		}
	}

	const accept = (group: GroupByStatus) => act(group, () => api.post(groupPath(group, '/join')), 'Joined group');
	const decline = (group: GroupByStatus) => act(group, () => api.delete(groupPath(group, '/membership')));
	const cancelRequest = (group: GroupByStatus) => act(group, () => api.delete(groupPath(group, '/request')));
	const block = (group: GroupByStatus) =>
		(confirm = { get description() { return m.confirm_block_group(); }, destructive: true, run: () => act(group, () => api.post(groupPath(group, '/block'))) });
	const unblock = (group: GroupByStatus) =>
		(confirm = { get description() { return m.confirm_unblock_group(); }, destructive: false, run: () => act(group, () => api.delete(groupPath(group, '/membership'))) });
</script>

<Dialog.Root open={mode !== null} onOpenChange={(o) => !o && (mode = null)}>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>{mode === 'blocked-groups' ? 'Blocked Groups' : 'Group Invites'}</Dialog.Title>
		</Dialog.Header>
		<div class="flex items-center gap-2">
			{#if mode === 'group-invites'}
				<Tabs.Root bind:value={activeTab}>
					<Tabs.List>
						<Tabs.Trigger value="invited">{m.dialog_group_invites_invited()}</Tabs.Trigger>
						<Tabs.Trigger value="requested">{m.dialog_group_invites_requested()}</Tabs.Trigger>
					</Tabs.List>
				</Tabs.Root>
			{/if}
			<ToolRefreshButton label={m.dialog_group_actions_refresh()} loading={loader.isLoading} onclick={load} />
		</div>
		{#if loader.error}<p class="text-xs text-destructive">{loader.error}</p>{/if}
		{#if notice}<p class="text-xs text-muted-foreground">{notice}</p>{/if}
		<div class="max-h-[60vh] overflow-y-auto">
			{#if !groups.length && !loader.isLoading}<p class="{EMPTY_STATE} text-xs">{m.common_no_data()}</p>{/if}
			{#each groups as group (group.id)}
				<div class="flex items-center gap-3 border-b py-2 last:border-b-0">
					<Avatar.Root class="size-9 flex-none">
						<Avatar.Image src={imageSrc(group.iconUrl) ?? undefined} class="object-cover" />
						<Avatar.Fallback><Users class="size-4 text-muted-foreground" /></Avatar.Fallback>
					</Avatar.Root>
					<div class="min-w-0 flex-1 text-sm">
						<a class="block truncate font-medium hover:underline" href={groupHref(group.groupId)}>{group.name}</a>
						<div class="truncate text-xs text-muted-foreground">{group.shortCode}.{group.discriminator} · {group.memberCount}</div>
					</div>
					{#if mode === 'blocked-groups'}
						<Button size="sm" variant="outline" onclick={() => unblock(group)}>{m.dialog_blocked_groups_unblock()}</Button>
					{:else if activeTab === 'requested'}
						<Button size="icon-sm" variant="ghost" title={m.dialog_group_actions_cancel_join_request_tooltip()} aria-label={m.dialog_group_actions_cancel_join_request_tooltip()} onclick={() => cancelRequest(group)}><X class="size-4" /></Button>
					{:else}
						<Button size="icon-sm" variant="ghost" title={m.dialog_group_invites_accept()} aria-label={m.dialog_group_invites_accept()} onclick={() => accept(group)}><Check class="size-4" /></Button>
						<Button size="icon-sm" variant="ghost" title={m.dialog_group_invites_decline()} aria-label={m.dialog_group_invites_decline()} onclick={() => decline(group)}><X class="size-4" /></Button>
						<Button size="icon-sm" variant="ghost" class="text-destructive" title={m.dialog_group_invites_block()} aria-label={m.dialog_group_invites_block()} onclick={() => block(group)}><Ban class="size-4" /></Button>
					{/if}
				</div>
			{/each}
		</div>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root open={confirm !== null} onOpenChange={(o) => !o && (confirm = null)}>
	<Dialog.Content class="sm:max-w-sm">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_alertdialog_confirm()}</Dialog.Title>
			<Dialog.Description>{confirm?.description}</Dialog.Description>
		</Dialog.Header>
		<Dialog.Footer>
			<Button variant="outline" onclick={() => (confirm = null)}>{m.dialog_group_transfer_cancel()}</Button>
			<Button
				variant={confirm?.destructive ? 'destructive' : 'default'}
				onclick={() => {
					confirm?.run();
					confirm = null;
				}}>{m.dialog_alertdialog_confirm()}</Button
			>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
