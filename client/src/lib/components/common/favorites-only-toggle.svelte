<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Star from '@lucide/svelte/icons/star';
	import { Toggle } from '#lib/components/ui/toggle/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';

	// The star toggle in the feed, friend list and game log toolbars that limits rows to favourites.
	let { pressed, onPressedChange, class: className }: { pressed: boolean; onPressedChange: (value: boolean) => void; class?: string } = $props();
</script>

<Tooltip.Root>
	<Tooltip.Trigger>
		{#snippet child({ props })}
			<div {...props} class={className}>
				<Toggle variant="outline" size="sm" {pressed} aria-label={m.view_feed_favorites_only_tooltip()} {onPressedChange}>
					<Star fill={pressed ? 'currentColor' : 'none'} />
				</Toggle>
			</div>
		{/snippet}
	</Tooltip.Trigger>
	<Tooltip.Content side="bottom">{m.view_feed_favorites_only_tooltip()}</Tooltip.Content>
</Tooltip.Root>
