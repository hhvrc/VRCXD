<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { EMPTY_STATE } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Check from '@lucide/svelte/icons/check';
	import User from '@lucide/svelte/icons/user';
	import ClearableInput from '#lib/components/group-moderation/clearable-input.svelte';
	import * as Avatar from '#lib/components/ui/avatar/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import type { GroupMember } from '#lib/groups/group-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupTransferDialog: search the members, check the one picked against VRChat's
	// requirements, then send the transfer request after a confirm.
	let {
		open = $bindable(false),
		groupId,
		groupName,
		ownerId,
		ontransferred
	}: { open?: boolean; groupId: string; groupName: string; ownerId?: string; ontransferred: () => void } = $props();

	const MIN_SEARCH_LENGTH = 3;
	const REQUIREMENTS: Record<string, string> = {
		groupNotMonetized: 'the group must not be monetized',
		hasVRCPlus: 'the member must have VRC+',
		hasVerifiedEmail: 'the member must have a verified email address',
		targetCanOwnMoreGroups: 'the member must not exceed the maximum number of groups they can own',
		targetIsGroupMember: 'the user must be a member of the group'
	};

	let searchText = $state('');
	let results = $state<GroupMember[]>([]);
	let selected = $state<GroupMember | undefined>();
	let searching = $state(false);
	let checking = $state(false);
	let submitting = $state(false);
	let alert = $state<string | undefined>();
	let confirming = $state(false);
	let searchRequest = 0;
	let checkRequest = 0;
	let timer: ReturnType<typeof setTimeout> | undefined;

	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(accountPath(owner, `/groups/${encodeURIComponent(groupId)}`));
	const nameOf = (m: GroupMember) => m.user?.displayName || m.userId;
	const imageOf = (m: GroupMember) => m.user?.iconUrl || m.user?.thumbnailUrl || m.user?.currentAvatarThumbnailImageUrl || m.user?.currentAvatarImageUrl; // The original's userImage order.

	$effect(() => {
		void open;
		searchRequest++;
		checkRequest++;
		clearTimeout(timer);
		searchText = '';
		results = [];
		selected = undefined;
		searching = checking = submitting = false;
	});

	function onSearch(value: string) {
		searchText = value;
		checkRequest++;
		selected = undefined;
		checking = false;
		results = [];
		const request = ++searchRequest;
		clearTimeout(timer);
		if (value.length < MIN_SEARCH_LENGTH) {
			searching = false;
			return;
		}
		searching = true;
		timer = setTimeout(() => search(value, request), 200);
	}

	async function search(query: string, request: number) {
		try {
			const found = await api.get<{ results: GroupMember[] }>(`${base}/members/search`, { query, n: 100, offset: 0 });
			if (request !== searchRequest || query !== searchText) return;
			results = found.results.filter((item) => item.userId !== ownerId);
		} catch (err) {
			toastError(err);
		} finally {
			if (request === searchRequest) searching = false;
		}
	}

	async function select(member: GroupMember) {
		if (checking || submitting) return;
		const request = ++checkRequest;
		selected = undefined;
		checking = true;
		try {
			const json = await api.get<{ requirements?: Record<string, boolean> }>(`${base}/transfer`, { transferTargetId: member.userId });
			if (request !== checkRequest) return;
			const failed = Object.entries(json.requirements ?? {})
				.filter(([, met]) => !met)
				.map(([key]) => REQUIREMENTS[key] ?? key);
			if (failed.length) {
				alert = `${nameOf(member)} does not meet all requirements to own this group. Requirements not met: ${failed.join('; ')}.`;
				return;
			}
			selected = member;
		} catch (err) {
			toastError(err);
		} finally {
			if (request === checkRequest) checking = false;
		}
	}

	async function transfer() {
		const member = selected;
		if (!member) return;
		confirming = false;
		submitting = true;
		try {
			await api.post(`${base}/transfer`, { transferTargetId: member.userId });
			toast.success(m.message_group_transfer_requested());
			open = false;
			ontransferred();
		} catch (err) {
			toastError(err);
		} finally {
			submitting = false;
		}
	}
</script>

<Dialog.Root bind:open={() => open, (next) => !next && !submitting && (open = false)}>
	<Dialog.Content class="bg-background sm:max-w-112.5">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_group_transfer_header()}</Dialog.Title>
			<Dialog.Description>Search for a member to transfer ownership of {groupName} to.</Dialog.Description>
		</Dialog.Header>
		<div class="flex min-h-0 flex-col gap-3">
			<ClearableInput bind:value={() => searchText, onSearch} disabled={submitting || checking} placeholder={m.dialog_group_transfer_search_placeholder()} />
			{#if searching}
				<div class="flex h-24 items-center justify-center"><Spinner /></div>
			{:else if searchText.length > 0 && searchText.length < MIN_SEARCH_LENGTH}
				<div class="{EMPTY_STATE} text-sm">Enter at least {MIN_SEARCH_LENGTH} characters to search.</div>
			{:else if searchText.length >= MIN_SEARCH_LENGTH && results.length === 0}
				<div class="{EMPTY_STATE} text-sm">{m.dialog_group_transfer_no_results()}</div>
			{:else if results.length > 0}
				<div class="max-h-64 overflow-y-auto">
					{#each results as member (member.userId)}
						<button
							type="button"
							class={['flex w-full items-center rounded-md p-2 text-left hover:bg-accent', selected?.userId === member.userId && 'bg-accent']}
							aria-pressed={selected?.userId === member.userId}
							disabled={submitting || checking}
							onclick={() => select(member)}>
							<Avatar.Root class="mr-3 size-10">
								<Avatar.Image src={imageSrc(imageOf(member)) ?? undefined} class="object-cover" />
								<Avatar.Fallback><User class="size-4 text-muted-foreground" /></Avatar.Fallback>
							</Avatar.Root>
							<span class="min-w-0 flex-1 truncate font-medium">{nameOf(member)}</span>
							{#if selected?.userId === member.userId}<Check class="ml-2 size-4" />{/if}
						</button>
					{/each}
				</div>
			{/if}
		</div>
		<Dialog.Footer>
			<Button variant="secondary" disabled={submitting} onclick={() => (open = false)}>{m.dialog_group_transfer_cancel()}</Button>
			<Button variant="destructive" disabled={submitting || !selected} onclick={() => (confirming = true)}>
				{#if submitting}<Spinner />{/if}
				Transfer Group
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={() => !!alert, (next) => !next && (alert = undefined)}>
	<Dialog.Content class="bg-background sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_group_transfer_requirements_title()}</Dialog.Title>
			<Dialog.Description>{alert}</Dialog.Description>
		</Dialog.Header>
		<Dialog.Footer><Button onclick={() => (alert = undefined)}>{m.dialog_alertdialog_ok()}</Button></Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<Dialog.Root bind:open={confirming}>
	<Dialog.Content class="bg-background sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_group_transfer_confirm_title()}</Dialog.Title>
			<Dialog.Description>Send a request to transfer ownership of {groupName} to {selected ? nameOf(selected) : ''}?</Dialog.Description>
		</Dialog.Header>
		<Dialog.Footer>
			<Button variant="outline" onclick={() => (confirming = false)}>{m.dialog_group_transfer_cancel()}</Button>
			<Button variant="destructive" onclick={transfer}>{m.dialog_alertdialog_confirm()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
