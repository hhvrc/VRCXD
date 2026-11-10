<script lang="ts">
	import { m } from '#lib/paraglide/messages.js';
	import ChevronDown from '@lucide/svelte/icons/chevron-down';
	import { toast } from 'svelte-sonner';
	import ColorPickerButton from '#lib/components/common/color-picker-button.svelte';
	import NumberField from '#lib/components/common/number-field.svelte';
	import SettingsGroup from '#lib/components/settings/settings-group.svelte';
	import SettingsItem from '#lib/components/settings/settings-item.svelte';
	import TableLimitsDialog from '#lib/components/settings/table-limits-dialog.svelte';
	import TablePageSizesDialog from '#lib/components/settings/table-page-sizes-dialog.svelte';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import * as Select from '#lib/components/ui/select/index.js';
	import { Switch } from '#lib/components/ui/switch/index.js';
	import { Textarea } from '#lib/components/ui/textarea/index.js';
	import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
	import { appearance, type TrustColorKey } from '#lib/settings/appearance.svelte.js';
	import { dashboards } from '#lib/nav/dashboards.svelte.js';
	import { APP_CJK_FONT_PACK_CONFIG, APP_CJK_FONT_PACKS, APP_FONT_CONFIG, APP_FONT_DEFAULT_KEY, APP_FONT_FAMILIES } from '#lib/settings/fonts.js';
	import { tableSettings } from '#lib/settings/table-settings.svelte.js';
	import { isLanguageCode, languageCodes, languageName } from '#lib/settings/languages.js';
	import { setLocale } from '#lib/paraglide/runtime.js';

	// The original's Settings > Interface tab (InterfaceTab.vue), in its order and wording.

	const trustColorEntries: { key: TrustColorKey; tagClass: string; label: string; presets: string[] }[] = [
		{ key: 'untrusted', tagClass: 'x-tag-untrusted', get label() { return m.view_settings_appearance_user_colors_trust_levels_visitor(); }, presets: ['#CCCCCC'] },
		{ key: 'basic', tagClass: 'x-tag-basic', get label() { return m.view_settings_appearance_user_colors_trust_levels_new_user(); }, presets: ['#1778ff'] },
		{ key: 'known', tagClass: 'x-tag-known', get label() { return m.view_settings_appearance_user_colors_trust_levels_user(); }, presets: ['#2bcf5c'] },
		{ key: 'trusted', tagClass: 'x-tag-trusted', get label() { return m.view_settings_appearance_user_colors_trust_levels_known_user(); }, presets: ['#ff7b42'] },
		{ key: 'veteran', tagClass: 'x-tag-veteran', get label() { return m.view_settings_appearance_user_colors_trust_levels_trusted_user(); }, presets: ['#b18fff', '#8143e6', '#ff69b4', '#b52626', '#ffd000', '#abcdef'] },
		{ key: 'vip', tagClass: 'x-tag-vip', get label() { return m.view_settings_appearance_user_colors_trust_levels_vrchat_team(); }, presets: ['#ff2626'] },
		{ key: 'troll', tagClass: 'x-tag-troll', get label() { return m.view_settings_appearance_user_colors_trust_levels_nuisance(); }, presets: ['#782f2f'] }
	];

	const westernFontItems = APP_FONT_FAMILIES.filter((key) => key !== 'custom' && key !== 'system_ui');

	const fontDropdownDisplayText = $derived.by(() => {
		if (appearance.fontFamily.value === 'custom') return 'Custom';
		const western = APP_FONT_CONFIG[appearance.fontFamily.value]?.label ?? '';
		const cjk = APP_CJK_FONT_PACK_CONFIG[appearance.cjkFontPack.value]?.label ?? '';
		return `${western} / ${cjk}`;
	});

	function selectCjkFont(key: string) {
		if (appearance.fontFamily.value === 'custom') appearance.fontFamily.value = APP_FONT_DEFAULT_KEY;
		appearance.cjkFontPack.value = key;
	}

	const FONT_FAMILY_REGEX = /^\s*(([-_\p{L}][\p{L}\p{N}_\s-]*)|'[^']+'|"[^"]+")\s*(,\s*(([-_\p{L}][\p{L}\p{N}_\s-]*)|'[^']+'|"[^"]+")\s*)*$/u;

	let customFontDialogOpen = $state(false);
	let customFontInput = $state('');

	function selectCustomFont() {
		const cssVarValue = getComputedStyle(document.documentElement).getPropertyValue('--font-western-primary').trim();
		const fallbackFont = APP_FONT_CONFIG[appearance.fontFamily.value]?.cssName || APP_FONT_CONFIG[APP_FONT_DEFAULT_KEY].cssName;
		customFontInput = appearance.customFontFamily.value.trim() || cssVarValue || fallbackFont;
		customFontDialogOpen = true;
	}

	function saveCustomFont() {
		const trimmed = customFontInput.trim();
		if (!trimmed || !FONT_FAMILY_REGEX.test(trimmed)) {
			toast.error("Invalid font-family. Use comma-separated font names, e.g. 'My Font', Arial, sans-serif");
			return;
		}
		appearance.customFontFamily.value = trimmed;
		appearance.fontFamily.value = 'custom';
		customFontDialogOpen = false;
	}

	// The original's custom.css, edited here since a browser can't read a file from AppData.
	let customCssDialogOpen = $state(false);
	let customCssInput = $state('');
	function editCustomCss() {
		customCssInput = appearance.customCss.value;
		customCssDialogOpen = true;
	}
	const customCssLines = (css: string) => `${css.split('\n').length} line(s)`;
	function saveCustomCss() {
		appearance.customCss.value = customCssInput;
		customCssDialogOpen = false;
		toast.success(customCssInput.trim() ? 'Custom CSS applied' : 'Custom CSS removed');
	}

	let tablePageSizesDialogOpen = $state(false);
	let tableLimitsDialogOpen = $state(false);

	const weekDays: { value: string; label: string }[] = [
		{ value: '1', get label() { return m.common_days_monday(); } },
		{ value: '0', get label() { return m.common_days_sunday(); } },
		{ value: '6', get label() { return m.common_days_saturday(); } }
	];
