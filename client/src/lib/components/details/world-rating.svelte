<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ThumbsDown from '@lucide/svelte/icons/thumbs-down';
	import ThumbsUp from '@lucide/svelte/icons/thumbs-up';
	import { api } from '#lib/api/client.js';
	import { toastError } from '#lib/helpers/feedback.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';

	// The original's world Like and Dislike (upstream a68c0534 moved them to the end of the tab
	// header): this account's rating of the world. Clicking the active one clears it, the other one sets it.
	let { path }: { /** The account's path to the world, `.../accounts/{id}/worlds/{worldId}`. */ path: string } = $props();

	type WorldRatingValue = 'like' | 'dislike';
	type WorldRating = { value: WorldRatingValue | null };

	let rating = $state<WorldRatingValue | null>(null);

	$effect(() => {
		const at = path;
		rating = null;
		api.get<WorldRating>(`${at}/rating`)
			.then((found) => at === path && (rating = found?.value ?? null))
			.catch((err) => console.error('Failed to load world rating:', err));
	});

	function setRating(value: WorldRatingValue) {
		const at = path;
		const req = rating === value ? api.delete<WorldRating>(`${at}/rating`) : api.put<WorldRating>(`${at}/rating?value=${value}`);
		req.then((result) => at === path && (rating = result?.value ?? null)).catch(toastError);
	}

	const choices = [
		{ value: 'like', label: m.dialog_world_actions_like, Icon: ThumbsUp },
		{ value: 'dislike', label: m.dialog_world_actions_dislike, Icon: ThumbsDown }
	] as const;
</script>

<div class="flex shrink-0 items-center gap-1 px-2">
	{#each choices as choice (choice.value)}
		<Tooltip.Root>
			<Tooltip.Trigger>
				{#snippet child({ props })}
					<Button {...props} class="size-7 rounded-full text-muted-foreground hover:text-foreground" size="icon-sm" variant="ghost" aria-label={choice.label()} aria-pressed={rating === choice.value} onclick={() => setRating(choice.value)}>
						<choice.Icon class={rating === choice.value ? 'fill-current text-primary' : ''} />
					</Button>
				{/snippet}
			</Tooltip.Trigger>
			<Tooltip.Content side="top">{choice.label()}</Tooltip.Content>
		</Tooltip.Root>
	{/each}
</div>
