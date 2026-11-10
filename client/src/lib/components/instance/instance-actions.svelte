<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import LogIn from '@lucide/svelte/icons/log-in';
	import Mail from '@lucide/svelte/icons/mail';
	import { Button } from '#lib/components/ui/button/index.js';
	import { ApiError } from '#lib/api/client.js';
	import { canInviteSelf, canOpenInGame, instanceDialogs, joinInstance } from '#lib/instances/instance-launch.svelte.js';
	import { isRealInstance } from '#lib/instances/location.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The Launch and Invite Yourself buttons of the original's InstanceActionBar, wherever an instance is shown.
	let { location, shortName = '', friendIds, class: className = '' }: { location: string; shortName?: string; friendIds?: ReadonlySet<string>; class?: string } = $props();

	const BUTTON = 'size-6 rounded-full text-xs text-muted-foreground hover:text-foreground';

	let notice = $state('');
	let error = $state('');

	const showInvite = $derived(canInviteSelf(location, vrchatAuth.ownerUserId, friendIds));
	const inviteLabel = $derived(canOpenInGame() ? 'Open in VRChat' : 'Invite Yourself');

	async function invite() {
		notice = '';
		error = '';
		try {
			notice = await joinInstance(location, shortName);
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}
</script>

{#if isRealInstance(location)}
	<span class="inline-flex items-center gap-2 {className}">
		<Button class={BUTTON} size="icon-sm" variant="outline" title={m.dialog_user_info_launch_invite_tooltip()} aria-label={m.dialog_user_info_launch_invite_tooltip()} onclick={() => (instanceDialogs.launch = { location, shortName })}><LogIn /></Button>
		{#if showInvite}
			<Button class={BUTTON} size="icon-sm" variant="outline" title={inviteLabel} aria-label={inviteLabel} onclick={invite}><Mail class="size-4" /></Button>
		{/if}
		{#if error}<span class="text-xs text-destructive">{error}</span>{:else if notice}<span class="text-xs text-muted-foreground">{notice}</span>{/if}
	</span>
{/if}
