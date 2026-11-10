<script lang="ts">
	import { onMount } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import { api, ApiError, getServerToken, setServerToken } from '#lib/api/client.js';
	import { serverAuth } from '#lib/api/server-auth.svelte.js';

	// Asks for the server's API token (VRCX:Auth:Token) when there is none, or when the
	// server rejected the stored one. The token is tried against the server before it's kept.

	let token = $state('');
	let error = $state('');
	let checking = $state(false);

	onMount(() => {
		if (!getServerToken()) {
			serverAuth.needsToken = true;
		}
	});

	async function submit(event: SubmitEvent) {
		event.preventDefault();
		const previous = getServerToken();
		checking = true;
		error = '';
		setServerToken(token.trim());
		try {
			await api.get('/1/accounts');
			serverAuth.needsToken = false;
			token = '';
			// Pages loaded their data before there was a working token.
			location.reload();
		} catch (err) {
			setServerToken(previous);
			error = ApiError.from(err).message;
			serverAuth.needsToken = true;
		} finally {
			checking = false;
		}
	}
</script>

<Dialog.Root open={serverAuth.needsToken}>
	<Dialog.Content showCloseButton={false} interactOutsideBehavior="ignore" escapeKeydownBehavior="ignore">
		<form class="flex flex-col gap-4" onsubmit={submit}>
			<Dialog.Header>
				<Dialog.Title>Connect to the VRCXD server</Dialog.Title>
				<Dialog.Description>
					Enter the server's API token (its <code>VRCX:Auth:Token</code> setting). It's remembered in
					this browser.
				</Dialog.Description>
			</Dialog.Header>
			<div class="flex flex-col gap-2">
				<Label for="server-token">API token</Label>
				<Input id="server-token" type="password" autocomplete="off" bind:value={token} required />
				{#if error}
					<p class="text-sm text-destructive">{error}</p>
				{/if}
			</div>
			<Dialog.Footer>
				<Button type="submit" disabled={checking || !token.trim()}>
					{checking ? 'Checking…' : 'Connect'}
				</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
