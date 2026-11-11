<script lang="ts">
	import { onMount } from 'svelte';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import { onLiveEvent } from '#lib/api/live.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { AgentAlertKind } from '#lib/gen/vrcx_live_pb.js';
	import { externalLink } from '#lib/nav/external-link.svelte.js';

	// The original's alerts from runCheckVRChatDebugLoggingFlow, now that the server runs the check
	// through the PC's agent. The original's English text, as it has no translations.
	const ALERTS: Partial<Record<AgentAlertKind, { title: string; description: string }>> = {
		[AgentAlertKind.DEBUG_LOGGING_ENABLED]: {
			title: 'Enabled debug logging',
			description: 'VRCX has noticed VRChat debug logging is disabled and automatically re-enabled it. VRCX requires debug logging in order to function correctly.'
		},
		[AgentAlertKind.DEBUG_LOGGING_FAILED]: {
			title: 'Enable debug logging',
			description:
				'VRCX has noticed VRChat debug logging is disabled. VRCX requires debug logging in order to function correctly. Please enable debug logging in VRChat quick menu settings > debug > enable debug logging, then rejoin the instance or restart VRChat.'
		}
	};

	// handleFileSaveError: Windows kept the agent from writing a saved print, sticker or emoji.
	function blocked(item: string): Confirming {
		return {
			title: `Failed to create ${item} folder`,
			description: `Windows has blocked VRCX from creating files on your system. Please allow VRCX to create files to save ${item}s, would you like to see instructions on how to fix this?`,
			cancelText: 'Ignore',
			run: () => {
				confirming = undefined;
				externalLink.open('https://www.youtube.com/watch?v=1mwmmCdA4D8&t=213s');
			}
		};
	}

	let confirming = $state<Confirming | undefined>();

	onMount(() =>
		onLiveEvent((event) => {
			if (event.event.case !== 'agentAlert') return;
			const { accountId, kind, detail } = event.event.value;
			if (accountId && accountId !== vrchatAuth.ownerUserId) return;
			if (kind === AgentAlertKind.SAVE_MEDIA_BLOCKED) {
				confirming = blocked(detail || 'file');
				return;
			}
			const alert = ALERTS[kind];
			if (alert) confirming = { ...alert, run: () => (confirming = undefined) };
		})
	);
</script>

<ActionDialogs bind:confirming />
