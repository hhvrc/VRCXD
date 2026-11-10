<script lang="ts">
	import { TAB_PANE } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import Pencil from '@lucide/svelte/icons/pencil';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import GroupPostMeta from '#lib/components/group/group-post-meta.svelte';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import type { Group, GroupPost } from '#lib/groups/group-dialog.svelte.js';

	// The original's GroupDialogPostsTab: every post, searchable by title and text.
	let {
		group,
		posts,
		postsError,
		search = $bindable(),
		canManagePosts,
		onedit,
		ondelete
	}: {
		group: Group;
		posts: GroupPost[] | undefined;
		postsError: string;
		search: string;
		canManagePosts: boolean;
		onedit: (post: GroupPost) => void;
		ondelete: (post: GroupPost) => void;
	} = $props();

	const filteredPosts = $derived.by(() => {
		const q = search.toLowerCase();
		return (posts ?? []).filter((p) => q === '' || p.title.toLowerCase().includes(q) || p.text.toLowerCase().includes(q));
	});
</script>

<div class="mt-2 {TAB_PANE}">
	<div class="sticky top-0 z-10 flex items-center gap-2 p-2">
		<span class="mr-2 align-top">Posts: {posts?.length ?? 0}</span>
		<ClearableInput bind:value={search} placeholder={m.dialog_group_members_search()} class="flex-1" />
	</div>
	{#if postsError}<p class="p-2 text-xs text-destructive">{postsError}</p>{/if}
	<div class="flex min-h-0 flex-1 flex-wrap items-start overflow-y-auto">
		{#each filteredPosts as entry (entry.id)}
			<div class="box-border flex w-full cursor-default items-start pb-2 text-compact">
				<div class="relative flex-1 overflow-hidden rounded-lg border border-border/60 bg-muted/60 p-2">
					{#if canManagePosts}
						<div class="absolute top-0 right-0 flex items-center">
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}<Button {...props} size="icon-sm" class="h-6 w-6 text-xs text-muted-foreground hover:text-foreground" variant="ghost" aria-label={m.dialog_group_posts_edit_tooltip()} onclick={() => onedit(entry)}><Pencil class="h-4 w-4" /></Button>{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="top">{m.dialog_group_posts_edit_tooltip()}</Tooltip.Content>
							</Tooltip.Root>
							<Tooltip.Root>
								<Tooltip.Trigger>
									{#snippet child({ props })}<Button {...props} size="icon-sm" class="h-6 w-6 text-xs text-muted-foreground hover:text-foreground" variant="ghost" aria-label={m.dialog_group_posts_delete_tooltip()} onclick={() => ondelete(entry)}><Trash2 class="h-4 w-4" /></Button>{/snippet}
								</Tooltip.Trigger>
								<Tooltip.Content side="top">{m.dialog_group_posts_delete_tooltip()}</Tooltip.Content>
							</Tooltip.Root>
						</div>
					{/if}
					<span class="block pr-15">{entry.title}</span>
					{#if entry.imageUrl}
						<div class="mr-1.5 inline-block">
							<ViewableImage src={entry.imageUrl} class="size-15 flex-none overflow-hidden rounded-md" imageClass="size-15 object-cover" />
						</div>
					{/if}
					<pre class="m-0 inline-block align-top font-[inherit] text-xs whitespace-pre-wrap">{entry.text || '-'}</pre>
					{#if entry.authorId}
						<div class="clear-both mt-1 flex w-full items-center justify-end gap-1.5 text-xs"><GroupPostMeta {entry} {group} /></div>
					{/if}
				</div>
			</div>
		{/each}
	</div>
</div>