</script>

<div class="flex flex-col gap-10 py-2">
	<SettingsGroup title={m.view_settings_category_appearance()}>
		<SettingsItem label={m.dialog_language_header()}>
			<Select.Root type="single" value={appearance.appLanguage.value} onValueChange={(v) => isLanguageCode(v) && setLocale(v, { reload: false })}>
				<Select.Trigger size="sm">{languageName(appearance.appLanguage.value)}</Select.Trigger>
				<Select.Content>
					{#each languageCodes as code (code)}
						<Select.Item value={code} label={languageName(code)} />
					{/each}
				</Select.Content>
			</Select.Root>
		</SettingsItem>

		<SettingsItem label={m.view_settings_appearance_appearance_font_family()}>
			<DropdownMenu.Root>
				<DropdownMenu.Trigger>
					{#snippet child({ props })}
						<Button {...props} variant="outline" size="sm" class="min-w-[180px] justify-between font-normal">
							<span class="truncate">{fontDropdownDisplayText}</span>
							<ChevronDown class="ml-2 size-4 shrink-0 opacity-50" />
						</Button>
					{/snippet}
				</DropdownMenu.Trigger>
				<DropdownMenu.Content align="end">
					{#each westernFontItems as key (key)}
						<DropdownMenu.CheckboxItem checked={appearance.fontFamily.value === key} onSelect={() => (appearance.fontFamily.value = key)}>
							{APP_FONT_CONFIG[key].label}
						</DropdownMenu.CheckboxItem>
					{/each}
					<DropdownMenu.Separator />
					{#each APP_CJK_FONT_PACKS as key (key)}
						<DropdownMenu.CheckboxItem
							checked={appearance.cjkFontPack.value === key && appearance.fontFamily.value !== 'custom'}
							onSelect={() => selectCjkFont(key)}
						>
							{APP_CJK_FONT_PACK_CONFIG[key].label}
						</DropdownMenu.CheckboxItem>
					{/each}
					<DropdownMenu.Separator />
					<DropdownMenu.CheckboxItem checked={appearance.fontFamily.value === 'custom'} onSelect={selectCustomFont}>{m.view_settings_appearance_appearance_font_family_custom()}</DropdownMenu.CheckboxItem>
				</DropdownMenu.Content>
			</DropdownMenu.Root>
		</SettingsItem>

		<SettingsItem label={m.view_settings_appearance_appearance_zoom()}>
			<NumberField class="w-32" value={appearance.zoomLevel.value} min={30} max={500} step={1} ariaLabel="Zoom" onchange={(v) => (appearance.zoomLevel.value = v)} />
		</SettingsItem>

		<SettingsItem label={m.view_settings_appearance_appearance_show_notification_icon_dot()} toggle>
			<Switch checked={appearance.notificationIconDot.value} aria-label={m.view_settings_appearance_appearance_show_notification_icon_dot()} onCheckedChange={(v) => (appearance.notificationIconDot.value = v)} />
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_appearance_user_dialog_header()}>
		<SettingsItem label={m.view_settings_appearance_appearance_vrc_profile_themes()} toggle>
			<Switch checked={appearance.displayVRCProfileThemes.value} aria-label={m.view_settings_appearance_appearance_vrc_profile_themes()} onCheckedChange={(v) => (appearance.displayVRCProfileThemes.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_vrc_profile_backgrounds()} description={m.view_settings_appearance_appearance_vrc_profile_backgrounds_description()} toggle>
			<Switch checked={appearance.displayVRCProfileBackgrounds.value} aria-label={m.view_settings_appearance_appearance_vrc_profile_backgrounds()} onCheckedChange={(v) => (appearance.displayVRCProfileBackgrounds.value = v)} />
		</SettingsItem>
		{#if appearance.displayVRCProfileBackgrounds.value}
			<SettingsItem label={m.view_settings_appearance_appearance_vrc_profile_backgrounds_opacity()} description={m.view_settings_appearance_appearance_vrc_profile_backgrounds_opacity_description()}>
				<NumberField
					class="w-32"
					value={appearance.profileBackgroundOpacity.value}
					min={0}
					max={1}
					step={0.1}
					decimals={2}
					ariaLabel="Profile Background Opacity"
					onchange={(v) => (appearance.profileBackgroundOpacity.value = v)}
				/>
			</SettingsItem>
		{/if}
		<SettingsItem label={m.view_settings_appearance_appearance_vrc_profile_cosmetics()} description={m.view_settings_appearance_appearance_cosmetics_description()} toggle>
			<Switch checked={appearance.displayVRCProfileCosmetics.value} aria-label={m.view_settings_appearance_appearance_vrc_profile_cosmetics()} onCheckedChange={(v) => (appearance.displayVRCProfileCosmetics.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_user_dialog_vrchat_notes()} description={m.view_settings_appearance_user_dialog_vrchat_notes_description()} toggle>
			<Switch checked={!appearance.hideUserNotes.value} aria-label={m.view_settings_appearance_user_dialog_vrchat_notes()} onCheckedChange={(v) => (appearance.hideUserNotes.value = !v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_user_dialog_vrcx_memos()} description={m.view_settings_appearance_user_dialog_vrcx_memos_description()} toggle>
			<Switch checked={!appearance.hideUserMemos.value} aria-label={m.view_settings_appearance_user_dialog_vrcx_memos()} onCheckedChange={(v) => (appearance.hideUserMemos.value = !v)} />
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_appearance_display_header()}>
		<SettingsItem label={m.view_settings_appearance_appearance_show_instance_id()} toggle>
			<Switch checked={appearance.showInstanceIdInLocation.value} aria-label={m.view_settings_appearance_appearance_show_instance_id()} onCheckedChange={(v) => (appearance.showInstanceIdInLocation.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_nicknames()} description={m.view_settings_appearance_appearance_nicknames_description()} toggle>
			<Switch checked={!appearance.hideNicknames.value} aria-label={m.view_settings_appearance_appearance_nicknames()} onCheckedChange={(v) => (appearance.hideNicknames.value = !v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_age_gated_instances()} description={m.view_settings_appearance_appearance_age_gated_instances_description()} toggle>
			<Switch checked={appearance.isAgeGatedInstancesVisible.value} aria-label={m.view_settings_appearance_appearance_age_gated_instances()} onCheckedChange={(v) => (appearance.isAgeGatedInstancesVisible.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_closed_instances()} description={m.view_settings_appearance_appearance_closed_instances_description()} toggle>
			<Switch checked={appearance.isClosedInstancesVisible.value} aria-label={m.view_settings_appearance_appearance_closed_instances()} onCheckedChange={(v) => (appearance.isClosedInstancesVisible.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_striped_data_table_mode()} toggle>
			<Switch checked={tableSettings.striped} aria-label={m.view_settings_appearance_appearance_striped_data_table_mode()} onCheckedChange={(v) => (tableSettings.striped = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_accessible_status_indicators()} description={m.view_settings_appearance_appearance_accessible_status_indicators_description()} toggle>
			<Switch checked={appearance.accessibleStatusIndicators.value} aria-label={m.view_settings_appearance_appearance_accessible_status_indicators()} onCheckedChange={(v) => (appearance.accessibleStatusIndicators.value = v)} />
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_interface_navigation_header()}>
		<SettingsItem label={m.view_settings_interface_navigation_show_new_dashboard_button()} toggle>
			<Switch checked={dashboards.showNewButton} aria-label={m.view_settings_interface_navigation_show_new_dashboard_button()} onCheckedChange={(v) => dashboards.setShowNewButton(v)} />
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_interface_lists_tables_header()}>
		<SettingsItem label={m.view_settings_appearance_appearance_sort_favorite_by()}>
			<ToggleGroup.Root
				type="single"
				variant="outline"
				size="sm"
				value={appearance.sortFavorites.value ? 'true' : 'false'}
				onValueChange={(v) => v && (appearance.sortFavorites.value = v === 'true')}
			>
				<ToggleGroup.Item value="false">{m.dialog_group_edit_name()}</ToggleGroup.Item>
				<ToggleGroup.Item value="true">{m.dialog_registry_backup_date()}</ToggleGroup.Item>
			</ToggleGroup.Root>
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_sort_instance_users_by()}>
			<ToggleGroup.Root
				type="single"
				variant="outline"
				size="sm"
				value={appearance.instanceUsersSortAlphabetical.value ? 'true' : 'false'}
				onValueChange={(v) => v && (appearance.instanceUsersSortAlphabetical.value = v === 'true')}
			>
				<ToggleGroup.Item value="false">{m.view_settings_appearance_side_panel_sorting_time_in_instance()}</ToggleGroup.Item>
				<ToggleGroup.Item value="true">{m.dialog_group_edit_name()}</ToggleGroup.Item>
			</ToggleGroup.Root>
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_table_page_sizes()}>
			<Button size="sm" variant="outline" aria-label={m.view_settings_appearance_appearance_table_page_sizes()} onclick={() => (tablePageSizesDialogOpen = true)}>{m.common_actions_configure()}</Button>
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_appearance_table_entries_settings()} description={m.view_settings_appearance_appearance_table_entries_settings_description()}>
			<Button size="sm" variant="outline" onclick={() => (tableLimitsDialogOpen = true)}>{m.common_actions_configure()}</Button>
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_appearance_timedate_header()}>
		<SettingsItem label={m.view_settings_appearance_timedate_time_format()}>
			<ToggleGroup.Root type="single" variant="outline" size="sm" value={appearance.dtHour12.value ? 'true' : 'false'} onValueChange={(v) => v && (appearance.dtHour12.value = v === 'true')}>
				<ToggleGroup.Item value="true">{m.view_settings_appearance_timedate_time_format_12()}</ToggleGroup.Item>
				<ToggleGroup.Item value="false">{m.view_settings_appearance_timedate_time_format_24()}</ToggleGroup.Item>
			</ToggleGroup.Root>
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_timedate_force_iso_date_format()} toggle>
			<Switch checked={appearance.dtIsoFormat.value} aria-label={m.view_settings_appearance_timedate_force_iso_date_format()} onCheckedChange={(v) => (appearance.dtIsoFormat.value = v)} />
		</SettingsItem>
		<SettingsItem label={m.view_settings_appearance_timedate_week_starts_on()} description={m.view_settings_appearance_timedate_week_starts_on_description()}>
			<Select.Root type="single" value={String(appearance.weekStartsOn.value)} onValueChange={(v) => (appearance.weekStartsOn.value = Number(v) as 0 | 1 | 6)}>
				<Select.Trigger size="sm">{weekDays.find((d) => d.value === String(appearance.weekStartsOn.value))?.label}</Select.Trigger>
				<Select.Content>
					{#each weekDays as day (day.value)}
						<Select.Item value={day.value} label={day.label} />
					{/each}
				</Select.Content>
			</Select.Root>
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_appearance_friend_log_header()}>
		<SettingsItem label={m.view_settings_appearance_friend_log_hide_unfriends()} toggle>
			<Switch checked={tableSettings.hideUnfriends} aria-label={m.view_settings_appearance_friend_log_hide_unfriends()} onCheckedChange={(v) => (tableSettings.hideUnfriends = v)} />
		</SettingsItem>
	</SettingsGroup>

	<SettingsGroup title={m.view_settings_appearance_user_colors_header()}>
		<SettingsItem label={m.view_settings_appearance_user_colors_random_colors_from_user_id()} description={m.view_settings_appearance_user_colors_random_colors_from_user_id_description()} toggle>
			<Switch checked={tableSettings.randomUserColours} aria-label={m.view_settings_appearance_user_colors_random_colors_from_user_id()} onCheckedChange={(v) => (tableSettings.randomUserColours = v)} />
		</SettingsItem>
		<div class="flex flex-col gap-2 py-2">
			{#each trustColorEntries as entry (entry.key)}
				<div class="flex items-center gap-3">
					<span class={entry.tagClass}>{entry.label}</span>
					<ColorPickerButton value={appearance.trustColor.value[entry.key]} presets={entry.presets} onchange={(color) => appearance.setTrustColor(entry.key, color)} />
				</div>
			{/each}
		</div>
	</SettingsGroup>
	<SettingsGroup title="Custom CSS">
		{#snippet description()}Your own stylesheet, applied after the app's (the original's custom.css). Alt+Shift+R re-applies it.{/snippet}
		<SettingsItem label="Custom CSS" description={appearance.customCss.value.trim() ? customCssLines(appearance.customCss.value) : 'None'}>
			<Button variant="outline" size="sm" onclick={editCustomCss}>Edit</Button>
		</SettingsItem>
	</SettingsGroup>
</div>

<Dialog.Root bind:open={customFontDialogOpen}>
	<Dialog.Content class="sm:max-w-md">
		<Dialog.Header>
			<Dialog.Title>{m.view_settings_appearance_appearance_font_family_custom_dialog_title()}</Dialog.Title>
			<Dialog.Description>{m.view_settings_appearance_appearance_font_family_custom_dialog_description()}</Dialog.Description>
		</Dialog.Header>
		<Input bind:value={customFontInput} placeholder="'My Font', Arial, sans-serif" onkeydown={(e) => e.key === 'Enter' && saveCustomFont()} />
		<Dialog.Footer>
			<Button variant="outline" onclick={() => (customFontDialogOpen = false)}>{m.view_login_qr_cancel()}</Button>
			<Button onclick={saveCustomFont}>{m.dialog_alertdialog_ok()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>

<TablePageSizesDialog bind:open={tablePageSizesDialogOpen} />
<TableLimitsDialog bind:open={tableLimitsDialogOpen} />

<Dialog.Root bind:open={customCssDialogOpen}>
	<Dialog.Content class="sm:max-w-2xl">
		<Dialog.Header>
			<Dialog.Title>Custom CSS</Dialog.Title>
			<Dialog.Description>Stored in this browser. Leave it empty to remove it.</Dialog.Description>
		</Dialog.Header>
		<Textarea bind:value={customCssInput} spellcheck={false} class="h-80 font-mono text-xs" placeholder={'.x-friend-item { padding: 2px; }'} aria-label="Custom CSS" />
		<Dialog.Footer>
			<Button variant="outline" onclick={() => (customCssDialogOpen = false)}>{m.view_login_qr_cancel()}</Button>
			<Button onclick={saveCustomCss}>{m.dialog_config_json_save()}</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
