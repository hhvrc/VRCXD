<script lang="ts">
	import { DETAIL_PAGE } from '#lib/details.js';
	import { browser } from '$app/env';
	import { page } from '$app/state';
	import UserDetails from '#lib/components/details/user-details.svelte';

	// Where a user link opened in a new tab lands. In the app a click opens the pop-up instead.
	// A query parameter rather than /user/[userId]: the whole app is prerendered.
	const userId = $derived(browser ? page.url.searchParams.get('id') ?? '' : '');
</script>

<svelte:head>
	<title>User · VRCXD</title>
</svelte:head>

<div
	class={DETAIL_PAGE}
><UserDetails userId={userId} /></div>
