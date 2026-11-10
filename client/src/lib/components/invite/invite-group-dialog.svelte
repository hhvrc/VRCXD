<script lang="ts">
	import { readText, storeText } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import OptionPicker, { type PickerOption } from '#lib/components/common/option-picker.svelte';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InviteGroupDialog: one of the account's groups it may invite to, the chosen
	// friends, and one invite per friend in turn. The last group picked is remembered.
	type Friend = { userId: string; displayName: string; state: string; thumbnailImageUrl?: string | null };
	type OptionGroup = { id: string; name: string; iconUrl?: string | null; permissions: string[] };

	// Opened for a user (userId) from the user pop-up, or for a group (open with groupId) from the
	// group pop-up, which picks that group instead of the last one.
	let {
		userId = $bindable(),
		userName,
		open: openForGroup = $bindable(false),
		groupId = ''
	}: { userId?: string; userName?: string; open?: boolean; groupId?: string } = $props();

	const LAST_GROUP_KEY = 'inviteGroupLastGroup';
	const INVITE_PERMISSION = 'group-invites-manage';

	let groups = $state<OptionGroup[]>([]);
	let friends = $state<Friend[]>([]);
	let groupIds = $state<string[]>([]);
	let userIds = $state<string[]>([]);
	let loading = $state(false);
	let error = $state('');
	let confirming = $state<Confirming | undefined>();

	const open = $derived(!!userId || openForGroup);
	const groupOptions = $derived<PickerOption[]>(
		groups.map((g) => ({ value: g.id, label: g.name, image: g.iconUrl ?? null, section: 'Groups with Invite Permission' }))
	);
	const friendOptions = $derived.by<PickerOption[]>(() => {
		const vip = sidebarData.vipIds;
		const person = (f: Friend, section: string): PickerOption => ({ value: f.userId, label: f.displayName, image: f.thumbnailImageUrl ?? null, section });
		const others = friends.filter((f) => f.userId !== userId);
		return [
			...(userId ? [{ value: userId, label: userName || userId, section: 'Selected Users' }] : []),
			...others.filter((f) => vip.has(f.userId)).map((f) => person(f, 'Favorite')),
			...others.filter((f) => !vip.has(f.userId) && f.state === 'online').map((f) => person(f, 'Online')),
			...others.filter((f) => !vip.has(f.userId) && f.state === 'active').map((f) => person(f, 'Active')),
			...others.filter((f) => !vip.has(f.userId) && f.state === 'offline').map((f) => person(f, 'Offline'))
		];
	});

	const remember = (groupId: string) => storeText(LAST_GROUP_KEY, groupId);
	const lastGroup = () => readText(LAST_GROUP_KEY);

	$effect(() => {
		if (!open) return;
		const target = userId;
		untrack(() => {
			error = '';
			userIds = target ? [target] : [];
			groupIds = [];
			loading = true;
			const account = vrchatAuth.ownerUserId;
			api.get<Friend[]>(accountPath(account, '/friends')).then((data) => (friends = data), () => (friends = []));
			api
				.get<{ groups: OptionGroup[] }>(accountPath(account, '/instances/options'))
				.then((data) => {
					groups = data.groups.filter((g) => g.permissions.includes('*') || g.permissions.includes(INVITE_PERMISSION));
					const wanted = groupId || lastGroup();
					if (wanted && groups.some((g) => g.id === wanted)) groupIds = [wanted];
				})
				.catch((err) => (error = ApiError.from(err).message))
				.finally(() => (loading = false));
		});
	});

	function pickGroup() {
		const id = groupIds[0];
		if (!id) return;
		// Only groups the account may invite to are listed, so the original's later refusal can only
		// happen when the permission went away since.
		if (!groups.some((g) => g.id === id)) {
			groupIds = [];
			toast.error('You are not allowed to invite to this group');
		}
	}

	async function invite() {
		const groupId = groupIds[0];
		if (!groupId || loading) return;
		loading = true;
		error = '';
		try {
			// One at a time, as the original does, stopping at the first refusal.
			while (userIds.length) {
				const next = userIds[0];
				await api.post(ownPath(`/groups/${encodeURIComponent(groupId)}/invites/${encodeURIComponent(next)}`));
				userIds = userIds.slice(1);
			}
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			loading = false;
		}
	}

	function close() {
		if (groupIds[0]) remember(groupIds[0]);
		userId = undefined;
		openForGroup = false;
	}
</script>

<Dialog.Root bind:open={() => open, (value) => { if (!value) close(); }}>
	<Dialog.Content class="bg-background sm:max-w-112.5">
		<Dialog.Header><Dialog.Title>{m.dialog_group_actions_invite_to_group()}</Dialog.Title></Dialog.Header>
		<span class="text-sm">{m.dialog_invite_to_group_description()}</span>
		<div class="mt-2 flex flex-col gap-4">
			<OptionPicker options={groupOptions} bind:values={groupIds} disabled={loading} placeholder={m.dialog_favorite_header()} onchange={pickGroup} />
			<OptionPicker options={friendOptions} bind:values={userIds} multiple disabled={loading} placeholder={m.dialog_invite_select_placeholder()} />
		</div>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		<Dialog.Footer>
			<Button
				disabled={loading || !userIds.length || !groupIds[0]}
				onclick={() => (confirming = { command: 'Invite', get title() { return m.dialog_invite_message_confirm(); }, get description() { return m.confirm_invite_group(); }, run: invite })}
			>
				{m.dialog_invite_invite()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<ActionDialogs bind:confirming />
