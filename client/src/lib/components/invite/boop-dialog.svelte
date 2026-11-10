<script lang="ts">
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import EmojiImage from '#lib/components/tools/emoji-image.svelte';
	import OptionPicker from '#lib/components/common/option-picker.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { emojiId, photonEmojis } from '#lib/instances/photon-emojis.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's SendBoopDialog, opened from a user's pop-up or by replying to a boop: a searchable
	// default emoji, or for VRC+ supporters one of their own uploaded emojis. Picking either replaces
	// the other, and picking the chosen one again clears it.
	let { open = $bindable(false), name, onsend }: { open?: boolean; name: string; onsend: (emojiId: string) => void } = $props();

	type EmojiFile = { id: string; url: string | null; frames: number | null; framesOverTime: number | null; loopStyle: string | null };

	let emoji = $state('');
	// The original keeps its emoji table for the session; so does this.
	let ownEmojis = $state.raw<EmojiFile[]>([]);
	let loadedFor = '';

	const options = photonEmojis.map((emojiName) => ({ value: emojiId(emojiName), label: emojiName, section: 'Default Emojis' }));
	const isVrcPlus = $derived(!!sidebarData.me?.isVrcPlus);

	$effect(() => {
		if (!open) return;
		emoji = '';
		const account = vrchatAuth.ownerUserId;
		if (!isVrcPlus || !account || loadedFor === account) return;
		loadedFor = account;
		api.get<EmojiFile[]>(accountPath(account, '/gallery/files/emoji')).then(
			(files) => (ownEmojis = files.filter((file) => file.url)),
			() => (loadedFor = '')
		);
	});
</script>

<Dialog.Root bind:open>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header>
			<Dialog.Title>{m.dialog_user_actions_send_boop()}</Dialog.Title>
			<Dialog.Description>{name}</Dialog.Description>
		</Dialog.Header>
		<OptionPicker {options} bind:values={() => (emoji ? [emoji] : []), (values) => (emoji = values[0] ?? '')} placeholder={m.dialog_boop_dialog_select_default_emoji()} />
		{#if isVrcPlus && ownEmojis.length}
			<div class="-m-1 grid max-h-[60vh] grid-cols-[repeat(auto-fill,minmax(88px,1fr))] gap-2 overflow-y-auto p-1">
				{#each ownEmojis as file (file.id)}
					<button
						type="button"
						class={['flex aspect-square cursor-pointer items-center justify-center rounded-md border p-0.5 transition-colors', file.id === emoji ? 'border-primary' : 'border-transparent hover:bg-accent']}
						aria-pressed={file.id === emoji}
						onclick={() => (emoji = file.id)}
					>
						<EmojiImage url={imageSrc(file.url!) ?? file.url!} frames={file.frames} fps={file.framesOverTime} loopStyle={file.loopStyle} class="size-full rounded-md" />
					</button>
				{/each}
			</div>
		{/if}
		<Dialog.Footer>
			<Button variant="secondary" size="sm" class="mr-2" onclick={() => (open = false)}>{m.dialog_boop_dialog_cancel()}</Button>
			<Button
				size="sm"
				onclick={() => {
					open = false;
					onsend(emoji);
				}}>{m.dialog_boop_dialog_send()}</Button
			>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
