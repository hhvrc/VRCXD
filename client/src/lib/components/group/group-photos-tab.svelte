<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import ImageIcon from '@lucide/svelte/icons/image';
	import ImagePlus from '@lucide/svelte/icons/image-plus';
	import Pencil from '@lucide/svelte/icons/pencil';
	import Plus from '@lucide/svelte/icons/plus';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import CountedInput from '#lib/components/group/counted-input.svelte';
	import TabRefreshButton from '#lib/components/details/tab-refresh-button.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER, TAB_PANE } from '#lib/details.js';
	import { hasGroupPermission, type Group, type GroupGallery } from '#lib/groups/group-dialog.svelte.js';
	import { imageViewer } from '#lib/content/image-viewer.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupDialogPhotosTab, GroupGalleryForm and useGroupGalleries: each gallery's images,
	// a gallery form for those who manage galleries, and Add Image for those who may submit.
	let { groupRef = $bindable(), images = $bindable({}) }: { groupRef: Group; images?: Record<string, GalleryImage[]> } = $props();
	// The group, whose galleries this tab adds to and removes from.
	const group = $derived(groupRef);

	export type GalleryImage = { id: string; groupId: string; galleryId: string; imageUrl: string; submittedByUserId?: string; approved?: boolean };

	let loading = $state(false);
	let current = $state('0');
	let creating = $state(false);
	let editingId = $state('');
	let saving = $state(false);
	let form = $state({ name: '', description: '', membersOnly: false });
	let addTo = $state('');
	let galleryOpen = $state(false);
	let confirming = $state<Confirming | undefined>();
	let broken = $state<Record<string, boolean>>({});

	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(accountPath(owner, `/groups/${encodeURIComponent(group.id)}/galleries`));
	const galleries = $derived(group.galleries ?? []);
	const myRoleIds = $derived(group.myMember?.roleIds ?? []);
	const hasAnyRole = (roleIds?: string[] | null) => !!roleIds?.some((id) => myRoleIds.includes(id));
	const canManageGroupGalleries = $derived(hasGroupPermission(group, 'group-galleries-manage'));
	const canManageGallery = (g: GroupGallery) => canManageGroupGalleries || hasAnyRole(g.roleIdsToManage);
	const canSubmit = (g: GroupGallery) => canManageGallery(g) || hasAnyRole(g.roleIdsToSubmit);
	const canDeleteImage = (g: GroupGallery, image: GalleryImage) => canManageGallery(g) || image.submittedByUserId === owner;
	const statusClass = (g: GroupGallery) => (!g.membersOnly ? 'blue' : !g.roleIdsToView ? 'green' : 'red');
	const statusText = (g: GroupGallery) => (!g.membersOnly ? 'Visible to everyone' : !g.roleIdsToView ? 'Visible to group members' : 'Visible to specific roles');

	/** getGroupGalleries: every gallery, 100 images a page, up to 50 pages each. */
	export async function reload() {
		const groupId = group.id;
		images = {};
		current = '0';
		loading = true;
		await Promise.allSettled(
			galleries.map(async (gallery) => {
				for (let page = 0, offset = 0; page < 50; page++, offset += 100) {
					const found = await api.get<GalleryImage[]>(`${base}/${encodeURIComponent(gallery.id)}`, { n: 100, offset });
					if (group.id !== groupId) return;
					for (const image of found) if (image.groupId === groupId) (images[image.galleryId] ??= []).push(image);
					if (found.length < 100) break;
				}
			})
		);
		loading = false;
	}

	$effect(() => {
		void group.id;
		reload();
	});

	function startCreate() {
		editingId = '';
		creating = true;
		form = { name: '', description: '', membersOnly: false };
	}

	function startEdit(gallery: GroupGallery) {
		creating = false;
		editingId = gallery.id;
		form = { name: gallery.name ?? '', description: gallery.description ?? '', membersOnly: !!gallery.membersOnly };
	}

	function closeForm() {
		creating = false;
		editingId = '';
	}

	async function save() {
		const fields = { name: form.name.trim(), description: form.description.trim(), membersOnly: form.membersOnly };
		saving = true;
		try {
			if (creating) {
				const created = await api.post<GroupGallery>(base, fields);
				const next = [...galleries, created];
				groupRef.galleries = next;
				images[created.id] = [];
				current = String(next.length - 1);
				toast.success(m.dialog_group_gallery_created());
			} else {
				const id = editingId;
				const updated = await api.put<GroupGallery>(`${base}/${encodeURIComponent(id)}`, fields);
				groupRef.galleries = galleries.map((g) => (g.id === id ? { ...g, ...updated } : g));
				toast.success(m.dialog_group_gallery_saved());
			}
			closeForm();
		} catch (err) {
			toastError(err);
		} finally {
			saving = false;
		}
	}

	function deleteGallery(gallery: GroupGallery) {
		confirming = {
			get title() { return m.confirm_title(); },
			description: `Are you sure you want to delete the gallery ${gallery.name} and all of its images?`,
			destructive: true,
			run: async () => {
				confirming = undefined;
				try {
					await api.delete(`${base}/${encodeURIComponent(gallery.id)}`);
					groupRef.galleries = galleries.filter((g) => g.id !== gallery.id);
					delete images[gallery.id];
					current = '0';
					toast.success(m.dialog_group_gallery_deleted());
				} catch (err) {
					toastError(err);
				}
			}
		};
	}

	async function addImage({ fileId }: { fileId: string }) {
		const galleryId = addTo;
		if (!fileId || !galleryId) return;
		try {
			const image = await api.post<GalleryImage>(`${base}/${encodeURIComponent(galleryId)}/images`, { fileId });
			if (!image.approved) {
				toast.success(m.dialog_group_gallery_image_pending_approval());
				return;
			}
			images[galleryId] = [image, ...(images[galleryId] ?? [])];
			toast.success(m.dialog_group_gallery_image_added());
		} catch (err) {
			toastError(err);
		}
	}

	function deleteImage(image: GalleryImage) {
		confirming = {
			get title() { return m.confirm_title(); },
			get description() { return m.confirm_delete_gallery_image(); },
			destructive: true,
			run: async () => {
				confirming = undefined;
				try {
					await api.delete(accountPath(owner, `/groups/${encodeURIComponent(image.groupId)}/galleries/${encodeURIComponent(image.galleryId)}/images/${encodeURIComponent(image.id)}`));
					images[image.galleryId] = (images[image.galleryId] ?? []).filter((i) => i.id !== image.id);
					toast.success(m.dialog_group_gallery_image_deleted());
				} catch (err) {
					toastError(err);
				}
			}
		};
	}
