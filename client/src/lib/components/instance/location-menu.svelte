<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ExternalLink from '@lucide/svelte/icons/external-link';
	import Flag from '@lucide/svelte/icons/flag';
	import LineChart from '@lucide/svelte/icons/chart-line';
	import MessageSquare from '@lucide/svelte/icons/message-square';
	import Share2 from '@lucide/svelte/icons/share-2';
	import { goto } from '$app/navigation';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import Location from '#lib/components/instance/location.svelte';
	import PreviousInstanceInfo from '#lib/components/instance/previous-instance-info.svelte';
	import { worldHref, vrchatUrl } from '#lib/api/links.js';
	import { canOpenInGame, createAndSelfInvite } from '#lib/instances/instance-launch.svelte.js';
	import { parseLocation } from '#lib/instances/location.js';

	// Location.vue with enable-context-menu: the world menu on a real instance, plain text otherwise.
	let {
		location,
		hint = '',
		grouphint = '',
		class: className = '',
		disableTooltip = false,
		traveling = undefined,
		onpreviousinstances
	}: {
		location: string | undefined | null;
		hint?: string;
		grouphint?: string;
		class?: string;
		disableTooltip?: boolean;
		traveling?: string;
		/** Without one, the menu opens Previous Instance Info itself (the dashboard widgets and Friends Locations). */
		onpreviousinstances?: (location: string) => void;
	} = $props();

	let infoLocation = $state<string | undefined>();
	const showPrevious = (at: string) => (onpreviousinstances ? onpreviousinstances(at) : (infoLocation = at));

	const L = $derived(parseLocation(location ?? ''));
</script>

{#if L.isRealInstance && L.worldId}
	<ContextMenu.Root>
		<ContextMenu.Trigger class="block min-w-0">
			<Location {location} {hint} {grouphint} {disableTooltip} {traveling} class={className} />
		</ContextMenu.Trigger>
		<ContextMenu.Content>
			<ContextMenu.Item onclick={() => goto(worldHref(location ?? ''))}><ExternalLink class="size-4" />{m.dialog_avatar_actions_view_details()}</ContextMenu.Item>
			<ContextMenu.Item onclick={() => navigator.clipboard.writeText(vrchatUrl('world', L.worldId))}><Share2 class="size-4" />{m.dialog_user_actions_share()}</ContextMenu.Item>
			<ContextMenu.Separator />
			<ContextMenu.Item onclick={() => goto(worldHref(L.worldId))}><Flag class="size-4" />{m.dialog_new_instance_header()}</ContextMenu.Item>
			<ContextMenu.Item onclick={() => createAndSelfInvite(L.worldId).catch(() => {})}>
				<MessageSquare class="size-4" />{canOpenInGame() ? 'New Instance and Open In-game' : 'New Instance and Self Invite'}
			</ContextMenu.Item>
			<ContextMenu.Separator />
			<ContextMenu.Item onclick={() => showPrevious(location ?? '')}><LineChart class="size-4" />{m.dialog_user_actions_show_previous_instances()}</ContextMenu.Item>
		</ContextMenu.Content>
	</ContextMenu.Root>
{:else}
	<Location {location} {hint} {grouphint} {disableTooltip} {traveling} class={className} />
{/if}

{#if infoLocation}
	<PreviousInstanceInfo bind:location={infoLocation} />
{/if}
