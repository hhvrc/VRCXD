<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { INSTANCE_CATEGORY_ICONS } from '#lib/instances/instance-create.js';
	import { instanceCatalog, instanceTags } from '#lib/instances/instance-launch.svelte.js';

	// The original's InstanceCategory (upstream 27e9acf9): the category's icon before a location, with
	// the category and vibes on hover. Shown only once a fetch of the instance has said what they are.
	let { location }: { location: string } = $props();

	const tags = $derived(instanceTags.get(location));
	const category = $derived.by(() => {
		if (!tags?.categoryId) return null;
		const found = instanceCatalog.categories.find((c) => c.id === tags.categoryId);
		return found ? { name: found.name, icon: found.iconUrl ? INSTANCE_CATEGORY_ICONS[found.iconUrl] : undefined } : null;
	});
	const vibeTitles = $derived.by(() => {
		const byId = new Map(instanceCatalog.vibes.map((v) => [v.id, v.title]));
		return (tags?.vibeIds ?? []).map((id) => byId.get(id) ?? id).join(', ');
	});
</script>

{#if tags && (tags.categoryId || tags.vibeIds.length)}
	<Tooltip.Root delayDuration={300}>
		<Tooltip.Trigger>
			{#snippet child({ props })}
				<span {...props} class="flex shrink-0 items-center text-muted-foreground">
					<i class={['text-base leading-none', category?.icon ?? 'ri-apps-2-line']}></i>
				</span>
			{/snippet}
		</Tooltip.Trigger>
		<Tooltip.Content side="top">
			<div class="flex flex-col gap-1">
				{#if tags.categoryId}<span>{m.dialog_user_info_instance_category()} {category?.name ?? tags.categoryId}</span>{/if}
				{#if tags.vibeIds.length}<span>{m.dialog_user_info_instance_vibes()} {vibeTitles}</span>{/if}
			</div>
		</Tooltip.Content>
	</Tooltip.Root>
{/if}
