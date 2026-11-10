<script lang="ts">
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { getServerToken } from '#lib/api/client.js';
	import { serverAuth } from '#lib/api/server-auth.svelte.js';
	import { errorReporting } from '#lib/settings/error-reporting.svelte.js';

	// The original's error reporting consent and restart confirms, and its start-up check, once the
	// server can be asked whether it has an address to report to.
	$effect(() => {
		if (!serverAuth.needsToken && getServerToken()) errorReporting.start();
	});

	const prompt = $derived(errorReporting.prompt);
</script>

<Dialog.Root open={!!prompt} onOpenChange={(open) => { if (!open) prompt?.resolve(false); }}>
	<Dialog.Content class="sm:max-w-md">
		{#if prompt}
			<Dialog.Header>
				<Dialog.Title>{prompt.title}</Dialog.Title>
				<Dialog.Description class="whitespace-pre-line">{prompt.description}</Dialog.Description>
			</Dialog.Header>
			<Dialog.Footer>
				<Button variant="outline" onclick={() => prompt.resolve(false)}>{prompt.cancelText}</Button>
				<Button onclick={() => prompt.resolve(true)}>{prompt.confirmText}</Button>
			</Dialog.Footer>
		{/if}
	</Dialog.Content>
</Dialog.Root>
