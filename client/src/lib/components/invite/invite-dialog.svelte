<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import InviteMessageDialog from '#lib/components/invite/invite-message-dialog.svelte';
	import Location from '#lib/components/instance/location.svelte';
	import OptionPicker, { type PickerOption } from '#lib/components/common/option-picker.svelte';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { instanceDialogs, selfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { toast } from 'svelte-sonner';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's InviteDialog. Invite With Message opens the shared message picker, which can
	// attach a picture for VRC+ supporters as the original's SendInviteDialog does.
	type Friend = { userId: string; displayName: string; state: string; location?: string | null; thumbnailImageUrl?: string | null };
	type FavoriteList = { groups: { name: string; displayName: string }[]; favorites: { targetId: string; groupName?: string | null }[] };
	type LocalFavorite = { groupName: string; targetId: string };

	let friends = $state<Friend[]>([]);
	let ownName = $state('');
	let currentLocation = $state<string | null>(null);
	let worldName = $state('');
	let userIds = $state<string[]>([]);
	let groups = $state<{ key: string; name: string; ids: string[] }[]>([]);
	let messagesOpen = $state(false);
	let busy = $state(false);
	let error = $state('');
	let confirming = $state<Confirming | undefined>();

	const location = $derived(instanceDialogs.invite?.location ?? '');
	const owner = $derived(vrchatAuth.ownerUserId);
	const online = $derived(friends.filter((f) => f.state === 'online'));
	const inInstance = $derived(currentLocation ? friends.filter((f) => f.location === currentLocation) : []);
	const vip = $derived(sidebarData.vipIds);
	const onlineIds = $derived(new Set(online.map((f) => f.userId)));
	const addableGroups = $derived(groups.filter((g) => g.ids.some((id) => onlineIds.has(id))));
	const options = $derived<PickerOption[]>([
		{ value: owner, label: ownName || owner, section: 'Me', image: null },
		...inInstance.map((f) => person(f, 'Friends In Instance')),
		...online.filter((f) => !inInstance.includes(f) && vip.has(f.userId)).map((f) => person(f, 'FAVORITES')),
		...online.filter((f) => !inInstance.includes(f) && !vip.has(f.userId)).map((f) => person(f, 'Online')),
		...friends.filter((f) => f.state === 'active' && !inInstance.includes(f)).map((f) => person(f, 'Active'))
	]);

	function person(friend: Friend, section: string): PickerOption {
		return { value: friend.userId, label: friend.displayName, section, image: friend.thumbnailImageUrl ?? null };
	}

	$effect(() => {
		const at = location;
		userIds = [];
		messagesOpen = false;
		error = '';
		if (!at) return;
		const { worldId } = parseLocation(at);
		const account = owner;
		api.get<Friend[]>(accountPath(account, '/friends')).then((data) => (friends = data), () => (friends = []));
		api.get<{ displayName: string; currentLocation?: string | null }>(accountPath(account, '/instances/options')).then(
			(data) => {
				ownName = data.displayName;
				currentLocation = data.currentLocation ?? null;
			},
			() => (currentLocation = null)
		);
		api.get<{ name: string }>(accountPath(account, `/worlds/${encodeURIComponent(worldId)}/live`)).then((world) => (worldName = world?.name ?? ''), () => (worldName = ''));
		Promise.all([
			api.get<FavoriteList>(accountPath(account, '/favorites'), { type: 'friend' }).catch((): FavoriteList => ({ groups: [], favorites: [] })),
			api.get<LocalFavorite[]>(accountPath(account, '/favorites/local'), { type: 'friend' }).catch((): LocalFavorite[] => [])
		]).then(([remote, local]) => {
			const remoteGroups = remote.groups.map((g) => ({ key: g.name, name: g.displayName, ids: remote.favorites.filter((f) => f.groupName === g.name).map((f) => f.targetId) }));
			const localGroups = [...new Set(local.map((f) => f.groupName))].map((name) => ({ key: `local:${name}`, name, ids: local.filter((f) => f.groupName === name).map((f) => f.targetId) }));
			groups = [...remoteGroups, ...localGroups];
		});
	});

	function add(ids: string[]) {
		userIds = [...new Set([...userIds, ...ids])];
	}

	async function send(messageSlot?: number, photo?: Blob | null) {
		busy = true;
		error = '';
		let failed = 0;
		for (const userId of userIds) {
			try {
				if (userId === owner) await selfInvite(location);
				else if (photo) {
					const form = new FormData();
					form.append('image', photo, 'image.png');
					form.append('location', location);
					form.append('worldName', worldName);
					if (messageSlot !== undefined) form.append('messageSlot', String(messageSlot));
					await api.upload(accountPath(owner, `/invites/${encodeURIComponent(userId)}/photo`), form);
				} else await api.post(accountPath(owner, `/invites/${encodeURIComponent(userId)}`), { location, worldName, messageSlot: messageSlot ?? null });
			} catch (err) {
				failed++;
				error = ApiError.from(err).message;
			}
		}
		busy = false;
		confirming = undefined;
		if (failed) {
			error = `${failed} of ${userIds.length} invites failed: ${error}`;
			throw new Error(error);
		}
		instanceDialogs.invite = undefined;
	}

</script>

<Dialog.Root bind:open={() => !!instanceDialogs.invite, (open) => { if (!open) instanceDialogs.invite = undefined; }}>
	<Dialog.Content class="bg-background sm:max-w-125" onOpenAutoFocus={(event) => event.preventDefault()}>
		<Dialog.Header><Dialog.Title>{m.dialog_invite_invite()}</Dialog.Title></Dialog.Header>
		<div class="overflow-hidden">
			<Location {location} hint={worldName} link={false} disableTooltip class="cursor-default" />
			<br />
			<Button size="sm" class="mt-2 mr-2" variant="outline" onclick={() => add([owner])}>{m.dialog_invite_add_self()}</Button>
			<Button size="sm" class="mt-2 mr-2" variant="outline" disabled={!inInstance.length} onclick={() => add(inInstance.map((f) => f.userId))}>{m.dialog_invite_add_friends_in_instance()}</Button>
			<DropdownMenu.Root>
				<DropdownMenu.Trigger disabled={!addableGroups.length}>
					{#snippet child({ props })}
						<Button {...props} size="sm" class="mt-2" variant="outline">{m.dialog_invite_add_favorite_friends()}</Button>
					{/snippet}
				</DropdownMenu.Trigger>
				<DropdownMenu.Content align="start" class="w-48">
					{#each addableGroups as group, i (group.key)}
						{#if i > 0 && group.key.startsWith('local:') && !addableGroups[i - 1].key.startsWith('local:')}<DropdownMenu.Separator />{/if}
						<DropdownMenu.Item closeOnSelect={false} onSelect={() => add(group.ids.filter((id) => onlineIds.has(id)))}>{group.name}</DropdownMenu.Item>
					{/each}
				</DropdownMenu.Content>
			</DropdownMenu.Root>
			<div class="mt-4 w-full">
				<OptionPicker {options} bind:values={userIds} multiple disabled={busy} placeholder={m.dialog_invite_select_placeholder()} />
			</div>
		</div>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" disabled={busy || !userIds.length} onclick={() => (messagesOpen = true)}>{m.dialog_invite_invite_with_message()}</Button>
			<Button disabled={busy || !userIds.length} onclick={() => (confirming = { get title() { return m.dialog_invite_message_confirm(); }, get description() { return m.confirm_invite(); }, run: () => send().then(() => toast.success(m.message_invite_sent()), () => {}) })}>{m.dialog_invite_invite()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<InviteMessageDialog bind:open={messagesOpen} type="message" send={(slot, photo) => send(slot, photo)} />

<ActionDialogs bind:confirming {busy} />
