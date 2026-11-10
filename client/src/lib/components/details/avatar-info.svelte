<script lang="ts">
	import { SUBTEXT_MUTED } from '#lib/details.js';
	import { m } from '#lib/paraglide/messages.js';
	import Lock from '@lucide/svelte/icons/lock';
	import { ApiError } from '#lib/api/client.js';
	import { lookupAvatar, openAvatarFromImage } from '#lib/content/avatar-lookup.js';

	// The original's AvatarInfo.vue: the avatar's name with a lock when it is the wearer's own
	// upload, "Unknown Avatar" when the image could not be matched to an avatar. Clicking opens the
	// avatar. The server resolves name and owner when it records a change, so only `resolve` asks.

	let {
		imageUrl,
		userId,
		ownerId,
		avatarName,
		tags,
		resolve = false
	}: {
		imageUrl: string | undefined | null;
		userId: string;
		ownerId?: string | null;
		avatarName?: string | null;
		tags?: string[] | null;
		resolve?: boolean;
	} = $props();

	let resolved = $state<{ ownerId?: string | null; avatarName?: string | null } | undefined>();
	let message = $state('');

	$effect(() => {
		const url = imageUrl;
		resolved = undefined;
		message = '';
		if (resolve && url && !ownerId) {
			lookupAvatar(url, userId).then(
				(found) => {
					if (url === imageUrl) resolved = found;
				},
				() => {}
			);
		}
	});

	const owner = $derived(ownerId ?? resolved?.ownerId);
	const name = $derived(imageUrl && owner ? (avatarName ?? resolved?.avatarName ?? '') : '');
	const isOwn = $derived(!!owner && owner === userId);
	const tagText = $derived(Array.isArray(tags) ? tags.map((t) => String(t).replace('content_', '')).join(', ') : '');

	async function open(event: MouseEvent) {
		event.stopPropagation();
		if (!imageUrl) return;
		try {
			message = (await openAvatarFromImage(imageUrl, userId)) ?? '';
		} catch (err) {
			message = ApiError.from(err).message;
		}
	}
</script>

<button type="button" class="flex min-w-0 cursor-pointer items-center text-left align-top" onclick={open}>
	{#if name}
		<span class="mr-1 flex items-center">{name}{#if isOwn}<Lock class="ml-1 size-4" />{/if}</span>
	{:else}
		<span class="mr-1 flex items-center text-muted-foreground">{m.dialog_user_info_unknown_avatar()}</span>
	{/if}
	{#if tagText}
		<span class={SUBTEXT_MUTED} title={tagText}>{tagText}</span>
	{/if}
	{#if message}
		<span class="ml-1 block truncate text-xs text-destructive">{message}</span>
	{/if}
</button>
