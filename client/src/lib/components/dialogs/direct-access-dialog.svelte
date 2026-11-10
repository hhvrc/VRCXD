<script lang="ts">
	import { SMALL_DIALOG } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { directAccess } from '#lib/nav/direct-access.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Direct Access prompt and its Ctrl/Cmd+D hotkey.
	const isMac = typeof navigator !== 'undefined' && /mac/i.test(navigator.platform);
	let invalid = $state(false);

	function onkeydown(event: KeyboardEvent) {
		if (!vrchatAuth.ownerUserId) return;
		if ((isMac ? event.metaKey : event.ctrlKey) && !event.shiftKey && !event.altKey && event.key.toLowerCase() === 'd') {
			event.preventDefault();
			void directAccess.paste();
		}
	}

	function submit() {
		const value = (directAccess.prompt ?? '').trim();
		if (!/\S+/.test(value)) {
			invalid = true;
			return;
		}
		invalid = false;
		directAccess.submit(value);
	}
</script>

<svelte:window {onkeydown} />

<Dialog.Root bind:open={() => directAccess.prompt !== undefined, (open) => { if (!open) { directAccess.prompt = undefined; invalid = false; } }}>
	<Dialog.Content class={SMALL_DIALOG} showCloseButton={false}>
		<Dialog.Header><Dialog.Title>{m.prompt_direct_access_omni_header()}</Dialog.Title></Dialog.Header>
		<p class="text-sm text-muted-foreground">{m.prompt_direct_access_omni_description()}</p>
		{#if directAccess.prompt !== undefined}
			<form onsubmit={(e) => { e.preventDefault(); submit(); }}>
				<Input bind:value={directAccess.prompt} />
			</form>
		{/if}
		{#if invalid}<p class="text-xs text-destructive">{m.prompt_direct_access_omni_input_error()}</p>{/if}
		<Dialog.Footer>
			<Button variant="secondary" class="mr-2" onclick={() => (directAccess.prompt = undefined)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button onclick={submit}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
