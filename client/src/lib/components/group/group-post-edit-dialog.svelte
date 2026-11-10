<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import ViewableImage from '#lib/components/common/viewable-image.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as RadioGroup from '#lib/components/ui/radio-group/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import type { GroupPost, GroupRole } from '#lib/groups/group-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupPostEditDialog: Create Post (no post) or edit one. Like the original, it
	// closes as soon as it sends and reports the answer in a toast.
	let {
		open = $bindable(false),
		groupId,
		roles,
		post,
		onsaved
	}: { open?: boolean; groupId: string; roles: GroupRole[]; post?: GroupPost; onsaved: (post: GroupPost) => void } = $props();

	let title = $state('');
	let text = $state('');
	let sendNotification = $state(true);
	let visibility = $state('group');
	let roleIds = $state<string[]>([]);
	let image = $state({ fileId: '', imageUrl: '' });
	let galleryOpen = $state(false);

	const owner = $derived(vrchatAuth.ownerUserId);
	const roleOptions = $derived(roles.map((r) => ({ value: r.id, label: r.name })));

	// showGroupPostEditDialog: a blank form, or the post's fields.
	$effect(() => {
		if (!open) return;
		title = post?.title ?? '';
		text = post?.text ?? '';
		sendNotification = true;
		visibility = post?.visibility ?? 'group';
		roleIds = [...(post?.roleIds ?? [])];
		image = { fileId: post?.imageId ?? '', imageUrl: post?.imageUrl ?? '' };
	});

	function save() {
		if (!title || !text) {
			toast.warning('Title and text are required');
			return;
		}
		const base = accountPath(owner, `/groups/${encodeURIComponent(groupId)}/posts`);
		const body = { title, text, roleIds, visibility, imageId: image.fileId || null };
		const request = post ? api.put<GroupPost>(`${base}/${encodeURIComponent(post.id)}`, body) : api.post<GroupPost>(base, { ...body, sendNotification });
		const edited = !!post;
		request
			.then((saved) => {
				onsaved(saved);
				toast.success(edited ? 'Group post edited' : 'Group post created');
			})
			.catch(toastError);
		open = false;
	}
</script>

<Dialog.Root bind:open>
	<Dialog.Content class="bg-background sm:max-w-162.5">
		<Dialog.Header>
			<Dialog.Title>{m.dialog_group_post_edit_header()}</Dialog.Title>
		</Dialog.Header>
		<div class="flex flex-col gap-4">
			<div class="flex flex-col gap-2">
				<span class="text-sm font-medium">{m.dialog_group_post_edit_title()}</span>
				<Input bind:value={title} class="h-8" />
			</div>
			<div class="flex flex-col gap-2">
				<span class="text-sm font-medium">{m.dialog_group_post_edit_message()}</span>
				<Textarea bind:value={text} rows={4} class="mt-2 resize-none" />
			</div>
			{#if !post}
				<label class="inline-flex items-center gap-2"><Checkbox bind:checked={sendNotification} /><span>{m.dialog_group_post_edit_send_notification()}</span></label>
			{/if}
			<div class="flex flex-col gap-2">
				<span class="text-sm font-medium">{m.dialog_group_post_edit_post_visibility()}</span>
				<RadioGroup.Root bind:value={visibility} class="flex items-center gap-4">
					<label class="flex items-center gap-2"><RadioGroup.Item value="public" />{m.dialog_group_tags_public()}</label>
					<label class="flex items-center gap-2"><RadioGroup.Item value="group" />{m.dialog_new_instance_group()}</label>
				</RadioGroup.Root>
			</div>
			{#if visibility === 'group'}
				<div class="flex flex-col gap-2">
					<span class="text-sm font-medium">{m.dialog_group_info_roles()}</span>
					<MultiSelect values={roleIds} options={roleOptions} placeholder={m.dialog_new_instance_role_placeholder()} class="h-8 w-full" onchange={(values) => (roleIds = values.filter(Boolean))} />
				</div>
			{/if}
			<div class="flex flex-col gap-2">
				<span class="text-sm font-medium">{m.dialog_group_post_edit_image()}</span>
				{#if image.fileId}
					<div class="mr-1.5 inline-flex flex-none items-start gap-2">
						<ViewableImage src={image.imageUrl} class="size-15 flex-none overflow-hidden rounded-md" imageClass="size-15 object-cover" />
						<Button size="sm" variant="outline" onclick={() => (image = { fileId: '', imageUrl: '' })}>{m.dialog_invite_message_clear_selected_image()}</Button>
					</div>
				{:else}
					<div><Button size="sm" variant="outline" onclick={() => (galleryOpen = true)}>{m.dialog_gallery_select_header()}</Button></div>
				{/if}
			</div>
		</div>
		<Dialog.Footer>
			<Button variant="secondary" onclick={() => (open = false)}>{m.dialog_group_transfer_cancel()}</Button>
			<Button onclick={save}>{post ? 'Edit Post' : 'Create Post'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<GallerySelectDialog bind:open={galleryOpen} onselect={(picked) => (image = picked)} />
