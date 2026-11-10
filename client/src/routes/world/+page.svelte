<script lang="ts">
	import { DETAIL_PAGE } from '#lib/details.js';
	import { browser } from '$app/env';
	import { page } from '$app/state';
	import WorldDetails from '#lib/components/details/world-details.svelte';

	// Where a world link opened in a new tab lands. In the app a click opens the pop-up instead.
	// The id may be a whole location (wrld_x:12345~region(eu)), as worldHref builds them: the world is
	// the part before the colon and the rest picks the instance, as the pop-up does.
	const id = $derived(browser ? page.url.searchParams.get('id') ?? '' : '');
	const worldId = $derived(id.split(':')[0]);
	const location = $derived(id.includes(':') ? id : undefined);
</script>

<svelte:head>
	<title>World · VRCXD</title>
</svelte:head>

<div class={DETAIL_PAGE}>
	<WorldDetails {worldId} {location} />
</div>
