<script lang="ts">
	import UserIcon from '@lucide/svelte/icons/user';
	import type { Component } from 'svelte';
	import IconFrame from '#lib/components/cosmetics/icon-frame.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { cn } from '#lib/utils.js';

	// The 36px picture at the start of a person, group or option row: the original's avatar box with
	// its status ring (statusClass), icon frame, and a fallback icon or a plain muted circle.
	let {
		src,
		statusClass,
		iconFrame,
		frameEnabled,
		fallback = UserIcon,
		fallbackClass = 'size-4',
		class: className = ''
	}: {
		/** An already resolved image URL. */
		src?: string | null;
		statusClass?: string | false | null;
		iconFrame?: string | null;
		/** Unset follows the Profile Cosmetics setting, as IconFrame does. */
		frameEnabled?: boolean;
		/** The icon shown with no picture; null leaves the muted circle empty. */
		fallback?: Component<{ class?: string }> | null;
		fallbackClass?: string;
		class?: string;
	} = $props();
</script>

<span class={cn('relative mr-2.5 inline-block size-9 flex-none', statusClass, className)}>
	<Avatar.Root class="size-full rounded-full">
		<Avatar.Image src={src ?? undefined} class="object-cover" loading="lazy" />
		<Avatar.Fallback>
			{#if fallback}
				{@const Icon = fallback}
				<Icon class={cn('text-muted-foreground', fallbackClass)} />
			{/if}
		</Avatar.Fallback>
	</Avatar.Root>
	{#if iconFrame}<IconFrame enabled={frameEnabled} {iconFrame} />{/if}
</span>
