<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { browser } from '$app/env';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import { toast } from 'svelte-sonner';
	import { untrack } from 'svelte';
	import { accountPath, api } from '#lib/api/client.js';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { directAccessParse } from '#lib/nav/direct-access.svelte.js';
	import { launchHandoff } from '#lib/instances/launch-handoff.svelte.js';
	import { parseLaunchCommand, SWITCH_AVATAR_CONFIRM_KEY } from '#lib/instances/launch-command.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's vrcx:// launch commands (eventLaunchCommand): /launch?command=world/wrld_...
	// opens what the link names, adds local favorites, adds an avatar search provider, switches
	// avatar (asking first when the Advanced setting says so) or starts a favorites import. It
	// waits for the VRChat account, as the original ignores commands until logged in.
	const raw = $derived(browser ? (page.url.searchParams.get('command') ?? '') : '');
	let confirming = $state<Confirming | undefined>();
	let ran = false;

	const PROVIDERS_KEY = 'VRCX_avatarRemoteDatabaseProviderList';
	const DEFAULT_PROVIDERS = ['https://api.avtrdb.com/v3/avatar/search/vrcx'];
	const FAVORITE_PAGES = { avatar: '/favorite-avatars', world: '/favorite-worlds', friend: '/favorite-friends' } as const;

	async function selectAvatar(account: string, avatarId: string) {
		try {
			await api.post(accountPath(account, `/avatars/${encodeURIComponent(avatarId)}/select`), {});
			toast.success('Avatar changed via launch command');
		} catch (err) {
			toastError(err);
		}
	}

	async function run(account: string) {
		const command = parseLaunchCommand(raw);
		switch (command.kind) {
			case 'world':
				await goto('/', { replaceState: true });
				// The fallback is for mangled world ids, as the original's.
				if (!directAccessParse(command.input)) userDialog.openWorld(command.worldId);
				return;
			case 'avatar':
				await goto('/', { replaceState: true });
				userDialog.openAvatar(command.avatarId);
				return;
			case 'user':
				await goto('/', { replaceState: true });
				userDialog.open(command.userId);
				return;
			case 'group':
				await goto('/', { replaceState: true });
				userDialog.openGroup(command.groupId);
				return;
			case 'local-favorite':
				await goto('/', { replaceState: true });
				try {
					await api.post(accountPath(account, '/favorites/local'), { type: command.type, targetId: command.targetId, group: command.group });
					toast.success(`Added to ${command.group}`);
				} catch (err) {
					toastError(err);
				}
				if (command.type === 'World') userDialog.openWorld(command.targetId);
				else userDialog.openAvatar(command.targetId);
				return;
			case 'add-avatar-provider': {
				const providers = readSetting<string[]>(PROVIDERS_KEY, DEFAULT_PROVIDERS);
				if (!providers.includes(command.url)) storeSetting(PROVIDERS_KEY, [...providers, command.url]);
				launchHandoff.showProviders = true;
				await goto('/search', { replaceState: true });
				return;
			}
			case 'switch-avatar':
				if (readSetting(SWITCH_AVATAR_CONFIRM_KEY, false)) {
					confirming = { get title() { return m.confirm_title(); }, get description() { return m.confirm_select_avatar(); }, run: () => selectAvatar(account, command.avatarId) };
					return;
				}
				await goto('/', { replaceState: true });
				await selectAvatar(account, command.avatarId);
				return;
			case 'import':
				launchHandoff.import = { type: command.type, text: command.data };
				await goto(FAVORITE_PAGES[command.type], { replaceState: true });
				return;
			case 'invalid':
				toast.error(command.message);
				await goto('/', { replaceState: true });
				return;
			case 'unknown':
				await goto('/', { replaceState: true });
		}
	}

	$effect(() => {
		const account = vrchatAuth.ownerUserId;
		if (!account || !raw || ran) return;
		ran = true;
		untrack(() => run(account));
	});

	// The confirmation, answered either way, ends at the start page.
	let asked = false;
	$effect(() => {
		if (confirming) asked = true;
		else if (asked) untrack(() => goto('/', { replaceState: true }));
	});
</script>

<svelte:head><title>Launch · VRCXD</title></svelte:head>

<div class="p-6 text-sm text-muted-foreground">{raw ? (vrchatAuth.ownerUserId ? 'Running launch command…' : 'Waiting for VRChat login…') : 'No launch command given.'}</div>

<ActionDialogs bind:confirming />
