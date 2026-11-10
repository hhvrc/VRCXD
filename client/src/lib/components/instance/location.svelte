<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import Lock from '@lucide/svelte/icons/lock';
	import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
	import { appearance } from '#lib/settings/appearance.svelte.js';
	import { closedInstances } from '#lib/instances/instance-launch.svelte.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { groupHref, worldHref } from '#lib/api/links.js';
	import { groupNames, worldNames } from '#lib/people/names.svelte.js';
	import { cn } from '#lib/utils.js';
	import { locationText, parseLocation, resolveRegion } from '#lib/instances/location.js';
	import { openShortName } from '#lib/nav/direct-access.svelte.js';

	// The original's Location.vue: region flag, "World · Access" text, the group name in
	// parentheses, a spinner while traveling, a lock for strict instances, and "Restricted" for
	// age-gated ones. The instance id is a tooltip unless disabled. The text opens the world with this
	// instance first, and the group name opens the group.

	let {
		location,
		traveling,
		hint = '',
		grouphint = '',
		excludeGroupName = false,
		disableTooltip = false,
		link = true,
		class: className = ''
	}: {
		location: string | undefined | null;
		traveling?: string;
		hint?: string;
		grouphint?: string;
		excludeGroupName?: boolean;
		disableTooltip?: boolean;
		link?: boolean;
		class?: string;
	} = $props();

	const isTraveling = $derived(traveling !== undefined && location === 'traveling');
	const instanceId = $derived(isTraveling ? traveling! : (location ?? ''));
	const L = $derived(parseLocation(instanceId));
	const text = $derived(locationText(L, hint, hint ? undefined : worldNames.get(L.worldId)));
	const region = $derived(L.isRealInstance ? resolveRegion(L) : '');
	const groupName = $derived(excludeGroupName || !L.isRealInstance || !L.groupId ? '' : grouphint || groupNames.get(L.groupId) || L.groupId);
	// Restricted only while Show Age-Gated Instances is off; the instance id is shown inline when
	// Show Instance ID is on, so its tooltip goes.
	const isAgeRestricted = $derived(L.isRealInstance && L.ageGate && !appearance.isAgeGatedInstancesVisible.value);
	const showInstanceId = $derived(appearance.showInstanceIdInLocation.value);
	const tooltipDisabled = $derived(disableTooltip || !L.instanceName || showInstanceId);
	const href = $derived(link && L.worldId ? worldHref(instanceId) : undefined);
	// The original's handleShowWorldDialog: no instance, only an 8-character short name as the hint.
	const shortName = $derived(link && !L.worldId && hint.length === 8 ? hint : '');
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div class={cn((href || shortName) && 'cursor-pointer', className)} onclick={shortName ? () => void openShortName('', shortName) : undefined}>
	{#if !text}
		<div class="text-transparent">-</div>
	{:else}
		<div class="flex items-center gap-2">
			{#if isAgeRestricted}
				<Tooltip.Root delayDuration={300}>
					<Tooltip.Trigger>
						{#snippet child({ props })}
							<svelte:element this={href ? 'a' : 'div'} {...props} {href} class="inline-flex items-center gap-1 text-muted-foreground">
								<Lock class="size-3.5 shrink-0" />
								<span>{m.dialog_user_info_instance_age_restricted()}</span>
							</svelte:element>
						{/snippet}
					</Tooltip.Trigger>
					<Tooltip.Content side="top">{m.dialog_user_info_instance_age_restricted_tooltip()}</Tooltip.Content>
				</Tooltip.Root>
			{:else}
				{#if region}
					<div class={cn('flags shrink-0', region)}></div>
				{/if}
				<div class="inline-flex min-w-0 items-center gap-1">
					<Tooltip.Root delayDuration={300} disabled={tooltipDisabled}>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<svelte:element this={href ? 'a' : 'div'} {...props} {href} class="x-location inline-flex min-w-0 flex-nowrap items-center overflow-hidden truncate">
									{#if isTraveling}
										<Spinner class="mr-1 shrink-0" />
									{/if}
									<span class="min-w-0 flex-1 truncate"><span>{text}</span>{#if showInstanceId && L.instanceName}<span class="ml-1">{`· #${L.instanceName}`}</span>{/if}</span>
								</svelte:element>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content side="top">Instance ID: #{L.instanceName}</Tooltip.Content>
					</Tooltip.Root>
					{#if groupName && L.groupId}
						<!-- The group name gives way first, so a narrow row cuts its end, as the original does (upstream issue 1606). -->
						<a class="min-w-0 shrink-[9999] cursor-pointer truncate" href={link ? groupHref(L.groupId) : undefined}>({groupName})</a>
					{/if}
				</div>
				{#if location && closedInstances.get(location)}
					<Tooltip.Root delayDuration={300}>
						<Tooltip.Trigger><TriangleAlert class="size-4 text-orange-500" /></Tooltip.Trigger>
						<Tooltip.Content side="top">Instance closed at: {formatDateFilter(closedInstances.get(location), 'long')}</Tooltip.Content>
					</Tooltip.Root>
				{/if}
				{#if L.strict}
					<Lock class="size-4 text-muted-foreground" />
				{/if}
			{/if}
		</div>
	{/if}
</div>