</script>

{#snippet tip(text: string, icon: typeof Plus, onclick: () => void, disabled = false)}
	{@const Icon = icon}
	<Tooltip.Root>
		<Tooltip.Trigger>
			{#snippet child({ props })}
				<Button {...props} size="icon-sm" variant="ghost" class="rounded-full text-muted-foreground hover:text-foreground" aria-label={text} {disabled} {onclick}><Icon /></Button>
			{/snippet}
		</Tooltip.Trigger>
		<Tooltip.Content side="top">{text}</Tooltip.Content>
	</Tooltip.Root>
{/snippet}

{#snippet galleryForm(submitLabel: string)}
	<div class="flex flex-col gap-2 p-2">
		<CountedInput bind:value={form.name} maxlength={64} placeholder={m.dialog_group_gallery_name()} disabled={saving} />
		<CountedInput bind:value={form.description} maxlength={512} rows={3} placeholder={m.dialog_group_gallery_description()} disabled={saving} />
		<label class="flex items-center gap-2 text-sm"><Checkbox bind:checked={form.membersOnly} disabled={saving} /><span>{m.dialog_group_gallery_members_only()}</span></label>
		<div class="flex justify-end gap-2">
			<Button variant="secondary" size="sm" disabled={saving} onclick={closeForm}>{m.dialog_group_transfer_cancel()}</Button>
			<Button size="sm" disabled={saving || !form.name.trim()} onclick={save}>{submitLabel}</Button>
		</div>
	</div>
{/snippet}

<div class={TAB_PANE}>
	<div class="flex items-center gap-1">
		<TabRefreshButton label={m.dialog_group_actions_refresh()} {loading} onclick={reload} />
		{#if canManageGroupGalleries}{@render tip('Create Gallery', Plus, startCreate, creating)}{/if}
	</div>
	{#if creating}{@render galleryForm('Create')}{/if}
	{#if galleries.length}
		<TabsPrimitive.Root bind:value={current} class="mt-2.5 flex min-h-0 flex-1 flex-col">
			<div class={TAB_LIST_WRAP}>
				<TabsPrimitive.List class={TAB_LIST}>
					{#each galleries as gallery, index (gallery.id)}
						<TabsPrimitive.Trigger value={String(index)} class={TAB_TRIGGER}>
							<span class="text-base font-bold">{gallery.name}</span>
							<span title={statusText(gallery)}><i class={['x-status-icon', statusClass(gallery)]} style="margin-left: 6px"></i></span>
							<span class="ml-1.5 text-xs text-muted-foreground">{images[gallery.id]?.length ?? 0}</span>
						</TabsPrimitive.Trigger>
					{/each}
				</TabsPrimitive.List>
			</div>
			{#each galleries as gallery, index (gallery.id)}
				<TabsPrimitive.Content value={String(index)} class="min-h-0 flex-1 overflow-y-auto outline-none">
					{#if editingId === gallery.id}
						{@render galleryForm('Save')}
					{:else}
						<div class="flex items-center gap-2 p-2">
							<span class="flex-1 text-xs text-muted-foreground">{gallery.description}</span>
							{#if canSubmit(gallery)}{@render tip('Add Image', ImagePlus, () => ((addTo = gallery.id), (galleryOpen = true)))}{/if}
							{#if canManageGallery(gallery)}{@render tip('Edit Gallery', Pencil, () => startEdit(gallery))}{/if}
							{#if canManageGroupGalleries}{@render tip('Delete Gallery', Trash2, () => deleteGallery(gallery))}{/if}
						</div>
					{/if}
					<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 16px; margin-top: 8px">
						{#each images[gallery.id] ?? [] as image (image.id)}
							<Card.Root class="group relative overflow-hidden p-0 transition-shadow hover:shadow-md">
								{#if canDeleteImage(gallery, image)}
									<Button
										size="icon-sm"
										variant="secondary"
										class="absolute top-1.5 right-1.5 z-10 opacity-0 transition-opacity group-hover:opacity-100"
										aria-label={m.dialog_group_gallery_delete_image_tooltip()}
										title={m.dialog_group_gallery_delete_image_tooltip()}
										onclick={(e: MouseEvent) => {
											e.stopPropagation();
											deleteImage(image);
										}}><Trash2 /></Button>
								{/if}
								<button type="button" class="cursor-pointer" onclick={() => imageViewer.show(image.imageUrl)}>
									{#if broken[image.id]}
										<div class="flex h-[200px] w-full items-center justify-center bg-muted"><ImageIcon class="size-8 text-muted-foreground" /></div>
									{:else}
										<img src={imageSrc(image.imageUrl)} alt="" class="max-h-full max-w-full" loading="lazy" onerror={() => (broken[image.id] = true)} />
									{/if}
								</button>
							</Card.Root>
						{/each}
					</div>
				</TabsPrimitive.Content>
			{/each}
		</TabsPrimitive.Root>
	{/if}
</div>

<GallerySelectDialog bind:open={galleryOpen} onselect={addImage} />
<ActionDialogs bind:confirming />
