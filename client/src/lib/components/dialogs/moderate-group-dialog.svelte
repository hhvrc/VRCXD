<script lang="ts" module>
	// The group picked last time, kept for the session as the original's store keeps it.
	let lastGroupId = '';
</script>

<script lang="ts">
	import { PERSON_NAME } from '#lib/details.js';
	import PersonAvatar from '#lib/components/common/person-avatar.svelte';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import OptionPicker, { type PickerOption } from '#lib/components/common/option-picker.svelte';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { hasModerationPermission, showGroupMemberModeration, userImage, type ModUser } from '#lib/groups/group-moderation.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's ModerateGroupDialog, the user page's "Group Moderation": the user, one of the
	// account's groups it may moderate, and "Moderation Tools" opening the member moderation tool
	// for that group with the user selected.
	type OptionGroup = { id: string; name: string; iconUrl?: string | null; permissions: string[] };

	let { userId = $bindable(), userName }: { userId?: string; userName?: string } = $props();

	let groups = $state<OptionGroup[]>([]);
	let groupIds = $state<string[]>([]);
	let user = $state<ModUser | undefined>();

	const open = $derived(!!userId);
	const groupOptions = $derived<PickerOption[]>(groups.map((g) => ({ value: g.id, label: g.name, image: g.iconUrl ?? null, section: 'Moderate Group Member' })));
	const shownName = $derived(user?.displayName || userName || '');

	$effect(() => {
		const target = userId;
		if (!target) return;
		untrack(() => {
			user = undefined;
			groupIds = lastGroupId ? [lastGroupId] : [];
			const account = vrchatAuth.ownerUserId;
			api
				.get<{ groups: OptionGroup[] }>(accountPath(account, '/instances/options'))
				.then((data) => {
					groups = data.groups.filter((g) => hasModerationPermission(g.permissions));
					// A remembered group the account can no longer moderate is dropped, as the original drops one it can't fetch.
					if (groupIds[0] && !groups.some((g) => g.id === groupIds[0])) groupIds = [];
				})
				.catch(() => (groups = []));
			api.get<ModUser>(`/1/users/${encodeURIComponent(target)}`, { account }).then(
				(found) => {
					if (userId === target) user = found;
				},
				() => {}
			);
		});
	});

	function close() {
		lastGroupId = groupIds[0] ?? '';
		userId = undefined;
	}

	function openTools() {
		const groupId = groupIds[0];
		const target = userId;
		if (!groupId || !target) return;
		close();
		showGroupMemberModeration(groupId, target);
	}
</script>

<Dialog.Root bind:open={() => open, (value) => { if (!value) close(); }}>
	<Dialog.Content class="bg-background sm:max-w-112.5">
		<Dialog.Header><Dialog.Title>{m.dialog_moderate_group_header()}</Dialog.Title></Dialog.Header>
		<div class="box-border flex cursor-default items-center p-1.5 text-compact">
			<PersonAvatar src={imageSrc(userImage(user))} fallback={null} />
			<div class="flex-1 overflow-hidden">
				{#if shownName}
					<span class={PERSON_NAME}>{shownName}</span>
				{:else}
					<span>{userId}</span>
				{/if}
			</div>
		</div>
		<div class="mt-4 w-full">
			<OptionPicker options={groupOptions} bind:values={groupIds} placeholder={m.dialog_moderate_group_choose_group_placeholder()} />
		</div>
		<Dialog.Footer>
			<Button disabled={!userId || !groupIds[0]} onclick={openTools}>{m.dialog_group_actions_moderation_tools()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
