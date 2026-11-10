<script lang="ts">
	import { copyText } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { externalLink, vrcdnPreviewUrl } from '#lib/nav/external-link.svelte.js';

	// The original's OpenExternalLinkDialog: Copy, or Open (focused); Ctrl+C copies too.
	const preview = $derived(externalLink.link ? vrcdnPreviewUrl(externalLink.link) : undefined);
	let openButton = $state<HTMLButtonElement | null>(null);

	function copy() {
		const link = externalLink.link;
		externalLink.link = undefined;
		if (!link) return;
		void copyText(link, 'Link copied to clipboard!');
	}

	function onkeydown(event: KeyboardEvent) {
		if (!(event.ctrlKey || event.metaKey) || event.key.toLowerCase() !== 'c') return;
		if (window.getSelection()?.toString()) return;
		event.preventDefault();
		copy();
	}

	$effect(() => {
		if (externalLink.link && openButton) openButton.focus();
	});
</script>

<Dialog.Root bind:open={() => !!externalLink.link, (open) => { if (!open) externalLink.link = undefined; }}>
	<Dialog.Content class="sm:max-w-lg" {onkeydown}>
		<Dialog.Header class="min-w-0">
			<Dialog.Title>Open External Link</Dialog.Title>
			<Dialog.Description class="w-full min-w-0 whitespace-normal wrap-anywhere">{externalLink.link}</Dialog.Description>
			{#if preview}<p class="w-full min-w-0 text-xs whitespace-normal wrap-anywhere text-muted-foreground/70">{preview}</p>{/if}
		</Dialog.Header>
		<Dialog.Footer>
			<Button variant="outline" onclick={copy}>{m.view_favorite_copy()}</Button>
			<Button bind:ref={openButton} onclick={() => externalLink.confirm()}>{m.dialog_group_tags_open()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
