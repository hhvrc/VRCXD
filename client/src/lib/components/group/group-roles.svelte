<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { toast } from 'svelte-sonner';
	import GripVertical from '@lucide/svelte/icons/grip-vertical';
	import Plus from '@lucide/svelte/icons/plus';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import UserMinus from '@lucide/svelte/icons/user-minus';
	import UserPlus from '@lucide/svelte/icons/user-plus';
	import ActionDialogs, { type Confirming } from '#lib/components/dialogs/action-dialogs.svelte';
	import CountedInput from '#lib/components/group/counted-input.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import { Checkbox } from '#lib/components/ui/checkbox/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api } from '#lib/api/client.js';
	import { hasGroupPermission, notifyGroupChanged, type Group, type GroupRole } from '#lib/groups/group-dialog.svelte.js';
	import { replaceBioSymbols } from '#lib/helpers/format.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's GroupRolesDialog (with SortableGroupRoleItem): the group's roles in order, split into
	// management and member roles, each editable as far as your own roles allow, dragged to reorder.
	let { groupId }: { groupId: string } = $props();

	type Permission = { name: string; displayName?: string; help?: string; isManagementPermission?: boolean; allowedToAdd?: boolean };
	type Form = {
		mode: '' | 'create' | 'edit';
		roleId: string;
		name: string;
		description: string;
		isSelfAssignable: boolean;
		isAddedOnJoin: boolean;
		requiresTwoFactor: boolean;
		requiresPurchase: boolean;
		isDefaultRole: boolean;
		isOwnerRole: boolean;
		permissions: string[];
	};

	const blank = (): Form => ({
		mode: '',
		roleId: '',
		name: '',
		description: '',
		isSelfAssignable: false,
		isAddedOnJoin: false,
		requiresTwoFactor: false,
		requiresPurchase: false,
		isDefaultRole: false,
		isOwnerRole: false,
		permissions: []
	});

	let group = $state<Group | undefined>();
	let roles = $state<GroupRole[]>([]);
	let permissions = $state<Permission[]>([]);
	let loading = $state(true);
	let form = $state<Form>(blank());
	let confirming = $state<Confirming | undefined>();
	let dragging = $state<{ section: string; index: number } | undefined>();

	const owner = $derived(vrchatAuth.ownerUserId);
	const base = $derived(accountPath(owner, `/groups/${encodeURIComponent(groupId)}`));
	const canManage = $derived(hasGroupPermission(group, 'group-roles-manage'));
	const canAssignRoles = $derived(hasGroupPermission(group, 'group-roles-assign'));
	const canManageDefaultRole = $derived(hasGroupPermission(group, 'group-default-role-manage'));
	const myRoleIds = $derived(group?.myMember?.roleIds ?? []);
	const myHighestRoleOrder = $derived(Math.min(...roles.filter((r) => myRoleIds.includes(r.id)).map((r) => r.order)));
	const sections = $derived(
		[
			{ group: 'management', get label() { return m.dialog_group_roles_management_roles(); }, roles: roles.filter((r) => r.isManagementRole) },
			{ group: 'member', get label() { return m.dialog_group_roles_member_roles(); }, roles: roles.filter((r) => !r.isManagementRole) }
		].filter((s) => s.roles.length > 0)
	);
	const selectedRole = $derived(roles.find((r) => r.id === form.roleId));
	const isReadOnly = $derived(
		(form.isDefaultRole ? !canManageDefaultRole : !canManage) || (form.mode === 'edit' && !!selectedRole && !canModifyRole(selectedRole)) || loading
	);
	const isDetailsReadOnly = $derived(isReadOnly || form.isDefaultRole);
	const isFlagsReadOnly = $derived(isDetailsReadOnly || form.isOwnerRole);
	const isPermissionsReadOnly = $derived(isReadOnly || form.isOwnerRole);
	const canSave = $derived(!isReadOnly && form.name.trim().length > 0);
	const assignable = $derived(permissions.filter((p) => p.name !== '*'));
	const standardPermissions = $derived(assignable.filter((p) => p.isManagementPermission !== true));
	const managementPermissions = $derived(assignable.filter((p) => p.isManagementPermission === true));
	const title = $derived(canManage ? 'Manage Group Roles' : 'Group Roles');

	const isOwnerRole = (role: GroupRole) => !!role.permissions?.includes('*');
	const isMyRole = (role: GroupRole) => myRoleIds.includes(role.id);
	function canModifyRole(role: GroupRole) {
		if (role.defaultRole) return true;
		if (isOwnerRole(role)) return isMyRole(role);
		return role.order > myHighestRoleOrder;
	}
	const isLockedRole = (role: GroupRole) => role.defaultRole === true || isOwnerRole(role) || !canModifyRole(role);
	function canToggleSelfRole(role: GroupRole) {
		if (role.defaultRole || isOwnerRole(role)) return false;
		if (group?.myMember?.membershipStatus !== 'member') return false;
		return role.isSelfAssignable === true || (canAssignRoles && role.order > myHighestRoleOrder);
	}
	const canEditRoles = (g: Group) => hasGroupPermission(g, 'group-roles-manage') || hasGroupPermission(g, 'group-default-role-manage');
	const sorted = (list: GroupRole[]) => [...list].sort((a, b) => a.order - b.order);

	function setRoles(list: GroupRole[]) {
		roles = sorted(list);
		// The selected role follows a reload; one that went away clears the form.
		if (form.mode === 'create' || !form.roleId) return;
		const role = roles.find((r) => r.id === form.roleId);
		form = role ? formOf(role) : blank();
	}

	/** loadViewOnlyGroupPermissions: the list from a group you manage, else the names the roles hold. */
	async function loadViewOnlyPermissions() {
		try {
			const options = await api.get<{ groups: { id: string; permissions?: string[] }[] }>(accountPath(owner, '/instances/options'));
			const managed = options.groups.find((g) => g.permissions?.includes('*') || g.permissions?.includes('group-roles-manage'));
			if (managed) {
				const list = await api.get<Permission[]>(accountPath(owner, `/groups/${encodeURIComponent(managed.id)}/permissions`));
				permissions = list.map((p) => ({ ...p, allowedToAdd: false }));
				return;
			}
		} catch {
			// falls through to the names the roles hold
		}
		permissions = [...new Set(roles.flatMap((r) => r.permissions ?? []))].sort().map((name) => ({ name, allowedToAdd: false }));
	}

	/** loadGroupRolesDialogRoles. */
	async function loadRoles() {
		loading = true;
		try {
			if (!group || !canEditRoles(group)) {
				const details = await api.get<{ group: Group }>(`${base}/details`);
				group = details.group;
				setRoles(details.group.roles ?? []);
				return;
			}
			const list = await api.get<GroupRole[]>(`${base}/roles`);
			setRoles(list.map((r) => ({ ...r, name: replaceBioSymbols(r.name), description: replaceBioSymbols(r.description ?? '') })));
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}

	/** showGroupRolesDialog. */
	async function open() {
		loading = true;
		try {
			const details = await api.get<{ group: Group }>(`${base}/details`);
			group = details.group;
			userDialog.setLabel(groupId, details.group.name);
			if (!canEditRoles(details.group)) {
				setRoles(details.group.roles ?? []);
				await loadViewOnlyPermissions();
				loading = false;
				return;
			}
			await loadRoles();
			api.get<Permission[]>(`${base}/permissions`).then((list) => (permissions = list)).catch(() => {});
		} catch (err) {
			loading = false;
			toastError(err);
		}
	}

	$effect(() => {
		if (owner && groupId) open();
	});

	function formOf(role: GroupRole): Form {
		return {
			mode: 'edit',
			roleId: role.id,
			name: role.name,
			description: role.description ?? '',
			isSelfAssignable: role.isSelfAssignable === true,
			isAddedOnJoin: role.isAddedOnJoin === true,
			requiresTwoFactor: role.requiresTwoFactor === true,
			requiresPurchase: role.requiresPurchase === true,
			isDefaultRole: role.defaultRole === true,
			isOwnerRole: isOwnerRole(role),
			permissions: [...(role.permissions ?? [])]
		};
	}

	const hasPermission = (name: string) => form.permissions.includes('*') || form.permissions.includes(name);
	function togglePermission(name: string, checked: boolean) {
		if (checked) {
			if (!form.permissions.includes(name)) form.permissions.push(name);
		} else form.permissions = form.permissions.filter((p) => p !== name);
	}

	async function saveRole() {
		if (!canSave) return;
		const body: Record<string, unknown> = {};
		if (!isDetailsReadOnly) {
			body.name = form.name.trim();
			body.description = form.description;
			body.requiresTwoFactor = form.requiresTwoFactor;
		}
		if (!isFlagsReadOnly) {
			body.isSelfAssignable = form.isSelfAssignable;
			body.isAddedOnJoin = form.isAddedOnJoin;
			body.requiresPurchase = form.requiresPurchase;
		}
		if (!isPermissionsReadOnly) body.permissions = [...form.permissions];
		loading = true;
		try {
			let roleId = form.roleId;
			if (form.mode === 'create') {
				// VRChat answers a new role with the roles, the new one among them.
				const created = await api.post<GroupRole | GroupRole[]>(`${base}/roles`, body);
				roleId = Array.isArray(created) ? (created.find((r) => !roles.some((old) => old.id === r.id))?.id ?? '') : created.id;
			} else {
				await api.put(`${base}/roles/${encodeURIComponent(roleId)}`, body);
			}
			form.mode = roleId ? 'edit' : '';
			form.roleId = roleId;
			await loadRoles();
			notifyGroupChanged(groupId);
			toast.success(m.dialog_group_roles_saved());
		} catch {
			toast.error(m.dialog_group_roles_save_failed());
		} finally {
			loading = false;
		}
	}

	function deleteRole(role: GroupRole) {
		confirming = {
			get title() { return m.confirm_title(); },
			description: `Delete role "${role.name}"? This cannot be undone.`,
			destructive: true,
			run: async () => {
				confirming = undefined;
				loading = true;
				try {
					await api.delete(`${base}/roles/${encodeURIComponent(role.id)}`);
					if (form.roleId === role.id) form = blank();
					await loadRoles();
					notifyGroupChanged(groupId);
					toast.success(m.dialog_group_roles_deleted());
				} catch {
					toast.error(m.dialog_group_roles_delete_failed());
				} finally {
					loading = false;
				}
			}
		};
	}

	async function toggleSelfRole(role: GroupRole) {
		const removing = isMyRole(role);
		const path = `${base}/members/${encodeURIComponent(owner)}/roles/${encodeURIComponent(role.id)}`;
		loading = true;
		try {
			const roleIds = removing ? await api.delete<string[]>(path) : await api.put<string[]>(path);
			if (group?.myMember) group.myMember.roleIds = roleIds;
			notifyGroupChanged(groupId);
			toast.success(removing ? 'Role removed' : 'Role added');
		} catch {
			toast.error(m.dialog_group_roles_self_role_failed());
		} finally {
			loading = false;
		}
	}

	/** onDragEnd: moves within a section only, never past a locked role, then writes the new orders. */
	async function drop(section: string, index: number) {
		const from = dragging;
		dragging = undefined;
		if (!from || from.section !== section || from.index === index) return;
		const before = sections.flatMap((s) => s.roles);
		const inSection = [...(sections.find((s) => s.group === section)?.roles ?? [])];
		const [moved] = inSection.splice(from.index, 1);
		inSection.splice(index, 0, moved);
		const reordered = sections.flatMap((s) => (s.group === section ? inSection : s.roles));
		if (reordered.some((role, i) => isLockedRole(role) && role.id !== before[i].id)) return;
		const orders = roles.map((r) => r.order);
		roles = reordered;
		loading = true;
		try {
			for (let i = 0; i < reordered.length; i++) {
				if (reordered[i].id !== before[i].id) await api.put(`${base}/roles/${encodeURIComponent(reordered[i].id)}`, { order: orders[i] });
			}
		} catch {
			toast.error(m.dialog_group_roles_save_failed());
		} finally {
			await loadRoles();
			loading = false;
		}
	}
</script>

{#snippet flag(label: string, help: string, checked: boolean, disabled: boolean, set: (v: boolean) => void)}
	<label class="inline-flex cursor-pointer items-start gap-2 rounded-md px-2 py-1.5 hover:bg-accent/50">
		<Checkbox {checked} {disabled} onCheckedChange={(v) => set(v === true)} />
		<span class="space-y-1">
			<span class="block text-sm">{label}</span>
			{#if help}<span class="block text-xs text-muted-foreground">{help}</span>{/if}
		</span>
	</label>
{/snippet}

{#snippet permissionList(label: string, list: Permission[])}
	{#if list.length > 0}
		<div class="flex flex-col gap-2">
			<span class="text-sm font-medium">{label}</span>
			<div class="grid grid-cols-1 gap-x-2 gap-y-0.5 xl:grid-cols-2">
				{#each list as permission (permission.name)}
					{@render flag(permission.displayName || permission.name, permission.help ?? '', hasPermission(permission.name), isPermissionsReadOnly || !permission.allowedToAdd, (v) => togglePermission(permission.name, v))}
				{/each}
			</div>
		</div>
	{/if}
{/snippet}

<div class="flex min-h-0 flex-1 flex-col">
	<h3>{title} <span class="text-muted-foreground">- {group?.name ?? ''}</span></h3>
	<div class="mt-2 flex min-h-0 flex-1 gap-4">
		<div class="flex min-h-0 w-80 shrink-0 flex-col border-r pr-4">
			<div class="mb-2 flex items-center gap-2">
				<Button size="sm" disabled={!canManage || loading} onclick={() => (form = { ...blank(), mode: 'create' })}><Plus /> {m.dialog_group_roles_new_role()}</Button>
				<Button class="ml-auto" variant="ghost" size="icon-sm" aria-label={m.dialog_group_actions_refresh()} disabled={loading} onclick={loadRoles}><RefreshCw class={loading ? 'animate-spin' : ''} /></Button>
			</div>
			<div class="flex min-h-0 flex-1 flex-col gap-1.5 overflow-y-auto">
				{#if !loading && roles.length === 0}
					<div class="p-2 text-sm text-muted-foreground">{m.dialog_group_roles_no_roles()}</div>
				{/if}
				{#each sections as section (section.group)}
					<div class="px-1 pt-2 text-xs font-medium text-muted-foreground first:pt-0">{section.label}</div>
					{#each section.roles as role, index (role.id)}
						{@const draggable = canManage && !isLockedRole(role) && !loading}
						<div
							role="button"
							tabindex="0"
							class={['group flex cursor-pointer items-center gap-2 rounded-md border px-2 py-1.5 hover:bg-accent', form.mode === 'edit' && form.roleId === role.id && 'bg-accent', dragging?.section === section.group && dragging.index === index && 'opacity-50']}
							{draggable}
							ondragstart={() => (dragging = { section: section.group, index })}
							ondragend={() => (dragging = undefined)}
							ondragover={(e) => dragging?.section === section.group && e.preventDefault()}
							ondrop={(e) => {
								e.preventDefault();
								drop(section.group, index);
							}}
							onclick={() => (form = formOf(role))}
							onkeydown={(e) => e.key === 'Enter' && (form = formOf(role))}>
							<div class={['shrink-0 select-none', draggable ? 'cursor-grab active:cursor-grabbing' : 'invisible']}><GripVertical class="size-4 text-muted-foreground" /></div>
							<div class="min-w-0 flex-1">
								<div class="truncate text-sm font-medium">{role.name}</div>
								<div class="mt-0.5 flex flex-wrap gap-1">
									{#if isMyRole(role)}<Badge class="px-1 py-0 text-3xs">{m.dialog_group_roles_assigned_to_you()}</Badge>{/if}
									{#if role.defaultRole}<Badge variant="secondary" class="px-1 py-0 text-3xs">{m.dialog_group_roles_default_role()}</Badge>{/if}
									{#if role.isSelfAssignable}<Badge variant="outline" class="px-1 py-0 text-3xs">{m.dialog_group_roles_self_assignable()}</Badge>{/if}
									{#if role.isAddedOnJoin}<Badge variant="outline" class="px-1 py-0 text-3xs">{m.dialog_group_roles_added_on_join()}</Badge>{/if}
									{#if role.requiresTwoFactor}<Badge variant="outline" class="px-1 py-0 text-3xs">{m.dialog_group_roles_requires_two_factor()}</Badge>{/if}
									{#if role.requiresPurchase}<Badge variant="outline" class="px-1 py-0 text-3xs">{m.dialog_group_roles_requires_purchase()}</Badge>{/if}
								</div>
							</div>
							{#if canManage && !isLockedRole(role)}
								<div class="flex shrink-0">
									<Tooltip.Root delayDuration={500}>
										<Tooltip.Trigger>
											{#snippet child({ props })}
												<Button
													{...props}
													variant="ghost"
													size="icon-sm"
													aria-label={m.dialog_group_roles_delete()}
													disabled={loading}
													onclick={(e: MouseEvent) => {
														e.stopPropagation();
														deleteRole(role);
													}}><Trash2 class="text-destructive" /></Button>
											{/snippet}
										</Tooltip.Trigger>
										<Tooltip.Content>{m.dialog_group_roles_delete()}</Tooltip.Content>
									</Tooltip.Root>
								</div>
							{/if}
						</div>
					{/each}
				{/each}
			</div>
		</div>
		<div class="flex min-h-0 flex-1 flex-col">
			{#if !form.mode}
				<div class="p-2 text-sm text-muted-foreground">{m.dialog_group_roles_no_role_selected()}</div>
			{:else}
				<div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto pr-2">
					<div class="flex flex-col gap-4">
						<div class="flex flex-col gap-2">
							<span class="text-sm font-medium">{m.dialog_group_roles_name()}</span>
							<CountedInput bind:value={form.name} maxlength={64} disabled={isDetailsReadOnly} />
						</div>
						<div class="flex flex-col gap-2">
							<span class="text-sm font-medium">{m.dialog_group_roles_description()}</span>
							<CountedInput bind:value={form.description} maxlength={512} rows={3} disabled={isDetailsReadOnly} />
						</div>
						<div class="flex flex-col gap-0.5">
							{@render flag('Self Assignable', 'Members can assign this role to themselves', form.isSelfAssignable, isFlagsReadOnly, (v) => (form.isSelfAssignable = v))}
							{@render flag('Added On Join', 'Automatically given to new members', form.isAddedOnJoin, isFlagsReadOnly, (v) => (form.isAddedOnJoin = v))}
							{@render flag('Requires 2FA', 'Members need two-factor authentication enabled to use this role', form.requiresTwoFactor, isDetailsReadOnly, (v) => (form.requiresTwoFactor = v))}
							{@render flag('Requires Purchase', 'Members must purchase a group subscription to get this role', form.requiresPurchase, isFlagsReadOnly, (v) => (form.requiresPurchase = v))}
						</div>
						{@render permissionList('Permissions', standardPermissions)}
						{@render permissionList('Management Permissions', managementPermissions)}
					</div>
				</div>
				<div class="flex justify-end gap-2 pt-4">
					{#if selectedRole && canToggleSelfRole(selectedRole)}
						{@const role = selectedRole}
						<Button class="mr-auto" variant="outline" disabled={loading} onclick={() => toggleSelfRole(role)}>
							{#if isMyRole(role)}<UserMinus /> Remove Myself{:else}<UserPlus /> Add Myself{/if}
						</Button>
					{/if}
					<Button variant="outline" disabled={loading} onclick={() => (form = blank())}>{m.dialog_group_roles_cancel()}</Button>
					<Button disabled={!canSave} onclick={saveRole}>{form.mode === 'create' ? 'Create Role' : 'Save'}</Button>
				</div>
			{/if}
		</div>
	</div>
</div>

<ActionDialogs bind:confirming busy={loading} />
