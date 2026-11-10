<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import CountedInput from '#lib/components/group/counted-input.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as InputGroup from '#lib/components/ui/input-group/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { getFaviconUrl } from '#lib/helpers/format.js';
	import { groupEdit, languageOptions, notifyGroupChanged, type Group } from '#lib/groups/group-dialog.svelte.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupEditDialog: Create Group (from your own Groups tab) and Edit Group (from the
	// group pop-up), mounted once for the whole app.
	type RoleTemplate = { value: string; name?: string };

	const JOIN_STATES = [
		{ value: 'open', get label() { return m.dialog_group_tags_open(); } },
		{ value: 'request', get label() { return m.dialog_group_tags_request(); } },
		{ value: 'invite', get label() { return m.dialog_group_tags_invite(); } },
		{ value: 'closed', get label() { return m.dialog_group_tags_closed(); } }
	];
	const PRIVACY = [
		{ value: 'default', get label() { return m.dialog_group_event_edit_access_public(); } },
		{ value: 'private', get label() { return m.dialog_group_tags_private(); } }
	];
	const ROLE_TEMPLATES = ['default', 'managedFree', 'managedInvite', 'managedRequest'];

	let loading = $state(false);
	let roleTemplates = $state<RoleTemplate[]>([]);
	let roleTemplatesLoading = $state(false);
	let languageNames = $state<Record<string, string>>({});
	let gallery = $state({ open: false, icon: false });

	const owner = $derived(vrchatAuth.ownerUserId);
	const D = $derived(groupEdit.form);
	const isEditMode = $derived(D.mode === 'edit');
	const languageList = $derived(Object.entries(languageNames).map(([value, label]) => ({ value, label })));
	const canSubmit = $derived(
		!loading &&
			!roleTemplatesLoading &&
			D.name.trim().length >= 3 &&
			/^[A-Z0-9]{3,6}$/.test(D.shortCode) &&
			JOIN_STATES.some((s) => s.value === D.joinState) &&
			(isEditMode || ((D.privacy === 'default' || D.privacy === 'private') && ROLE_TEMPLATES.includes(D.roleTemplate)))
	);

	// showCreateGroupDialog fetches the role templates as it opens; VRChat answers a list or a map.
	$effect(() => {
		if (!groupEdit.open || !owner) return;
		languageOptions(owner).then((names) => (languageNames = names));
		if (groupEdit.form.mode !== 'create') return;
		roleTemplates = [];
		roleTemplatesLoading = true;
		api.get<unknown>(accountPath(owner, '/groups/role-templates'))
			.then((json) => {
				roleTemplates = Array.isArray(json)
					? (json as RoleTemplate[])
					: Object.entries((json ?? {}) as Record<string, unknown>).map(([value, template]) => ({ value, ...(typeof template === 'object' && template !== null ? template : {}) }));
			})
			.catch(toastError)
			.finally(() => (roleTemplatesLoading = false));
	});

	function close() {
		if (!loading) groupEdit.open = false;
	}

	function showGallery(icon: boolean) {
		gallery = { open: true, icon };
	}

	function selectImage({ fileId, imageUrl }: { fileId: string; imageUrl: string }) {
		if (gallery.icon) {
			D.iconId = fileId;
			D.iconUrl = imageUrl;
		} else {
			D.bannerId = fileId;
			D.bannerUrl = imageUrl;
		}
	}

	async function save() {
		if (!canSubmit) return;
		loading = true;
		const common = {
			name: D.name.trim(),
			shortCode: D.shortCode,
			description: D.description.trim(),
			joinState: D.joinState,
			bannerId: D.bannerId || undefined,
			iconId: D.iconId || undefined
		};
		try {
			const group = isEditMode
				? await api.put<Group>(accountPath(owner, `/groups/${encodeURIComponent(D.groupId)}`), {
						...common,
						languages: D.languages.slice(0, 3),
						rules: D.rules.trim(),
						links: D.links.map((link) => link.trim()).filter(Boolean),
						allowGroupJoinPrompt: D.allowGroupJoinPrompt
					})
				: await api.post<Group>(accountPath(owner, '/groups'), { ...common, privacy: D.privacy, roleTemplate: D.roleTemplate });
			groupEdit.open = false;
			toast.success(isEditMode ? 'Group updated' : 'Group created');
			notifyGroupChanged(group.id);
			userDialog.openGroup(group.id);
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}
</script>

{#snippet field(label: string)}
	<span class="text-sm leading-none font-medium select-none">{label}</span>
{/snippet}

<Dialog.Root bind:open={() => groupEdit.open, (next) => !next && close()}>
	<Dialog.Content class="flex max-h-[85vh] flex-col gap-0 overflow-hidden bg-background p-0 sm:max-w-180">
		<Dialog.Header class="px-6 pt-6 pb-4">
			<Dialog.Title>{isEditMode ? 'Edit Group' : 'Create Group'}</Dialog.Title>
		</Dialog.Header>

		<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto px-6 py-2">
			<div class="flex flex-col gap-4">
				<div class="flex flex-col gap-2">
					{@render field('Name')}
					<CountedInput bind:value={D.name} maxlength={64} disabled={loading} />
				</div>
				<div class="flex flex-col gap-2">
					{@render field('Short Code')}
					<CountedInput bind:value={D.shortCode} maxlength={6} disabled={loading} oninput={(value) => value.toUpperCase().replace(/[^A-Z0-9]/g, '')} />
				</div>
				<div class="flex flex-col gap-2">
					{@render field('Description')}
					<CountedInput bind:value={D.description} maxlength={250} rows={4} disabled={loading} />
				</div>
				<div class="flex flex-col gap-2">
					{@render field('Join State')}
					<Select.Root type="single" bind:value={D.joinState} disabled={loading}>
						<Select.Trigger size="sm" class="w-full">{JOIN_STATES.find((s) => s.value === D.joinState)?.label}</Select.Trigger>
						<Select.Content>
							{#each JOIN_STATES as option (option.value)}
								<Select.Item value={option.value} label={option.label}>{option.label}</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
				</div>

				{#if !isEditMode}
					<div class="flex flex-col gap-2">
						{@render field('Privacy')}
						<Select.Root type="single" bind:value={D.privacy} disabled={loading}>
							<Select.Trigger size="sm" class="w-full">{PRIVACY.find((p) => p.value === D.privacy)?.label}</Select.Trigger>
							<Select.Content>
								{#each PRIVACY as option (option.value)}
									<Select.Item value={option.value} label={option.label}>{option.label}</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex flex-col gap-2">
						{@render field('Role Template')}
						<Select.Root type="single" bind:value={D.roleTemplate} disabled={loading || roleTemplatesLoading}>
							<Select.Trigger size="sm" class="w-full">
								{roleTemplates.find((t) => t.value === D.roleTemplate)?.name ?? (roleTemplatesLoading ? 'Loading role templates...' : 'Role Template')}
							</Select.Trigger>
							<Select.Content>
								{#each roleTemplates as template (template.value)}
									<Select.Item value={template.value} label={template.name ?? template.value}>{template.name}</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
				{:else}
					<div class="flex flex-col gap-2">
						{@render field('Languages')}
						<MultiSelect disabled={loading} values={D.languages} options={languageList} placeholder={m.dialog_group_edit_no_language()} class="h-8 w-full" onchange={(values) => (D.languages = values.slice(0, 3))} />
					</div>
					<div class="flex flex-col gap-2">
						{@render field('Rules')}
						<CountedInput bind:value={D.rules} maxlength={2048} rows={4} disabled={loading} />
					</div>
					<div class="flex flex-col gap-2">
						{@render field('Links')}
						<div class="space-y-2">
							{#each D.links as link, index (index)}
								<InputGroup.Root class="flex-nowrap">
									<InputGroup.Addon>
										{#if link}
											<img src={getFaviconUrl(link)} alt="" style="width: 16px; height: 16px; vertical-align: middle" />
										{:else}
											<div style="width: 16px; height: 16px"></div>
										{/if}
									</InputGroup.Addon>
									<InputGroup.Input bind:value={D.links[index]} maxlength={1000} disabled={loading} class="h-8" />
									<InputGroup.Addon align="inline-end">
										<Button variant="ghost" size="icon-sm" disabled={loading} aria-label={m.dialog_world_actions_delete()} onclick={() => D.links.splice(index, 1)}><Trash2 class="size-4" /></Button>
									</InputGroup.Addon>
								</InputGroup.Root>
							{/each}
							<Button variant="outline" size="sm" disabled={loading || D.links.length >= 3} onclick={() => D.links.push('')}>{m.dialog_group_edit_add_link()}</Button>
						</div>
					</div>
					<label class="inline-flex items-start gap-2">
						<Checkbox bind:checked={D.allowGroupJoinPrompt} disabled={loading || D.joinState !== 'open'} />
						<span class="space-y-1">
							<span class="block text-sm">{m.dialog_group_edit_allow_join_prompt()}</span>
							<span class="block text-xs text-muted-foreground">{m.dialog_group_edit_allow_join_prompt_description()}</span>
						</span>
					</label>
				{/if}

				<div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
					<div class="flex flex-col gap-2">
						{@render field('Banner')}
						<div class="space-y-2">
							{#if D.bannerUrl}<img src={D.bannerUrl} alt="Banner" class="aspect-17/6 h-24 w-full rounded-md object-cover" />{/if}
							<div class="flex gap-2">
								<Button variant="outline" size="sm" disabled={loading} onclick={() => showGallery(false)}>{m.dialog_group_edit_select_image()}</Button>
								{#if D.bannerUrl}<Button variant="outline" size="sm" disabled={loading} onclick={() => ((D.bannerId = ''), (D.bannerUrl = ''))}>{m.dialog_group_edit_clear_image()}</Button>{/if}
							</div>
						</div>
					</div>
					<div class="flex flex-col gap-2">
						{@render field('Icon')}
						<div class="space-y-2">
							{#if D.iconUrl}<img src={D.iconUrl} alt="Icon" class="aspect-square h-24 w-24 rounded-md object-cover" />{/if}
							<div class="flex gap-2">
								<Button variant="outline" size="sm" disabled={loading} onclick={() => showGallery(true)}>{m.dialog_group_edit_select_image()}</Button>
								{#if D.iconUrl}<Button variant="outline" size="sm" disabled={loading} onclick={() => ((D.iconId = ''), (D.iconUrl = ''))}>{m.dialog_group_edit_clear_image()}</Button>{/if}
							</div>
						</div>
					</div>
				</div>
			</div>
		</div>

		<Dialog.Footer class="px-6 py-4">
			<Button variant="secondary" disabled={loading} onclick={close}>{m.dialog_group_transfer_cancel()}</Button>
			<Button disabled={!canSubmit} onclick={save}>{loading ? 'Saving...' : isEditMode ? 'Save Changes' : 'Create Group'}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<GallerySelectDialog bind:open={gallery.open} icon={gallery.icon} onselect={selectImage} />
