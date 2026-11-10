<script lang="ts">
	import { userHref } from '#lib/api/links.js';
	import { userNames } from '#lib/people/names.svelte.js';

	// The original's DisplayName.vue: a user's name from just their id, opening their pop-up.
	let { userId, hint = '', class: className = '' }: { userId: string; hint?: string; class?: string } = $props();

	const name = $derived(hint || userNames.get(userId) || userId);
</script>

<a class={className} href={userHref(userId)}>{name}</a>
