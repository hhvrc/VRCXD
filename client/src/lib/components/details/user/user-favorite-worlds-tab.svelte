<script lang="ts">
	import { PERSON_NAME, PERSON_SUBTEXT, NONE, ROW, TAB_PANE } from '#lib/details.js';
	import { imageSrc, worldHref } from '#lib/api/links.js';
	import ImageIcon from '@lucide/svelte/icons/image';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import type { UserTabs } from './user-tabs.svelte.js';

	// The original's UserDialogFavoriteWorldsTab: the user's public favorite world lists, one at a time.
	let { tabs }: { tabs: UserTabs } = $props();

	const list = $derived(tabs.favorites?.lists[Number(tabs.favoriteList)]);
</script>

<div class="overflow-hidden {TAB_PANE}">
	{#if tabs.favorites?.loading}
		<div class="flex h-24 items-center justify-center"><Spinner /></div>
	{:else if tabs.favorites?.error}
		<p class="p-1.5 text-sm text-destructive">{tabs.favorites.error}</p>
	{:else if !tabs.favorites?.lists.length}
		<p class="p-1.5 text-xs text-muted-foreground">{NONE}</p>
	{:else}
		<div class="flex shrink-0 border-b border-border">
			{#each tabs.favorites.lists as favorite, index (favorite.name + index)}
				<button
					type="button"
					class="relative flex min-w-0 flex-1 cursor-pointer flex-col items-center px-1 py-1.5 leading-tight {String(index) === tabs.favoriteList ? 'text-primary' : 'text-muted-foreground hover:text-foreground'}"
					onclick={() => (tabs.favoriteList = String(index))}
				>
					<span class="max-w-full truncate text-sm font-bold">{favorite.name}</span>
					<span class="inline-flex items-center justify-center text-3xs text-muted-foreground">
						<i class="x-status-icon mr-1 shrink-0 {favorite.visibility === 'public' ? 'green' : favorite.visibility === 'friends' ? 'blue' : 'red'}"></i>
						{favorite.worlds.length}
					</span>
					{#if String(index) === tabs.favoriteList}<span class="absolute inset-x-0 bottom-0 h-0.5 rounded-full bg-primary"></span>{/if}
				</button>
			{/each}
		</div>
		<div class="min-h-0 flex-1 overflow-auto">
			<div class="flex flex-wrap items-start pt-2 pb-4">
				{#each list?.worlds ?? [] as world, index (world.id + index)}
					<a class={ROW} href={worldHref(world.id)}>
						<div class="relative mr-2.5 inline-block size-9 flex-none">
							<Avatar.Root class="size-9">
								<Avatar.Image src={imageSrc(world.thumbnailImageUrl) ?? undefined} class="object-cover" />
								<Avatar.Fallback><ImageIcon class="size-4 text-muted-foreground" /></Avatar.Fallback>
							</Avatar.Root>
						</div>
						<div class="flex-1 overflow-hidden">
							<span class={PERSON_NAME}>{world.name}</span>
							{#if world.occupants}<span class={PERSON_SUBTEXT}>({world.occupants})</span>{/if}
						</div>
					</a>
				{/each}
			</div>
		</div>
	{/if}
</div>
