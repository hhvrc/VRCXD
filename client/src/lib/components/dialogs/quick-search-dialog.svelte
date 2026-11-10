<script lang="ts">
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { goto } from '$app/navigation';
	import { untrack } from 'svelte';
	import Globe from '@lucide/svelte/icons/globe';
	import Image from '@lucide/svelte/icons/image';
	import Users from '@lucide/svelte/icons/users';
	import * as Command from '#lib/components/ui/command/index.js';
	import { avatarHref, groupHref, imageSrc, worldHref } from '#lib/api/links.js';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { nameColour } from '#lib/people/user-colour.js';
	import { quickSearch, type QuickItem } from '#lib/nav/quick-search.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's QuickSearchDialog, opened with Ctrl+K or the sidebar's search box.
	type Kind = 'avatar' | 'world' | 'group';
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);

	function select(kind: 'friend' | Kind, id: string) {
		quickSearch.open = false;
		quickSearch.query = '';
		if (kind === 'friend') userDialog.open(id);
		else goto(kind === 'avatar' ? avatarHref(id) : kind === 'world' ? worldHref(id) : groupHref(id));
	}

	function onkeydown(event: KeyboardEvent) {
		if (!vrchatAuth.ownerUserId) return;
		// The original listens for both Ctrl+K and ⌘K on every platform.
		if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
			event.preventDefault();
			if (quickSearch.open) quickSearch.open = false;
			else quickSearch.show();
		}
	}

	const hints = [
		{ icon: Users, get label() { return m.side_panel_search_friends(); }, scope: 'Name, memo & note' },
		{ icon: Image, get label() { return m.side_panel_search_avatars(); }, scope: 'Own & favorites' },
		{ icon: Globe, get label() { return m.side_panel_search_worlds(); }, scope: 'Own & favorites' },
		{ icon: Users, get label() { return m.side_panel_search_groups(); }, scope: 'Own & joined' }
	];
	const results = $derived(quickSearch.results);
	const friendColour = (id: string) => {
		const friend = sidebarData.friends.find((f) => f.userId === id);
		return friend ? nameColour(friend, appearance.isDarkMode) : undefined;
	};

	// As the original: while open, changes to friends, names and content refresh the index (200 ms
	// debounce); closing clears the text; another account starts from nothing.
	$effect(() => {
		if (!quickSearch.open) {
			untrack(() => (quickSearch.query = ''));
			return;
		}
		let timer: ReturnType<typeof setTimeout> | undefined;
		const unsubscribe = onLiveEvent((event) => {
			const kind = event.event.case;
			if (kind !== 'friendship' && kind !== 'user' && kind !== 'content') return;
			clearTimeout(timer);
			timer = setTimeout(() => quickSearch.open && quickSearch.load(), 200);
		});
		return () => {
			clearTimeout(timer);
			unsubscribe();
		};
	});

	let shownFor = vrchatAuth.ownerUserId;
	$effect(() => {
		const account = vrchatAuth.ownerUserId;
		if (account === shownFor) return;
		shownFor = account;
		untrack(() => {
			quickSearch.clear();
			if (quickSearch.open) quickSearch.load();
		});
	});
</script>

<svelte:window {onkeydown} />

{#snippet group(heading: string, items: QuickItem[], kind: Kind, tag: string)}
	{#if items.length}
		<Command.Group {heading}>
			{#each items as item (item.id)}
				<Command.Item value={`${item.name} ${tag} ${item.id}`} class="gap-3 py-2 text-[15px]" onSelect={() => select(kind, item.id)}>
					{#if item.imageUrl}
						<img src={imageSrc(item.imageUrl)} alt="" class="size-6 rounded object-cover" loading="lazy" />
					{:else if kind === 'avatar'}
						<Image class="size-4" />
					{:else if kind === 'world'}
						<Globe class="size-4" />
					{:else}
						<Users class="size-4" />
					{/if}
					<span class="truncate">{item.name}</span>
				</Command.Item>
			{/each}
		</Command.Group>
	{/if}
{/snippet}

<Command.Dialog
	bind:open={() => quickSearch.open, (open) => { quickSearch.open = open; if (!open) quickSearch.query = ''; }}
	title={m.side_panel_search_placeholder()}
	description={m.side_panel_search_placeholder()}
	shouldFilter={false}
	class="sm:max-w-2xl"
>
	<Command.Input placeholder={m.side_panel_search_placeholder()} bind:value={quickSearch.query} />
	<Command.List class="max-h-[min(400px,50vh)] overflow-x-hidden overflow-y-auto">
		{#if quickSearch.query.length < 2}
			<Command.Group heading={m.side_panel_search_categories()}>
				{#each hints as hint (hint.label)}
					<Command.Item value={`hint-${hint.label}`} disabled class="gap-3 py-2 text-[15px] opacity-70">
						<hint.icon class="size-4" />
						<span class="flex-1">{hint.label}</span>
						<span class="text-xs text-muted-foreground">{hint.scope}</span>
					</Command.Item>
				{/each}
			</Command.Group>
		{:else}
			{#if !quickSearch.hasResults}
				<div class="{EMPTY_STATE} text-sm">{m.dialog_screenshot_metadata_no_results()}</div>
			{/if}
			{#if results.friends.length}
				<Command.Group heading={m.dialog_group_tags_friends()}>
					{#each results.friends as item (item.id)}
						<Command.Item value={[item.name, item.memo, item.note, item.id].filter(Boolean).join(' ')} class="gap-3 py-2 text-[15px]" onSelect={() => select('friend', item.id)}>
							{#if item.imageUrl}
								<img src={imageSrc(item.imageUrl)} alt="" class="size-6 rounded-full object-cover" loading="lazy" />
							{:else}
								<Users class="size-4" />
							{/if}
							<div class="flex min-w-0 flex-col">
								<span class="truncate" style:color={friendColour(item.id)}>{item.name}</span>
								{#if item.matchedField !== 'name' && item.memo}<span class="truncate text-xs text-muted-foreground">Memo: {item.memo}</span>{/if}
								{#if item.matchedField !== 'name' && item.note}<span class="truncate text-xs text-muted-foreground">Note: {item.note}</span>{/if}
							</div>
						</Command.Item>
					{/each}
				</Command.Group>
			{/if}
			{@render group('Own Avatars', results.ownAvatars, 'avatar', 'own')}
			{@render group('Favorite Avatars', results.favAvatars, 'avatar', 'fav')}
			{@render group('Own Worlds', results.ownWorlds, 'world', 'own')}
			{@render group('Favorite Worlds', results.favWorlds, 'world', 'fav')}
			{@render group('Own Groups', results.ownGroups, 'group', 'own')}
			{@render group('Joined Groups', results.joinedGroups, 'group', 'joined')}
		{/if}
	</Command.List>
</Command.Dialog>
