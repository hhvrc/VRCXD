<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import Check from '@lucide/svelte/icons/check';
	import Star from '@lucide/svelte/icons/star';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { api, ApiError } from '#lib/api/client.js';
	import { vrchatAuth, ownPath } from '#lib/api/vrchat-auth.svelte.js';
	import { toast } from 'svelte-sonner';
	import { DEFAULT_PROFILE_THEME, invertHexColor, type ProfileTheme } from '#lib/settings/profile-theme.js';

	// The star on the user, world and avatar pop-ups, and the original's ChooseFavoriteGroupDialog behind it.
	let {
		type,
		targetId,
		onlyIfFavorite = false,
		class: className = '',
		open = $bindable(false),
		showButton = true,
		theme,
		onchange
	}: {
		type: 'friend' | 'world' | 'avatar';
		targetId: string;
		/** Hides the star unless this already is a favorite, for people who aren't friends. */
		onlyIfFavorite?: boolean;
		class?: string;
		/** The dialog, for opening it without the star: the Favorites page's Edit Favorite. */
		open?: boolean;
		showButton?: boolean;
		/** The user's VRChat profile theme on the user pop-up's star, as the original's UserActionDropdown. */
		theme?: ProfileTheme;
		onchange?: () => void;
	} = $props();

	type Slot = { type: string; name: string; displayName: string; capacity: number; count: number };
	type LocalGroup = { name: string; count: number; contains: boolean };
	type Picker = { groups: Slot[]; current?: Slot | null; local: LocalGroup[]; isSupporter: boolean };

	let isFavorite = $state(false);
	let picker = $state<Picker | undefined>();
	let error = $state('');
	let busy = $state(false);
	let newGroup = $state('');

	const buttonStyle = $derived.by(() => {
		if (!theme) return undefined;
		if (isFavorite) {
			const color = theme.buttonColor === DEFAULT_PROFILE_THEME.buttonColor ? 'var(--background)' : invertHexColor(theme.buttonColor);
			return `color: ${color}; background-color: ${theme.buttonColor}`;
		}
		return `color: ${theme.iconColor === DEFAULT_PROFILE_THEME.iconColor ? 'var(--foreground)' : theme.iconColor}`;
	});

	const base = $derived(ownPath('/favorites'));
	// Local avatar favorites are a VRC+ perk in the original, for adding only.
	const canAddLocal = $derived(type !== 'avatar' || !!picker?.isSupporter);
	const localTitle = $derived(type === 'avatar' ? 'Local Favorites (Requires VRC+)' : 'Local Favorites');

	async function refreshStatus(id: string) {
		const found = await api.get<boolean>(`${base}/status`, { targetId: id }).catch(() => false);
		if (id === targetId) isFavorite = found;
	}

	$effect(() => {
		isFavorite = false;
		if (vrchatAuth.ownerUserId && targetId) refreshStatus(targetId);
	});

	async function loadPicker() {
		error = '';
		try {
			picker = await api.get<Picker>(`${base}/picker`, { type, targetId });
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	function show() {
		open = true;
	}

	$effect(() => {
		if (!open) return;
		picker = undefined;
		newGroup = '';
		untrack(loadPicker);
	});

	async function run(send: () => Promise<unknown>, close = false, done?: string) {
		busy = true;
		error = '';
		try {
			await send();
			await refreshStatus(targetId);
			onchange?.();
			if (done) toast.success(done);
			if (close) open = false;
			else await loadPicker();
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			busy = false;
		}
	}

	const addVrchat = (group: Slot) => run(() => api.post(base, { type: group.type, targetId, group: group.name }), true, 'Favorite added!');
	// The original's deleteFavoriteNoConfirm closes the dialog.
	const removeVrchat = () => run(() => api.delete(`${base}/${encodeURIComponent(targetId)}`), true);
	const addLocal = (group: string) => run(() => api.post(`${base}/local`, { type, targetId, group }));
	const removeLocal = (group: string) => run(() => api.delete(`${base}/local?type=${type}&targetId=${encodeURIComponent(targetId)}&group=${encodeURIComponent(group)}`));
</script>

{#if showButton && (!onlyIfFavorite || isFavorite)}
	<Button class="rounded-lg {className}" size="icon" variant={isFavorite ? 'default' : 'outline'} title={m.view_friends_locations_favorite()} aria-label={m.view_friends_locations_favorite()} style={buttonStyle} onclick={show}><Star /></Button>
{/if}

<Dialog.Root bind:open>
	<Dialog.Content class="bg-background sm:max-w-lg">
		<Dialog.Header><Dialog.Title>{m.dialog_favorite_header()}</Dialog.Title></Dialog.Header>
		{#if error}<p class="text-xs text-destructive">{error}</p>{/if}
		{#if !picker}
			{#if !error}<div class="flex justify-center p-4"><Spinner /></div>{/if}
		{:else}
			<div>
				<span class="block text-center text-sm">{m.dialog_favorite_vrchat_favorites()}</span>
				{#if picker.current}
					<Button variant="outline" class="my-1 h-auto min-h-9 w-full whitespace-normal px-4 py-2" disabled={busy} onclick={removeVrchat}>
						<Check />{picker.current.displayName} ({picker.current.count} / {picker.current.capacity})
					</Button>
				{:else}
					{#each picker.groups as group (group.name)}
						<Button variant="outline" class="my-1 h-auto min-h-9 w-full whitespace-normal px-4 py-2" disabled={busy} onclick={() => addVrchat(group)}>
							{group.displayName} ({group.count} / {group.capacity})
						</Button>
					{/each}
				{/if}
			</div>
			<div class="mt-5">
				<span class="block text-center text-sm">{localTitle}</span>
				{#each picker.local as group (group.name)}
					{#if group.contains}
						<Button variant="outline" class="my-1 h-auto min-h-9 w-full whitespace-normal px-4 py-2" disabled={busy} onclick={() => removeLocal(group.name)}>
							<Check />{group.name} ({group.count})
						</Button>
					{:else}
						<Button variant="outline" class="my-1 h-auto min-h-9 w-full whitespace-normal px-4 py-2" disabled={busy || !canAddLocal} onclick={() => addLocal(group.name)}>
							{group.name} ({group.count})
						</Button>
					{/if}
				{/each}
				<form
					class="my-1 flex gap-2"
					onsubmit={(event) => {
						event.preventDefault();
						const name = newGroup.trim();
						if (name) addLocal(name);
						newGroup = '';
					}}
				>
					<Input bind:value={newGroup} placeholder="New local group" class="h-9 flex-1" disabled={!canAddLocal} />
					<Button type="submit" variant="outline" class="h-9 px-4" disabled={busy || !canAddLocal || !newGroup.trim()}>Add</Button>
				</form>
			</div>
		{/if}
	</Dialog.Content>
</Dialog.Root>
