<script lang="ts">
	import { toastError } from '#lib/helpers/feedback.js';
	import { m } from '#lib/paraglide/messages.js';
	import { untrack } from 'svelte';
	import { toast } from 'svelte-sonner';
	import Bookmark from '@lucide/svelte/icons/bookmark';
	import Check from '@lucide/svelte/icons/check';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import History from '@lucide/svelte/icons/history';
	import Trash2 from '@lucide/svelte/icons/trash-2';
	import X from '@lucide/svelte/icons/x';
	import ColorPickerButton from '#lib/components/common/color-picker-button.svelte';
	import GallerySelectDialog from '#lib/components/dialogs/gallery-select-dialog.svelte';
	import { Badge } from '#lib/components/ui/badge/index.js';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as InputGroup from '#lib/components/ui/input-group/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { api } from '#lib/api/client.js';
	import { imageSrc } from '#lib/api/links.js';
	import { editProfile } from '#lib/content/edit-profile.svelte.js';
	import { getFaviconUrl } from '#lib/helpers/format.js';
	import { profileBackgrounds } from '#lib/settings/profile-backgrounds.js';
	import { sidebarData, type Me } from '#lib/people/sidebar-data.svelte.js';
	import { getStatusClass, MAX_PRESETS, statusPresets, type StatusPreset } from '#lib/settings/status-presets.svelte.js';
	import { languageClass, languagesOf } from '#lib/people/user-display.js';
	import { ownPath } from '#lib/api/vrchat-auth.svelte.js';

	// The original's EditProfileDialog: social status with history and presets, icon, banner,
	// pronouns, bio and links, languages, profile theme and background, and the three cosmetics.
	// Saving sends only what changed: the profile fields to PUT profile, the rest to PUT users.
	type Theme = { id: string; name: string; buttonColor?: string | null; iconColor?: string | null; subtextColor?: string | null };
	type SelfProfile = {
		status?: string;
		statusDescription?: string;
		pronouns?: string;
		bio?: string;
		bioLinks?: string[];
		bannerColor?: string;
		bannerUrl?: string;
		bannerType?: string;
		userIcon?: string;
		iconUrl?: string;
		themes?: Theme[];
		themeId?: string;
		themeButtonColor?: string;
		themeIconColor?: string;
		themeSubtextColor?: string;
		backgroundType?: string;
		backgroundTextureId?: string;
		backgroundGradientBottom?: string;
		backgroundGradientTop?: string;
		nameplateEffect?: string;
		profileEffect?: string;
		iconFrame?: string;
	};
	type EditProfileData = {
		status: string;
		statusDescription: string;
		pronouns: string;
		statusHistory: string[];
		iconUrl?: string | null;
		bannerUrl?: string | null;
		bannerType?: string | null;
		currentAvatarThumbnailImageUrl: string;
		isModerator: boolean;
		isVrcPlus: boolean;
		languages: string[];
		profile: SelfProfile;
		languageOptions: Record<string, string>;
		profileDefaults?: Record<string, string> | null;
	};
	type InventoryItem = { id: string; name?: string; imageUrl?: string; itemType?: string; templateId?: string };

	const BANNER_TYPES = [
		{ value: 'color', get label() { return m.dialog_edit_profile_banner_type_color(); } },
		{ value: 'avatarBanner', get label() { return m.dialog_edit_profile_banner_type_avatar_banner(); } },
		{ value: 'customImage', get label() { return m.dialog_edit_profile_banner_type_custom_image(); } }
	];
	const BACKGROUND_TYPES = [
		{ value: 'default', get label() { return m.dialog_edit_profile_profile_background_type_none(); } },
		{ value: 'texture', get label() { return m.dialog_edit_profile_profile_background_type_image(); } },
		{ value: 'gradient', get label() { return m.dialog_edit_profile_profile_background_type_gradient(); } }
	];

	let data = $state<EditProfileData | undefined>();
	let self = $state<SelfProfile>({});
	let inventory = $state<InventoryItem[]>([]);
	let loading = $state(false);

	// The dialog's working copy, the original's editProfileDialog.
	let status = $state('');
	let statusDescription = $state('');
	let pronouns = $state('');
	let bio = $state('');
	let bioLinks = $state<string[]>([]);
	let bannerColor = $state<string | undefined>('');
	let bannerUrl = $state<string | undefined>('');
	let bannerType = $state<string | undefined>('');
	let iconUrl = $state<string | null | undefined>('');
	let themes = $state<Theme[]>([]);
	let themeId = $state<string | undefined>('');
	let themeName = $state('');
	let themeButtonColor = $state<string | undefined>('');
	let themeIconColor = $state<string | undefined>('');
	let themeSubtextColor = $state<string | undefined>('');
	let backgroundType = $state<string | undefined>('default');
	let backgroundTextureId = $state<string | undefined>('');
	let backgroundGradientBottom = $state<string | undefined>('');
	let backgroundGradientTop = $state<string | undefined>('');
	let nameplateEffect = $state<string | undefined>('');
	let profileEffect = $state<string | undefined>('');
	let iconFrame = $state<string | undefined>('');
	let languages = $state<string[]>([]);
	let selectedLanguageToAdd = $state('');

	let pickerOpen = $state({ nameplate: false, profile: false, frame: false });
	let gallery = $state({ open: false, icon: false });
	let themePrompt = $state({ open: false, value: '', error: '' });

	const path = ownPath;
	const avatarThumbnail = $derived(data?.currentAvatarThumbnailImageUrl ?? '');

	const statusOptions = $derived([
		{ value: 'join me', statusClass: 'joinme', get label() { return m.dialog_user_status_join_me(); } },
		{ value: 'active', statusClass: 'online', get label() { return m.dialog_user_status_online(); } },
		{ value: 'ask me', statusClass: 'askme', get label() { return m.dialog_user_status_ask_me(); } },
		{ value: 'busy', statusClass: 'busy', get label() { return m.dialog_user_status_busy(); } },
		...(data?.isModerator ? [{ value: 'offline', statusClass: 'offline', get label() { return m.dialog_user_status_offline(); } }] : [])
	]);
	const selectedStatusOption = $derived(statusOptions.find((o) => o.value === status));
	const historyItems = $derived((data?.statusHistory ?? sidebarData.me?.statusHistory ?? []).map((item, i) => ({ no: i + 1, status: item })));
	const getStatusLabel = (value: string) => statusOptions.find((o) => o.value === value)?.label || value;

	const selectedBannerType = $derived(BANNER_TYPES.some((o) => o.value === bannerType) ? (bannerType as string) : 'color');
	const selectedProfileBackgroundTextureOption = $derived(profileBackgrounds.find((o) => o.id === backgroundTextureId));

	const nameplateEffects = $derived(inventory.filter((item) => item.itemType === 'nameplateEffect'));
	const profileEffects = $derived(inventory.filter((item) => item.itemType === 'profileEffect'));
	const iconFrames = $derived(inventory.filter((item) => item.itemType === 'iconFrame'));

	const languageNames = $derived(new Map(languagesOf(languages.map((key) => `language_${key}`)).map((l) => [l.key, l.value])));
	const currentLanguages = $derived(languages.map((key) => ({ key, value: data?.languageOptions[key] ?? languageNames.get(key) ?? key })));
	const availableLanguages = $derived(Object.entries(data?.languageOptions ?? {}).map(([key, value]) => ({ key, value })));

	function normalizeColor(value: unknown): string {
		const hex = String(value ?? '')
			.trim()
			.replace(/^#/, '')
			.toLowerCase();
		return /^[0-9a-f]{6}$/.test(hex) ? hex : '';
	}

	function getThemeHexColor(value: unknown): string {
		const normalized = normalizeColor(value);
		return normalized ? `#${normalized}` : '#ffffff';
	}

	// The original's showEditProfileDialog: start from the account, then fill in the self profile.
	$effect(() => {
		if (!editProfile.open) return;
		untrack(() => {
			const me = sidebarData.me;
			data = undefined;
			self = {};
			inventory = [];
			status = me?.status ?? '';
			statusDescription = me?.statusDescription ?? '';
			pronouns = '';
			bio = '';
			bioLinks = [];
			themes = [];
			themeId = '';
			themeName = '';
			themeButtonColor = '';
			themeIconColor = '';
			themeSubtextColor = '';
			load();
			api.get<InventoryItem[]>(path('/inventory')).then(
				(items) => (inventory = items),
				(err) => console.error('Error fetching inventory items:', err)
			);
		});
	});

	async function load() {
		try {
			const next = await api.get<EditProfileData>(path('/profile'));
			const ref = next.profile;
			data = next;
			self = ref;
			iconUrl = next.iconUrl;
			languages = next.languages;
			status = ref.status ?? next.status;
			statusDescription = ref.statusDescription ?? next.statusDescription;
			pronouns = ref.pronouns ?? next.pronouns;
			bio = ref.bio ?? '';
			bioLinks = (ref.bioLinks ?? []).slice();
			bannerColor = ref.bannerColor;
			bannerUrl = ref.bannerUrl;
			bannerType = ref.bannerType || 'color';
			themes = ref.themes ?? [];
			themeId = ref.themeId;
			themeName = themes.find((theme) => theme.id === ref.themeId)?.name ?? '';
			themeButtonColor = ref.themeButtonColor;
			themeIconColor = ref.themeIconColor;
			themeSubtextColor = ref.themeSubtextColor;
			backgroundType = ref.backgroundType || 'default';
			backgroundTextureId = ref.backgroundTextureId;
			backgroundGradientBottom = ref.backgroundGradientBottom;
			backgroundGradientTop = ref.backgroundGradientTop;
			nameplateEffect = ref.nameplateEffect;
			profileEffect = ref.profileEffect;
			iconFrame = ref.iconFrame;
		} catch (err) {
			toastError(err);
		}
	}

	function applyPreset(preset: StatusPreset) {
		status = preset.status;
		statusDescription = preset.statusDescription;
	}

	function handleSavePreset() {
		const result = statusPresets.add(status, statusDescription);
		if (result === 'ok') toast.success(m.dialog_social_status_preset_saved());
		else if (result === 'exists') toast.info(m.dialog_social_status_preset_exists());
		else if (result === 'limit') toast.warning(`Maximum ${MAX_PRESETS} presets`);
	}

	function handleBannerColorInput(value: string) {
		const normalized = normalizeColor(value);
		if (!normalized) return;
		bannerColor = normalized;
		bannerType = 'color';
	}

	function handleBannerTypeChange(value: string) {
		if (!BANNER_TYPES.some((o) => o.value === value)) return;
		bannerType = value;
	}

	function handleProfileBackgroundTypeChange(value: string) {
		if (!BACKGROUND_TYPES.some((o) => o.value === value)) return;
		backgroundType = value;
		if (value === 'texture' && !profileBackgrounds.some((o) => o.id === backgroundTextureId)) {
			backgroundTextureId = profileBackgrounds[0]?.id ?? '';
		}
		if (value === 'gradient') {
			const defaults = data?.profileDefaults;
			backgroundGradientTop = defaults?.backgroundGradientTop ?? '000000';
			backgroundGradientBottom = defaults?.backgroundGradientBottom ?? '000000';
		}
	}

	function handleBackgroundTextureChange(value: string) {
		if (!profileBackgrounds.some((o) => o.id === value)) return;
		backgroundTextureId = value;
		backgroundType = 'texture';
	}

	function handleBackgroundGradientTopColorInput(value: string) {
		const normalized = normalizeColor(value);
		if (!normalized) return;
		backgroundGradientTop = normalized;
		backgroundType = 'gradient';
	}

	function handleBackgroundGradientBottomColorInput(value: string) {
		const normalized = normalizeColor(value);
		if (!normalized) return;
		backgroundGradientBottom = normalized;
		backgroundType = 'gradient';
	}

	function handleThemeChange(value: string) {
		themeId = value;
		const selectedTheme = themes.find((o) => o.id === themeId);
		if (!selectedTheme) return;
		themeName = selectedTheme.name;
		themeButtonColor = normalizeColor(selectedTheme.buttonColor);
		themeIconColor = normalizeColor(selectedTheme.iconColor);
		themeSubtextColor = normalizeColor(selectedTheme.subtextColor);
	}

	function handleThemeButtonColorInput(value: string) {
		themeButtonColor = normalizeColor(value);
		handleThemeInput();
	}

	function handleThemeIconColorInput(value: string) {
		themeIconColor = normalizeColor(value);
		handleThemeInput();
	}

	function handleThemeSubtextColorInput(value: string) {
		themeSubtextColor = normalizeColor(value);
		handleThemeInput();
	}

	let saveThemeTimer: ReturnType<typeof setTimeout> | undefined;
	function handleThemeInput() {
		if (!themeId) return;
		const selectedTheme = themes.find((o) => o.id === themeId);
		if (selectedTheme) selectedTheme.name = themeName;
		clearTimeout(saveThemeTimer);
		saveThemeTimer = setTimeout(saveTheme, 300);
	}

	async function createTheme() {
		const defaults = data?.profileDefaults;
		try {
			const newTheme = await api.post<Theme>(path('/profile/themes'), {
				name: themeName,
				buttonColor: defaults?.themeButtonColor ?? '064b5c',
				iconColor: defaults?.themeIconColor ?? '6ae3f9',
				subtextColor: defaults?.themeSubtextColor ?? 'a9a9a9'
			});
			if (newTheme?.id) {
				themes.push(newTheme);
				themeId = newTheme.id;
				themeName = newTheme.name;
				themeButtonColor = newTheme.buttonColor ?? '';
				themeIconColor = newTheme.iconColor ?? '';
				themeSubtextColor = newTheme.subtextColor ?? '';
			}
		} catch (err) {
			console.error('Failed to create profile theme:', err);
		}
	}

	async function saveTheme() {
		const id = themeId;
		if (!id) return;
		const payload = { name: themeName, buttonColor: themeButtonColor ?? '', iconColor: themeIconColor ?? '', subtextColor: themeSubtextColor ?? '' };
		try {
			await api.put(path(`/profile/themes/${encodeURIComponent(id)}`), payload);
			const index = themes.findIndex((o) => o.id === id);
			if (index !== -1) themes[index] = { id, ...payload };
		} catch (err) {
			console.error('Failed to update profile theme:', err);
		}
	}

	async function deleteTheme() {
		const id = themeId;
		if (!id) return;
		try {
			await api.delete(path(`/profile/themes/${encodeURIComponent(id)}`));
			const index = themes.findIndex((o) => o.id === id);
			if (index !== -1) {
				themes.splice(index, 1);
				themeId = '';
				themeName = '';
				themeButtonColor = '';
				themeIconColor = '';
				themeSubtextColor = '';
				if (themes.length) handleThemeChange(themes[themes.length - 1].id);
			}
		} catch (err) {
			console.error('Failed to delete profile theme:', err);
		}
	}

	function handleCreateNewTheme() {
		themePrompt = { open: true, value: '', error: '' };
	}

	function submitThemePrompt(e: SubmitEvent) {
		e.preventDefault();
		if (!/^.{1,12}$/.test(themePrompt.value)) {
			themePrompt.error = 'Theme name must be between 1 and 12 characters.';
			return;
		}
		const value = themePrompt.value;
		themePrompt = { open: false, value: '', error: '' };
		themeId = '';
		themeName = value;
		themeButtonColor = '';
		themeIconColor = '';
		themeSubtextColor = '';
		createTheme();
	}

	function showGallerySelectDialog() {
		gallery = { open: true, icon: false };
	}

	function showIconSelectDialog() {
		gallery = { open: true, icon: true };
	}

	function handleGalleryImageSelect({ imageUrl }: { imageUrl: string }) {
		if (gallery.icon) {
			iconUrl = imageUrl;
			return;
		}
		if (!imageUrl) {
			bannerType = 'color';
			bannerUrl = '';
			return;
		}
		bannerUrl = imageUrl;
	}

	function handleAddUserLanguage(language: string) {
		changeLanguage(language, true);
		selectedLanguageToAdd = '';
	}

	async function changeLanguage(language: string, add: boolean) {
		if (!language) return;
		loading = true;
		try {
			languages = add
				? await api.post<string[]>(path('/profile/languages'), { language })
				: await api.delete<string[]>(path(`/profile/languages/${encodeURIComponent(language)}`));
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}

	function arraysMatch(a: readonly string[] | undefined, b: readonly string[] | undefined): boolean {
		if (!a || !b || a.length !== b.length) return false;
		return a.every((value, i) => value === b[i]);
	}

	async function saveProfile() {
		if (loading || !data) return;
		const current = data;

		const userPayload: { status?: string; statusDescription?: string; pronouns?: string } = {};
		if (status !== current.status) userPayload.status = status;
		if (statusDescription !== current.statusDescription) userPayload.statusDescription = statusDescription;
		if (pronouns !== current.pronouns) userPayload.pronouns = pronouns;

		const profilePayload: Record<string, unknown> = {};
		if (bio !== self.bio) profilePayload.bio = bio;
		if (!arraysMatch(bioLinks, self.bioLinks)) profilePayload.bioLinks = bioLinks;
		if (bannerColor !== self.bannerColor) profilePayload.bannerColor = bannerColor;
		if (bannerUrl !== self.bannerUrl) profilePayload.bannerCustomUrl = bannerUrl;
		if (bannerType !== self.bannerType) {
			profilePayload.bannerType = bannerType;
			if (bannerType === 'avatarBanner') {
				delete profilePayload.bannerCustomUrl;
				delete profilePayload.bannerColor;
			}
			if (bannerType === 'color') delete profilePayload.bannerCustomUrl;
			if (bannerType === 'customImage') delete profilePayload.bannerColor;
		}
		if (iconUrl !== self.iconUrl) profilePayload.userIcon = iconUrl;
		if (themeId !== self.themeId) profilePayload.themeId = themeId;
		if (backgroundType !== self.backgroundType) profilePayload.backgroundType = backgroundType;
		if (backgroundTextureId !== self.backgroundTextureId) profilePayload.backgroundTextureId = backgroundTextureId;
		if (backgroundGradientBottom !== self.backgroundGradientBottom) profilePayload.backgroundGradientBottom = backgroundGradientBottom;
		if (backgroundGradientTop !== self.backgroundGradientTop) profilePayload.backgroundGradientTop = backgroundGradientTop;
		if (nameplateEffect !== self.nameplateEffect) profilePayload.nameplateEffect = nameplateEffect;
		if (profileEffect !== self.profileEffect) profilePayload.profileEffect = profileEffect;
		if (iconFrame !== self.iconFrame) profilePayload.iconFrame = iconFrame;
		// JSON leaves out what is undefined, as the original's request did.
		for (const key of Object.keys(profilePayload)) if (profilePayload[key] === undefined) delete profilePayload[key];

		loading = true;
		try {
			if (Object.keys(profilePayload).length) await api.put(path('/profile'), profilePayload);
			if (Object.keys(userPayload).length) sidebarData.me = await api.put<Me>(path('/me'), userPayload);
			editProfile.open = false;
			editProfile.notifySaved();
			toast.success('Profile updated');
		} catch (err) {
			toastError(err);
		} finally {
			loading = false;
		}
	}
</script>

{#snippet colorField(label: string, value: string, onchange: (color: string) => void)}
	<div class="space-y-1">
		<span class="text-xs text-muted-foreground">{label}</span>
		<div class="flex items-center gap-2">
			<ColorPickerButton {value} disabled={loading} {onchange} />
		</div>
	</div>
{/snippet}

{#snippet effectPicker(title: string, items: InventoryItem[], selectedId: string | undefined, key: 'nameplate' | 'profile' | 'frame', set: (id: string) => void)}
	{@const selected = items.find((item) => item.templateId === selectedId)}
	<section class="space-y-3">
		<div class="flex items-center justify-between gap-2">
			<h3 class="text-sm font-semibold">{title}</h3>
			<Button size="sm" variant="outline" disabled={loading || !selectedId} onclick={() => set('')}>
				<X class="size-4" />
				{m.dialog_gallery_icons_clear()}
			</Button>
		</div>
		<Popover.Root bind:open={pickerOpen[key]}>
			<Popover.Trigger disabled={loading}>
				{#snippet child({ props })}
					<Button {...props} variant="outline" class="h-16 w-full justify-between px-3" disabled={loading}>
						{#if selected}
							<span class="flex min-w-0 items-center gap-3">
								<img src={imageSrc(selected.imageUrl)} alt={selected.name} class="size-12 shrink-0 rounded-sm object-cover" loading="lazy" />
								<span class="truncate">{selected.name}</span>
							</span>
						{:else}
							<span class="text-muted-foreground">{m.dialog_edit_profile_select_effect()}</span>
						{/if}
						<ChevronDown class="size-4 shrink-0" />
					</Button>
				{/snippet}
			</Popover.Trigger>
			<Popover.Content class="w-130 max-w-[calc(100vw-2rem)] p-3" align="start">
				{#if items.length}
					<div class="grid max-h-105 grid-cols-2 gap-3 overflow-y-auto">
						{#each items as item (item.id)}
							{@const isSelected = selectedId === item.templateId}
							<button
								type="button"
								class={['relative min-w-0 overflow-hidden rounded-md border bg-background text-left transition-colors hover:bg-accent', isSelected && 'border-primary ring-2 ring-primary/40 ring-inset']}
								aria-label={item.name}
								aria-pressed={isSelected}
								onclick={() => {
									set(item.templateId ?? '');
									pickerOpen[key] = false;
								}}>
								<img src={imageSrc(item.imageUrl)} alt={item.name} class="aspect-square w-full object-cover" loading="lazy" />
								<span class="block truncate px-3 py-2 text-sm font-medium">{item.name}</span>
								{#if isSelected}
									<span class="absolute top-2 right-2 flex size-6 items-center justify-center rounded-full bg-primary text-primary-foreground">
										<Check class="size-4" />
									</span>
								{/if}
							</button>
						{/each}
					</div>
				{:else}
					<p class="text-sm text-muted-foreground">{m.common_no_data()}</p>
				{/if}
			</Popover.Content>
		</Popover.Root>
	</section>
{/snippet}

<Dialog.Root bind:open={editProfile.open}>
	<Dialog.Content class="flex max-h-[85vh] flex-col gap-0 overflow-hidden bg-background p-0 sm:max-w-150">
		<Dialog.Header class="px-6 pt-6 pb-4">
			<Dialog.Title>{m.dialog_user_actions_edit_profile()}</Dialog.Title>
		</Dialog.Header>

		<div class="min-h-0 flex-1 space-y-4 overflow-x-hidden overflow-y-auto px-6 py-2 [&>section]:rounded-lg [&>section]:border [&>section]:border-border [&>section]:bg-muted/20 [&>section]:p-4">
			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_social_status_header()}</h3>

				<div class="flex min-w-0 items-center gap-2">
					<Select.Root type="single" value={status} disabled={loading} onValueChange={(value) => (status = String(value))}>
						<Select.Trigger size="sm" class="w-42 shrink-0">
							{#if selectedStatusOption}
								<span class="inline-flex items-center gap-2">
									<i class={['x-user-status', selectedStatusOption.statusClass]}></i>
									<span>{selectedStatusOption.label}</span>
								</span>
							{/if}
						</Select.Trigger>
						<Select.Content>
							{#each statusOptions as option (option.value)}
								<Select.Item value={option.value} label={option.label}>
									<div class="flex items-center gap-2">
										<i class={['x-user-status', option.statusClass]}></i>
										<span>{option.label}</span>
									</div>
								</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>

					<InputGroup.Root class="min-w-0 flex-1 flex-nowrap">
						<InputGroup.Input bind:value={statusDescription} placeholder={m.dialog_social_status_status_placeholder()} maxlength={32} />
						{#if statusDescription.length > 0}
							<InputGroup.Addon align="inline-end">
								<InputGroup.Button size="icon-xs" aria-label={m.dialog_gallery_icons_clear()} onclick={() => (statusDescription = '')}>
									<X class="size-3.5" />
									<span class="sr-only">{m.dialog_gallery_icons_clear()}</span>
								</InputGroup.Button>
							</InputGroup.Addon>
						{/if}
					</InputGroup.Root>
					<DropdownMenu.Root>
						<DropdownMenu.Trigger>
							{#snippet child({ props })}
								<Button {...props} variant="outline" size="icon-lg" aria-label={m.dialog_social_status_history()}>
									<History class="text-lg" />
								</Button>
							{/snippet}
						</DropdownMenu.Trigger>
						<DropdownMenu.Content align="start">
							{#if !historyItems.length}
								<DropdownMenu.Item disabled>{m.dialog_social_status_history()}</DropdownMenu.Item>
							{/if}
							{#each historyItems as item (item.no)}
								<DropdownMenu.Item onSelect={() => (statusDescription = item.status)}>{item.status}</DropdownMenu.Item>
							{/each}
						</DropdownMenu.Content>
					</DropdownMenu.Root>
				</div>

				{#if statusPresets.list.length}
					<div class="space-y-2">
						<span class="block text-xs text-muted-foreground">{m.dialog_social_status_presets()}</span>
						<div class="flex flex-wrap gap-1.5">
							{#each statusPresets.list as preset, index (index)}
								<div
									class="group inline-flex max-w-50 cursor-pointer items-center gap-1.5 rounded-full border bg-background py-1 pr-1.5 pl-2.5 text-xs transition-colors hover:bg-accent"
									role="button"
									tabindex="0"
									onclick={() => applyPreset(preset)}
									onkeydown={(e) => e.key === 'Enter' && applyPreset(preset)}>
									<i class={['x-user-status flex-none', getStatusClass(preset.status)]}></i>
									<span class="truncate">{preset.statusDescription || getStatusLabel(preset.status)}</span>
									<button
										type="button"
										class="inline-flex size-4 flex-none cursor-pointer items-center justify-center rounded-full opacity-0 transition-opacity group-hover:opacity-100 hover:bg-muted"
										aria-label={m.dialog_world_actions_delete()}
										onclick={(e) => {
											e.stopPropagation();
											statusPresets.remove(index);
										}}>
										<X class="size-3" />
									</button>
								</div>
							{/each}
						</div>
					</div>
				{/if}

				<Button variant="outline" disabled={loading} onclick={handleSavePreset}>
					<Bookmark class="size-4" />
					{m.dialog_social_status_save_preset()}
				</Button>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_edit_profile_icon()}</h3>

				<div class="flex items-center gap-2">
					<img src={imageSrc(iconUrl || avatarThumbnail)} class="inline-block aspect-square h-16 rounded-md object-cover" alt="Icon" loading="lazy" />
					{#if iconUrl !== avatarThumbnail}
						<Button size="sm" variant="outline" disabled={loading} onclick={() => (iconUrl = '')} aria-label={m.dialog_world_actions_delete()}>
							<X class="size-4" />
						</Button>
					{/if}
					<Button size="sm" variant="outline" onclick={showIconSelectDialog}>{m.dialog_gallery_select_header()}</Button>
				</div>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_edit_profile_banner()}</h3>

				<Select.Root type="single" value={selectedBannerType} disabled={loading} onValueChange={handleBannerTypeChange}>
					<Select.Trigger size="sm" class="w-42">{BANNER_TYPES.find((o) => o.value === selectedBannerType)?.label}</Select.Trigger>
					<Select.Content>
						{#each BANNER_TYPES as option (option.value)}
							<Select.Item value={option.value} label={option.label} />
						{/each}
					</Select.Content>
				</Select.Root>

				{#if selectedBannerType === 'color'}
					<div class="flex items-center gap-2">
						<ColorPickerButton value={getThemeHexColor(bannerColor)} disabled={loading} onchange={handleBannerColorInput} />
					</div>
				{:else if selectedBannerType === 'avatarBanner'}
					<div>
						<!-- svelte-ignore a11y_img_redundant_alt -->
						<img src={imageSrc(avatarThumbnail)} class="inline-block aspect-17/6 h-16 rounded-md object-cover" alt="Avatar Image" />
					</div>
				{:else if selectedBannerType === 'customImage'}
					<div>
						<div class="mt-2 flex items-center gap-2">
							{#if bannerUrl}
								<!-- svelte-ignore a11y_img_redundant_alt -->
								<img src={imageSrc(bannerUrl)} class="inline-block aspect-17/6 h-16 rounded-md object-cover" alt="Custom Image" />
							{/if}
							<Button size="sm" variant="outline" onclick={showGallerySelectDialog}>{m.dialog_gallery_select_header()}</Button>
						</div>
					</div>
				{/if}
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_user_pronouns()}</h3>
				<InputGroup.Root class="flex-nowrap">
					<InputGroup.Textarea bind:value={pronouns} maxlength={32} rows={1} class="min-h-0 py-2" placeholder={m.dialog_pronouns_pronouns_placeholder()} />
					<InputGroup.Addon align="block-end">
						<InputGroup.Text class="gap-1 text-xs tabular-nums">
							<span>{pronouns.length}</span>
							<span class="text-muted-foreground/70">/ 32</span>
						</InputGroup.Text>
					</InputGroup.Addon>
				</InputGroup.Root>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_bio_header()}</h3>

				<InputGroup.Root class="flex-nowrap">
					<InputGroup.Textarea bind:value={bio} maxlength={512} rows={5} placeholder={m.dialog_bio_bio_placeholder()} />
					<InputGroup.Addon align="block-end">
						<InputGroup.Text class="gap-1 text-xs tabular-nums">
							<span>{bio.length}</span>
							<span class="text-muted-foreground/70">/ 512</span>
						</InputGroup.Text>
					</InputGroup.Addon>
				</InputGroup.Root>

				{#each bioLinks as link, index (index)}
					<InputGroup.Root class="h-8 flex-nowrap">
						<InputGroup.Addon align="inline-start">
							{#if link}
								<img src={getFaviconUrl(link)} alt="" style="width: 16px; height: 16px; vertical-align: middle" />
							{:else}
								<div style="width: 16px; height: 16px"></div>
							{/if}
						</InputGroup.Addon>
						<InputGroup.Input bind:value={bioLinks[index]} maxlength={1000} />
						<InputGroup.Addon align="inline-end">
							<Button variant="ghost" size="icon-sm" onclick={() => bioLinks.splice(index, 1)} aria-label={m.dialog_world_actions_delete()}>
								<Trash2 class="size-4" />
							</Button>
						</InputGroup.Addon>
					</InputGroup.Root>
				{/each}

				<Button variant="outline" disabled={bioLinks.length >= 3 || loading} size="sm" onclick={() => bioLinks.push('')}>{m.dialog_bio_add_link()}</Button>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_language_header()}</h3>

				{#each currentLanguages as item (item.key)}
					<div class="my-2">
						<Badge class="mr-1.5" variant="outline">
							<span class={['flags mr-1.5', languageClass(item.key)]} style="display: inline-block"></span>
							{item.value} ({item.key.toUpperCase()})
							<button
								class="ml-2 p-0"
								type="button"
								style="border: none; background: transparent; display: inline-flex; align-items: center; color: inherit; cursor: pointer"
								aria-label={m.dialog_world_actions_delete()}
								onclick={() => changeLanguage(item.key, false)}>
								<X class="h-3 w-3" />
							</button>
						</Badge>
					</div>
				{/each}

				<Select.Root type="single" bind:value={selectedLanguageToAdd} disabled={loading || currentLanguages.length === 3} onValueChange={handleAddUserLanguage}>
					<Select.Trigger size="sm"><span class="text-muted-foreground">{m.dialog_language_select_language()}</span></Select.Trigger>
					<Select.Content>
						{#each availableLanguages as item (item.key)}
							<Select.Item value={item.key} label={item.value}>
								<span class={['flags mr-1.5', languageClass(item.key)]} style="display: inline-block"></span>
								{item.value} ({item.key.toUpperCase()})
							</Select.Item>
						{/each}
					</Select.Content>
				</Select.Root>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_edit_profile_profile_theme()}</h3>
				<div class="flex items-center gap-2">
					<Select.Root type="single" value={themeId ?? ''} disabled={loading} onValueChange={handleThemeChange}>
						<Select.Trigger size="sm" class="w-42">
							{#if themeName}{themeName}{:else}<span class="text-muted-foreground">{m.dialog_edit_profile_theme_name_placeholder()}</span>{/if}
						</Select.Trigger>
						<Select.Content>
							{#each themes as option (option.id)}
								<Select.Item value={option.id} label={option.name}>
									<div class="flex w-full items-center justify-between gap-3">
										<span class="inline-flex shrink-0 items-center gap-1">
											<span class="size-3 rounded-sm border" style:background-color={getThemeHexColor(option.buttonColor)} title="Button color"></span>
											<span class="size-3 rounded-sm border" style:background-color={getThemeHexColor(option.iconColor)} title="Icon color"></span>
											<span class="size-3 rounded-sm border" style:background-color={getThemeHexColor(option.subtextColor)} title="Subtext color"></span>
										</span>
										<span class="truncate">{option.name}</span>
									</div>
								</Select.Item>
							{/each}
						</Select.Content>
					</Select.Root>

					<Button size="sm" variant="outline" disabled={loading} onclick={handleCreateNewTheme}>{m.dialog_edit_profile_create_theme()}</Button>
					<Button size="sm" variant="outline" disabled={loading || !themeId} onclick={deleteTheme}>{m.dialog_edit_profile_delete_theme()}</Button>
				</div>

				<InputGroup.Root class="mb-1 min-w-0 flex-1 flex-nowrap">
					<InputGroup.Input bind:value={themeName} oninput={handleThemeInput} maxlength={12} placeholder={m.dialog_edit_profile_theme_name_placeholder()} />
				</InputGroup.Root>
				<div class="flex gap-2">
					{@render colorField('Button', getThemeHexColor(themeButtonColor), handleThemeButtonColorInput)}
					{@render colorField('Icon', getThemeHexColor(themeIconColor), handleThemeIconColorInput)}
					{@render colorField('Subtext', getThemeHexColor(themeSubtextColor), handleThemeSubtextColorInput)}
				</div>
			</section>

			<section class="space-y-3">
				<h3 class="text-sm font-semibold">{m.dialog_edit_profile_profile_background()}</h3>
				<div class="flex items-center gap-2">
					<Select.Root type="single" value={backgroundType ?? ''} disabled={loading} onValueChange={handleProfileBackgroundTypeChange}>
						<Select.Trigger size="sm" class="w-42">{BACKGROUND_TYPES.find((o) => o.value === backgroundType)?.label ?? ''}</Select.Trigger>
						<Select.Content>
							{#each BACKGROUND_TYPES as option (option.value)}
								<Select.Item value={option.value} label={option.label} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>

				{#if backgroundType === 'gradient'}
					<div class="flex gap-2">
						{@render colorField('Gradient Top', getThemeHexColor(backgroundGradientTop), handleBackgroundGradientTopColorInput)}
						{@render colorField('Gradient Bottom', getThemeHexColor(backgroundGradientBottom), handleBackgroundGradientBottomColorInput)}
					</div>
				{/if}

				{#if backgroundType === 'texture'}
					<div class="space-y-2">
						<Select.Root type="single" value={backgroundTextureId ?? ''} disabled={loading} onValueChange={handleBackgroundTextureChange}>
							<Select.Trigger size="sm" class="h-14! w-80">
								{#if selectedProfileBackgroundTextureOption}
									<span class="inline-flex min-w-0 items-center gap-2">
										<img src={imageSrc(selectedProfileBackgroundTextureOption.thumbnail)} class="h-9 w-16 shrink-0 rounded-sm object-cover" alt={selectedProfileBackgroundTextureOption.label} loading="lazy" />
										<span class="truncate">{selectedProfileBackgroundTextureOption.label}</span>
									</span>
								{:else}
									<span class="text-muted-foreground">{m.dialog_edit_profile_profile_background_type_image()}</span>
								{/if}
							</Select.Trigger>
							<Select.Content class="w-80">
								{#each profileBackgrounds as option (option.id)}
									<Select.Item value={option.id} label={option.label} disabled={!data?.isVrcPlus && option.isVRCPlus}>
										<div class="flex items-center gap-2">
											<img src={imageSrc(option.thumbnail)} class="h-9 w-16 shrink-0 rounded-sm object-cover" alt={option.label} loading="lazy" />
											<span>{option.label}</span>
										</div>
									</Select.Item>
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
				{/if}
			</section>

			{@render effectPicker('Nameplate Effect', nameplateEffects, nameplateEffect, 'nameplate', (id) => (nameplateEffect = id))}
			{@render effectPicker('Profile Effect', profileEffects, profileEffect, 'profile', (id) => (profileEffect = id))}
			{@render effectPicker('Icon Frame', iconFrames, iconFrame, 'frame', (id) => (iconFrame = id))}
		</div>

		<Dialog.Footer class="px-6 py-4">
			<Button variant="outline" disabled={loading} onclick={() => (editProfile.open = false)}>{m.dialog_alertdialog_cancel()}</Button>
			<Button disabled={loading || !data} onclick={saveProfile}>{m.dialog_bio_update()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<GallerySelectDialog bind:open={gallery.open} icon={gallery.icon} onselect={handleGalleryImageSelect} />

<Dialog.Root bind:open={themePrompt.open}>
	<Dialog.Content showCloseButton={false} class="bg-background">
		<form onsubmit={submitThemePrompt}>
			<Dialog.Header class="mb-5">
				<Dialog.Title>{m.dialog_edit_profile_profile_theme()}</Dialog.Title>
				<Dialog.Description>Enter a theme name (1-12 characters).</Dialog.Description>
			</Dialog.Header>
			<Input bind:value={themePrompt.value} aria-invalid={!!themePrompt.error} />
			{#if themePrompt.error}<p class="mt-2 text-sm text-destructive">{themePrompt.error}</p>{/if}
			<Dialog.Footer class="mt-5">
				<Button type="button" variant="outline" onclick={() => (themePrompt.open = false)}>{m.dialog_alertdialog_cancel()}</Button>
				<Button type="submit">{m.dialog_group_gallery_create()}</Button>
			</Dialog.Footer>
		</form>
	</Dialog.Content>
</Dialog.Root>
