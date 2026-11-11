<script lang="ts">
	import X from '@lucide/svelte/icons/x';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { m } from '#lib/paraglide/messages.js';

	// The original's ChatboxBlacklistDialog: the keywords that hide a chatbox message, and the users
	// whose messages are hidden.
	let {
		open = $bindable(false),
		keywords,
		users,
		onKeywords,
		onRemoveUser
	}: {
		open?: boolean;
		keywords: string[];
		users: Record<string, string>;
		onKeywords: (keywords: string[]) => void;
		onRemoveUser: (userId: string) => void;
	} = $props();

	let draft = $state<string[]>([]);
	$effect(() => {
		if (open) draft = [...keywords];
	});
	const save = () => onKeywords(draft);
</script>

<Dialog.Root bind:open>
	<Dialog.Content>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_chatbox_blacklist_header()}</Dialog.Title>
		</Dialog.Header>
		<h2 class="font-semibold">{m.dialog_chatbox_blacklist_keyword_blacklist()}</h2>
		{#each draft as _, index (index)}
			<div class="mt-1.5 flex gap-1">
				<Input class="h-8" bind:value={draft[index]} onchange={save} />
				<Button
					size="sm"
					variant="outline"
					aria-label="Remove"
					onclick={() => {
						draft.splice(index, 1);
						save();
					}}><Trash2 class="h-4 w-4" /></Button>
			</div>
		{/each}
		<Button class="mt-1.5 w-fit" size="sm" variant="outline" onclick={() => draft.push('')}>{m.dialog_chatbox_blacklist_add_item()}</Button>
		<h2 class="mt-2 font-semibold">{m.dialog_chatbox_blacklist_user_blacklist()}</h2>
		<div class="flex flex-wrap">
			{#each Object.entries(users) as [userId, name] (userId)}
				<Badge class="mt-1.5 mr-1.5" variant="outline">
					<span>{name}</span>
					<button type="button" class="ml-2 inline-flex cursor-pointer items-center" aria-label="Remove" onclick={() => onRemoveUser(userId)}><X class="h-3 w-3" /></button>
				</Badge>
			{/each}
		</div>
	</Dialog.Content>
</Dialog.Root>
