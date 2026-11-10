<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import * as Tabs from '#lib/components/ui/tabs/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import type { OwnAvatar } from '#lib/api/types.js';
	import { replaceBioSymbols } from '#lib/helpers/format.js';
	import { csvField, findDiscordName, type FriendExportRow } from '#lib/nav/tools.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's three read-only exports: ExportFriendsListDialog, ExportAvatarsListDialog
	// and ExportDiscordNamesDialog. `tool` picks which one is open.

	let { tool = $bindable(null) }: { tool?: 'export-friend-list' | 'export-own-avatars' | 'discord-names' | null } = $props();

	let friends = $state<FriendExportRow[]>([]);
	let avatars = $state<OwnAvatar[]>([]);
	let publicOnly = $state(false);
	let error = $state('');
	let loadingProfiles = $state(false);
	let loaded = $state(0);

	// The friends list from VRChat carries no bios, so a friend's bio is only known once their
	// profile has been fetched. Null means never fetched; the original's Friends List tab had
	// the same "load missing entries" pass.
	const missingProfiles = $derived(friends.filter((f) => f.bio == null));

	async function loadMissingProfiles() {
		loadingProfiles = true;
		loaded = 0;
		for (const friend of missingProfiles) {
			if (!loadingProfiles) break;
			try {
				const user = await api.get<{ bio?: string | null }>(`/1/users/${encodeURIComponent(friend.userId)}`, { refresh: true, account: vrchatAuth.ownerUserId });
				friend.bio = user.bio ?? '';
			} catch (err) {
				error = ApiError.from(err).message;
			}
			loaded++;
		}
		loadingProfiles = false;
	}

	const friendsCsv = $derived(
		['UserID,DisplayName,Memo', ...friends.map((f) => `${csvField(f.userId)},${csvField(f.displayName)},${csvField((f.memo ?? '').replace(/\n/g, ' '))}`)].join('\n')
	);
	const friendsJson = $derived(JSON.stringify({ friends: friends.map((f) => f.userId) }, null, 4));
	const avatarsCsv = $derived(
		[
			'AvatarID,AvatarName',
			...avatars.filter((a) => !publicOnly || a.avatar.releaseStatus === 'public').map((a) => `${csvField(a.avatar.id)},${csvField(a.avatar.name)}`)
		].join('\n')
	);
	const discordCsv = $derived.by(() => {
		const lines = ['DisplayName,DiscordName'];
		for (const friend of friends) {
			const discord = (findDiscordName(friend.statusDescription) || findDiscordName(replaceBioSymbols(friend.bio))).trim();
			if (discord) lines.push(`${csvField(friend.displayName)},${csvField(discord)}`);
		}
		return lines.join('\n');
	});

	async function load(which: NonNullable<typeof tool>) {
		error = '';
		try {
			if (which === 'export-own-avatars') avatars = await api.get<OwnAvatar[]>(ownPath('/avatars'));
			else friends = await api.get<FriendExportRow[]>(ownPath('/tools/friends'));
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	$effect(() => {
		const which = tool;
		if (which) untrack(() => load(which));
		else loadingProfiles = false;
	});

	const selectAll = (e: MouseEvent) => (e.currentTarget as HTMLTextAreaElement).select();
	const fieldClass = 'mt-2 h-80 w-full resize-none rounded-lg border border-input bg-transparent p-2 font-mono text-xs outline-none';
</script>

<Dialog.Root open={tool !== null} onOpenChange={(o) => !o && (tool = null)}>
	<Dialog.Content class="sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>
				{tool === 'export-friend-list' ? 'Export Friends List' : tool === 'export-own-avatars' ? 'Export Own Avatars' : 'Discord Names'}
			</Dialog.Title>
		</Dialog.Header>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		{#if tool === 'export-friend-list'}
			<Tabs.Root value="csv">
				<Tabs.List>
					<Tabs.Trigger value="csv">{m.dialog_export_friends_list_csv()}</Tabs.Trigger>
					<Tabs.Trigger value="json">{m.dialog_user_json_header()}</Tabs.Trigger>
				</Tabs.List>
				<Tabs.Content value="csv"><textarea readonly class={fieldClass} value={friendsCsv} onclick={selectAll}></textarea></Tabs.Content>
				<Tabs.Content value="json"><textarea readonly class={fieldClass} value={friendsJson} onclick={selectAll}></textarea></Tabs.Content>
			</Tabs.Root>
		{:else if tool === 'export-own-avatars'}
			<div class="flex items-center justify-between text-xs">
				<span class="mr-6">Public Only</span>
				<Switch bind:checked={publicOnly} />
			</div>
			<textarea readonly class={fieldClass} value={avatarsCsv} onclick={selectAll}></textarea>
		{:else if tool === 'discord-names'}
			<div class="flex items-center gap-2 text-xs">
				<span class="flex-1">{missingProfiles.length} friends have not had their profile loaded yet, so their bios were not searched.</span>
				{#if loadingProfiles}
					<span class="flex items-center gap-2"><Spinner />{loaded}</span>
					<Button size="sm" variant="outline" onclick={() => (loadingProfiles = false)}>{m.dialog_alertdialog_cancel()}</Button>
				{:else}
					<Button size="sm" variant="outline" disabled={missingProfiles.length === 0} onclick={loadMissingProfiles}>{m.view_friend_list_load()}</Button>
				{/if}
			</div>
			<textarea readonly class={fieldClass} value={discordCsv} onclick={selectAll}></textarea>
		{/if}
	</Dialog.Content>
</Dialog.Root>
