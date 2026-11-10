<script lang="ts">
	import { FORM_FIELD } from '#lib/styles.js';
	import { m } from '#lib/paraglide/messages.js';
	import { Tabs as TabsPrimitive } from 'bits-ui';
	import { untrack } from 'svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import OptionPicker, { type PickerOption } from '#lib/components/common/option-picker.svelte';
	import MultiSelect from '#lib/components/common/multi-select.svelte';
	import CountedInput from '#lib/components/group/counted-input.svelte';
	import { languageOptions } from '#lib/groups/group-dialog.svelte.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import { TAB_LIST, TAB_LIST_WRAP, TAB_TRIGGER } from '#lib/details.js';
	import { buildLegacyInstanceTag, canOpenInGame, createInstance, getShortNames, instanceCatalog, instanceDialogs, launchUrl, loadInstanceCategoriesAndVibes, openInGame, loadInstanceChoices, REGIONS, saveInstanceChoices, selfInvite, type CreatedInstance } from '#lib/instances/instance-launch.svelte.js';
	import { INSTANCE_CATEGORY_ICONS, MAX_INSTANCE_TAGS } from '#lib/instances/instance-create.js';
	import { parseLocation } from '#lib/instances/location.js';
	import { sidebarData } from '#lib/people/sidebar-data.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';
	import { toast } from 'svelte-sonner';

	// The original's NewInstanceDialog and useNewInstanceBuilder.
	const ACCESS = [['public', 'Public'], ['group', 'Group'], ['friends+', 'Friends+'], ['friends', 'Friends'], ['invite+', 'Invite+'], ['invite', 'Invite']];
	const PERFORMANCE = ['None', 'Poor', 'Medium', 'Good'];
	const LABEL = 'text-sm leading-snug font-medium';

	type OptionGroup = { id: string; name: string; iconUrl?: string | null; privacy?: string | null; permissions: string[]; canCreateInstance: boolean };
	type Options = { displayName: string; isSupporter: boolean; ageVerified?: boolean; currentLocation?: string | null; groups: OptionGroup[] };
	type Role = { id: string; name: string };
	type Friend = { userId: string; displayName: string; state: string; thumbnailImageUrl?: string | null };

	const saved = $state(loadInstanceChoices());

	let tab = $state('Normal');
	let options = $state<Options | undefined>();
	let friends = $state<Friend[]>([]);
	let roles = $state<Role[]>([]);
	let roleIds = $state<string[]>([]);
	let worldId = $state('');
	let legacyTag = $state('');
	let created = $state<CreatedInstance | undefined>();
	let shortName = $state('');
	let error = $state('');
	let notice = $state('');
	let busy = $state(false);
	let languageNames = $state<Record<string, string>>({});

	const owner = $derived(vrchatAuth.ownerUserId);
	const group = $derived(options?.groups.find((g) => g.id === saved.groupId));
	const can = (permission: string) => !!group && (group.permissions.includes('*') || group.permissions.includes(permission));
	const isGroup = $derived(saved.accessType === 'group');
	// Group instances need the group's permission; any other one an 18+ verified creator.
	const canAgeGate = $derived(isGroup ? can('group-instance-age-gated-create') : !!options?.ageVerified);
	const languageList = $derived(Object.entries(languageNames).map(([value, label]) => ({ value, label })));
	const vibeList = $derived(instanceCatalog.vibes.map((v) => ({ value: v.id, label: v.title })));
	const categoryName = $derived(instanceCatalog.categories.find((c) => c.id === saved.categoryId)?.name);
	const groupOptions = $derived<PickerOption[]>((options?.groups ?? []).filter((g) => g.canCreateInstance).map((g) => ({ value: g.id, label: g.name, image: g.iconUrl ?? null })));
	const legacyGroupOptions = $derived<PickerOption[]>((options?.groups ?? []).map((g) => ({ value: g.id, label: g.name, image: g.iconUrl ?? null })));
	const creatorOptions = $derived<PickerOption[]>([
		{ value: owner, label: options?.displayName ?? owner, section: 'Me', image: null },
		// The original's sections: favorites online, then the rest online, active and offline.
		...friends.filter((f) => f.state === 'online' && sidebarData.vipIds.has(f.userId)).map((f) => creator(f, 'FAVORITES')),
		...friends.filter((f) => f.state === 'online' && !sidebarData.vipIds.has(f.userId)).map((f) => creator(f, 'Online')),
		...friends.filter((f) => f.state === 'active').map((f) => creator(f, 'Active')),
		...friends.filter((f) => f.state !== 'online' && f.state !== 'active').map((f) => creator(f, 'Offline'))
	]);
	function creator(f: Friend, section: string): PickerOption {
		return { value: f.userId, label: f.displayName, section, image: f.thumbnailImageUrl ?? null };
	}
	const legacyLocation = $derived(worldId && legacyTag ? `${worldId}:${legacyTag}` : worldId);
	const shownLocation = $derived(tab === 'Legacy' ? legacyLocation : (created?.location ?? ''));
	const shownUrl = $derived.by(() => {
		const L = parseLocation(shownLocation);
		return shownLocation ? launchUrl({ worldId: L.worldId, instanceId: L.instanceId, shortName }) : '';
	});
	const canInviteOthers = $derived(!((saved.accessType === 'friends' || saved.accessType === 'invite') && (tab === 'Legacy' ? saved.legacyUserId || owner : owner) !== owner));

	function loadRoles() {
		const id = saved.groupId;
		roleIds = [];
		roles = [];
		if (!id) return;
		api.get<Role[]>(accountPath(owner, `/groups/${encodeURIComponent(id)}/roles`)).then(
			(data) => {
				if (saved.groupId === id) roles = data;
			},
			() => {}
		);
	}

	/** buildInstance and buildLegacyInstance: any change forgets the instance made from the old choices. */
	function changed() {
		created = undefined;
		shortName = '';
		error = '';
		notice = '';
		saved.instanceName = saved.instanceName.replace(/[^A-Za-z0-9]/g, '');
		const name = saved.instanceName || String(Math.floor(99999 * Math.random() + 1)).padStart(5, '0');
		legacyTag = buildLegacyInstanceTag({
			instanceName: name,
			userId: saved.legacyUserId || owner,
			accessType: saved.accessType,
			groupId: saved.groupId,
			groupAccessType: saved.groupAccessType,
			region: saved.region,
			ageGate: saved.ageGate
		});
		saveInstanceChoices(saved, owner);
	}

	$effect(() => {
		const opened = instanceDialogs.newInstance;
		if (!opened) return;
		// Reopening for the same world keeps the last instance made, as the original does.
		if (untrack(() => worldId) === opened.worldId) return;
		worldId = opened.worldId;
		tab = 'Normal';
		options = undefined;
		// Only opening the dialog sets it up, not the choices this reads.
		untrack(changed);
		const account = owner;
		api.get<Options>(accountPath(account, '/instances/options')).then(
			(data) => {
				options = data;
				loadRoles();
			},
			(err) => (error = ApiError.from(err).message)
		);
		api.get<Friend[]>(accountPath(account, '/friends')).then((data) => (friends = data), () => (friends = []));
		languageOptions(account).then((names) => (languageNames = names));
		void loadInstanceCategoriesAndVibes();
	});

	async function create() {
		busy = true;
		error = '';
		try {
			created = await createInstance(worldId, saved, { roleIds, canAgeGate });
			shortName = created.shortName ?? '';
		} catch (err) {
			error = ApiError.from(err).message;
		} finally {
			busy = false;
		}
	}

	async function copyUrl() {
		error = '';
		const L = parseLocation(shownLocation);
		const names = await getShortNames(shownLocation).catch(() => ({ shortName: '' }));
		shortName = names.shortName ?? '';
		try {
			await navigator.clipboard.writeText(launchUrl({ worldId: L.worldId, instanceId: L.instanceId, shortName }));
			toast.success('Copied successfully!');
		} catch {
			error = 'Copy failed';
			toast.error(error);
		}
	}

	async function sendSelfInvite() {
		error = '';
		try {
			await selfInvite(shownLocation, shortName);
			toast.success(m.message_invite_self_sent());
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	async function openHere() {
		error = '';
		try {
			if ((await openInGame(shownLocation, shortName)) === 'invited') {
				toast.warning('Failed open instance in VRChat, falling back to self invite');
				toast.success(m.message_invite_self_sent());
			}
		} catch (err) {
			error = ApiError.from(err).message;
		}
	}

	const showLaunch = () => (instanceDialogs.launch = { location: shownLocation, shortName });
	const showInvite = () => (instanceDialogs.invite = { location: shownLocation });
</script>

{#snippet toggles(label: string, value: string, items: { value: string; label: string; disabled?: boolean }[], set: (v: string) => void)}
	<div class={FORM_FIELD}>
		<span class={LABEL}>{label}</span>
		<ToggleGroup.Root type="single" variant="outline" size="sm" {value} onValueChange={(next) => { if (next) { set(next); changed(); } }}>
			{#each items as item (item.value)}
				<ToggleGroup.Item value={item.value} disabled={item.disabled}>{item.label}</ToggleGroup.Item>
			{/each}
		</ToggleGroup.Root>
	</div>
{/snippet}

{#snippet accessToggles()}
	{@render toggles('Access Type', saved.accessType, ACCESS.map(([value, label]) => ({ value, label })), (v) => (saved.accessType = v))}
{/snippet}

{#snippet regionToggles()}
	{@render toggles('Region', saved.region, REGIONS.map((r) => ({ value: r.value, label: r.value })), (v) => (saved.region = v))}
{/snippet}

{#snippet groupPicker(items: PickerOption[])}
	<div class={FORM_FIELD}>
		<span class={LABEL}>{m.dialog_new_instance_group()}</span>
		<OptionPicker options={items} bind:values={() => (saved.groupId ? [saved.groupId] : []), (v) => (saved.groupId = v[0] ?? '')} placeholder={m.dialog_new_instance_group_placeholder()} onchange={() => { loadRoles(); changed(); }} />
	</div>
{/snippet}

{#snippet readonlyRows()}
	<div class={FORM_FIELD}>
		<span class={LABEL}>{m.dialog_new_instance_location()}</span>
		<Input value={shownLocation} readonly class="h-8" onclick={(event) => event.currentTarget.select()} />
	</div>
	<div class={FORM_FIELD}>
		<span class={LABEL}>{m.dialog_new_instance_url()}</span>
		<Input value={shownUrl} readonly class="h-8" />
	</div>
{/snippet}

<Dialog.Root bind:open={() => !!instanceDialogs.newInstance, (open) => { if (!open) instanceDialogs.newInstance = undefined; }}>
	<Dialog.Content class="max-h-[90vh] overflow-y-auto bg-background sm:max-w-2xl" onOpenAutoFocus={(event) => event.preventDefault()}>
		<Dialog.Header><Dialog.Title>{m.dialog_new_instance_header()}</Dialog.Title></Dialog.Header>
		<TabsPrimitive.Root bind:value={() => tab, (next) => { tab = next; changed(); }}>
			<div class={TAB_LIST_WRAP}>
				<TabsPrimitive.List class={TAB_LIST}>
					<TabsPrimitive.Trigger value="Normal" class={TAB_TRIGGER}>{m.dialog_new_instance_normal()}</TabsPrimitive.Trigger>
					<TabsPrimitive.Trigger value="Legacy" class={TAB_TRIGGER}>{m.dialog_new_instance_legacy()}</TabsPrimitive.Trigger>
				</TabsPrimitive.List>
			</div>
			<TabsPrimitive.Content value="Normal" class="mt-4 flex flex-col gap-4">
				{@render accessToggles()}
				{#if isGroup}
					{@render toggles('Group Access', saved.groupAccessType, [
						{ value: 'members', get label() { return m.dialog_new_instance_group_access_type_members(); }, disabled: !can('group-instance-open-create') },
						{ value: 'plus', get label() { return m.dialog_new_instance_group_access_type_plus(); }, disabled: !can('group-instance-plus-create') },
						{ value: 'public', get label() { return m.dialog_new_instance_access_type_public(); }, disabled: !can('group-instance-public-create') || group?.privacy === 'private' }
					], (v) => (saved.groupAccessType = v))}
				{/if}
				{@render regionToggles()}
				{#if isGroup}
					<div class={FORM_FIELD}>
						<span class={LABEL}>{m.dialog_new_instance_queueEnabled()}</span>
						<Checkbox bind:checked={saved.queueEnabled} onCheckedChange={changed} />
					</div>
				{/if}
				{#if canAgeGate}
					<div class={FORM_FIELD}>
						<span class={LABEL}>{m.dialog_new_instance_ageGate()}</span>
						<Checkbox bind:checked={saved.ageGate} onCheckedChange={changed} />
					</div>
				{/if}
				<!-- The text fields count against VRChat's limits; a change forgets the instance made before it. -->
				<div class={FORM_FIELD} onchange={changed}>
					<span class={LABEL}>{m.dialog_new_instance_display_name()}</span>
					<CountedInput bind:value={saved.displayName} maxlength={128} />
				</div>
				<div class={FORM_FIELD} onchange={changed}>
					<span class={LABEL}>{m.dialog_new_instance_description()}</span>
					<CountedInput bind:value={saved.description} maxlength={256} rows={3} />
				</div>
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_category()}</span>
					<Select.Root type="single" value={saved.categoryId || 'none'} onValueChange={(v) => { saved.categoryId = v === 'none' ? '' : v; changed(); }}>
						<Select.Trigger size="sm" class="w-full"><span class="truncate">{categoryName || m.dialog_new_instance_none()}</span></Select.Trigger>
						<Select.Content>
							<Select.Item value="none">{m.dialog_new_instance_none()}</Select.Item>
							{#each instanceCatalog.categories as category (category.id)}
								<Select.Item value={category.id} label={category.name}>
									{#if category.iconUrl && INSTANCE_CATEGORY_ICONS[category.iconUrl]}<i class={INSTANCE_CATEGORY_ICONS[category.iconUrl]}></i>{/if}
									{category.name}
								</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_vibes()}</span>
					<MultiSelect values={saved.vibeIds} options={vibeList} placeholder={m.dialog_new_instance_none()} class="h-8 w-full" onchange={(values) => { saved.vibeIds = values.slice(0, MAX_INSTANCE_TAGS); changed(); }} />
				</div>
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_languages()}</span>
					<MultiSelect values={saved.languages} options={languageList} placeholder={m.dialog_group_edit_no_language()} class="h-8 w-full" onchange={(values) => { saved.languages = values.slice(0, MAX_INSTANCE_TAGS); changed(); }} />
				</div>
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_minimum_avatar_performance()}</span>
					<Select.Root type="single" value={saved.minimumAvatarPerformance || 'None'} onValueChange={(v) => { saved.minimumAvatarPerformance = v; changed(); }}>
						<Select.Trigger size="sm" class="w-full"><span>{saved.minimumAvatarPerformance || 'None'}</span></Select.Trigger>
						<Select.Content>
							{#each PERFORMANCE as level (level)}<Select.Item value={level}>{level}</Select.Item>{/each}
						</Select.Content>
					</Select.Root>
				</div>
				{#if isGroup}
					{@render groupPicker(groupOptions)}
					{#if saved.groupAccessType === 'members'}
						<div class={FORM_FIELD}>
							<span class={LABEL}>{m.dialog_new_instance_roles()}</span>
							<OptionPicker options={roles.map((r) => ({ value: r.id, label: r.name }))} bind:values={roleIds} multiple placeholder={m.dialog_new_instance_role_placeholder()} onchange={() => { created = undefined; }} />
						</div>
					{/if}
				{/if}
				{#if created}{@render readonlyRows()}{/if}
			</TabsPrimitive.Content>
			<TabsPrimitive.Content value="Legacy" class="mt-4 flex flex-col gap-4">
				{@render accessToggles()}
				{#if isGroup}
					{@render toggles('Group Access', saved.groupAccessType, [{ value: 'members', get label() { return m.dialog_new_instance_group_access_type_members(); } }, { value: 'plus', get label() { return m.dialog_new_instance_group_access_type_plus(); } }, { value: 'public', get label() { return m.dialog_new_instance_access_type_public(); } }], (v) => (saved.groupAccessType = v))}
				{/if}
				{@render regionToggles()}
				{#if isGroup}
					<div class={FORM_FIELD}>
						<span class={LABEL}>{m.dialog_new_instance_ageGate()}</span>
						<Checkbox bind:checked={saved.ageGate} onCheckedChange={changed} />
					</div>
				{/if}
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_world_id()}</span>
					<Input bind:value={worldId} class="h-8" onchange={changed} onclick={(event) => event.currentTarget.select()} />
				</div>
				<div class={FORM_FIELD}>
					<span class={LABEL}>{m.dialog_new_instance_instance_id()}</span>
					<Input bind:value={saved.instanceName} placeholder={m.dialog_new_instance_instance_id_placeholder()} class="h-8" onchange={changed} />
				</div>
				{#if saved.accessType !== 'public' && !isGroup}
					<div class={FORM_FIELD}>
						<span class={LABEL}>{m.dialog_new_instance_instance_creator()}</span>
						<OptionPicker options={creatorOptions} bind:values={() => (saved.legacyUserId ? [saved.legacyUserId] : []), (v) => (saved.legacyUserId = v[0] ?? '')} placeholder={m.dialog_new_instance_instance_creator_placeholder()} onchange={changed} />
					</div>
				{/if}
				{#if isGroup}{@render groupPicker(legacyGroupOptions)}{/if}
				{@render readonlyRows()}
			</TabsPrimitive.Content>
		</TabsPrimitive.Root>
		{#if error}<p class="text-xs text-destructive">{error}</p>{:else if notice}<p class="text-xs text-muted-foreground">{notice}</p>{/if}
		<Dialog.Footer>
			{#if tab === 'Legacy' || created}
				<Button variant="outline" class="mr-2" onclick={copyUrl}>{m.dialog_new_instance_copy_url()}</Button>
				<Button variant="outline" class="mr-2" onclick={sendSelfInvite}>{m.dialog_new_instance_self_invite()}</Button>
				<Button variant="outline" class="mr-2" disabled={!canInviteOthers} onclick={showInvite}>{m.dialog_new_instance_invite()}</Button>
				{#if canOpenInGame()}
					<Button variant="secondary" class="mr-2" onclick={showLaunch}>{m.dialog_new_instance_launch()}</Button>
					<Button onclick={openHere}>{m.dialog_new_instance_open_ingame()}</Button>
				{:else}
					<Button onclick={showLaunch}>{m.dialog_new_instance_launch()}</Button>
				{/if}
			{:else}
				<Button disabled={busy || (isGroup && !saved.groupId)} onclick={create}>{m.dialog_new_instance_create_instance()}</Button>
			{/if}
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
