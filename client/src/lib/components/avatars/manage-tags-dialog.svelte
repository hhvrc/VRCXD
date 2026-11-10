<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import X from '@lucide/svelte/icons/x';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import type { OwnAvatarTag } from '#lib/api/types.js';
	import { TAG_COLORS, colorForTagName, resolveTagColor, type TagColor } from '#lib/content/tag-colors.js';

	// The original's ManageTagsDialog.vue: a tags input where each chip opens a palette, comma
	// or Enter adds a tag, and Save hands the whole set back.

	let {
		open = $bindable(false),
		avatarName = '',
		avatarId = '',
		initialTags = [],
		onsave
	}: { open?: boolean; avatarName?: string; avatarId?: string; initialTags?: OwnAvatarTag[]; onsave: (avatarId: string, tags: OwnAvatarTag[]) => void } = $props();

	let entries = $state<OwnAvatarTag[]>([]);
	let draft = $state('');

	$effect(() => {
		if (open) {
			entries = initialTags.map((e) => ({ ...e }));
			draft = '';
		}
	});

	function addDraft() {
		const names = draft.split(',').map((s) => s.trim()).filter(Boolean);
		for (const name of names) if (!entries.some((e) => e.tag === name)) entries = [...entries, { tag: name, color: null }];
		draft = '';
	}

	function remove(tag: string) {
		entries = entries.filter((e) => e.tag !== tag);
	}

	function isSelected(entry: OwnAvatarTag, color: TagColor): boolean {
		return entry.color ? entry.color === color.name : colorForTagName(entry.tag).name === color.name;
	}

	function setColor(entry: OwnAvatarTag, color: TagColor) {
		const hashed = colorForTagName(entry.tag);
		entries = entries.map((e) => (e.tag === entry.tag ? { ...e, color: hashed.name === color.name ? null : color.name } : e));
	}

	function save() {
		addDraft();
		onsave(avatarId, entries.map((e) => ({ tag: e.tag, color: e.color })));
		open = false;
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="sm:max-w-110">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_avatar_actions_manage_tags()}</Dialog.Title>
			<Dialog.Description>{avatarName}</Dialog.Description>
		</Dialog.Header>
		<p class="text-xs text-muted-foreground">{m.dialog_avatar_actions_manage_tags_hint()}</p>
		<div class="mt-1 flex min-h-9 flex-wrap items-center gap-1 rounded-lg border border-input px-2 py-1">
			{#each entries as entry (entry.tag)}
				{@const color = resolveTagColor(entry.tag, entry.color ?? '')}
				<Popover.Root>
					<Popover.Trigger>
						{#snippet child({ props })}
							<span {...props} class="inline-flex cursor-pointer items-center gap-1 rounded-md px-2 py-0.5 text-xs" style="background-color: {color.bg}; color: {color.text}">
								{entry.tag}
								<button
									type="button"
									class="rounded-sm opacity-70 hover:opacity-100"
									aria-label="Remove {entry.tag}"
									onclick={(e) => {
										e.stopPropagation();
										remove(entry.tag);
									}}><X class="size-3" /></button
								>
							</span>
						{/snippet}
					</Popover.Trigger>
					<Popover.Content class="w-auto p-2" side="top" sideOffset={8}>
						<div class="flex items-center gap-1.5">
							{#each TAG_COLORS as c (c.name)}
								<button
									type="button"
									class="h-4 w-4 shrink-0 cursor-pointer rounded-sm transition-transform hover:scale-125 {isSelected(entry, c) ? 'ring-1 ring-ring ring-offset-1 ring-offset-background' : ''}"
									style="background-color: {c.bg.replace('/ 0.2)', '/ 1)')}"
									title={c.label}
									onclick={() => setColor(entry, c)}
								></button>
							{/each}
						</div>
					</Popover.Content>
				</Popover.Root>
			{/each}
			<input
				class="min-w-24 flex-1 bg-transparent px-1 text-sm outline-none"
				placeholder={m.dialog_avatar_actions_manage_tags_placeholder()}
				bind:value={draft}
				onkeydown={(e) => {
					if (e.key === 'Enter' || e.key === ',') {
						e.preventDefault();
						addDraft();
					}
				}}
				onblur={addDraft}
			/>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button onclick={save}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
