<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import { onMount } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's checkYoutubeBotError: the first "Sign in to confirm" YouTube error of the session
	// offers VRCVideoCacher's Steam page, unless "Don't show again" was ticked.
	const HIDE_KEY = 'VRCX_hideYoutubeBotErrorPrompt';
	const STORE_URL = 'https://store.steampowered.com/app/4296960';

	let open = $state(false);
	let dontShowAgain = $state(false);
	let prompted = false;

	function close(openStore: boolean) {
		if (dontShowAgain) storeSetting(HIDE_KEY, true);
		open = false;
		if (openStore) window.open(STORE_URL, '_blank', 'noopener,noreferrer');
	}

	onMount(() =>
		onLiveEvent((event) => {
			if (event.event.case !== 'youtubeBotError' || prompted) return;
			const { accountId } = event.event.value;
			if (accountId && accountId !== vrchatAuth.ownerUserId) return;
			prompted = true;
			if (readSetting(HIDE_KEY, false)) return;
			dontShowAgain = false;
			open = true;
		})
	);
</script>

<Dialog.Root bind:open={() => open, (value) => { if (!value) close(false); }}>
	<Dialog.Content class="sm:max-w-md" showCloseButton={false}>
		<Dialog.Header>
			<Dialog.Title>{m.confirm_youtube_bot_error_title()}</Dialog.Title>
			<Dialog.Description>{m.confirm_youtube_bot_error()}</Dialog.Description>
		</Dialog.Header>
		<label class="flex items-center gap-2 text-sm"><Checkbox bind:checked={dontShowAgain} /> Don't show again</label>
		<Dialog.Footer>
			<Button variant="outline" onclick={() => close(false)}>{m.prompt_redeem_cancel()}</Button>
			<Button onclick={() => close(true)}>{m.prompt_rename_world_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
