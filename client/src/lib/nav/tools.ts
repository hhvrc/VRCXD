import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
import { m } from '#lib/paraglide/messages.js';
import type { AgentCapability } from '#lib/instances/instance-launch.svelte.js';
import type { AgentFolder } from '#lib/agent/agent.js';
// The original's tool list (src/shared/constants/tools.js): same keys, icons, categories and
// wording. `kind` says how this client runs each one: a dialog here, a job the PC's agent does
// (offered when it declares `capability`), or one nothing here does yet.

export type ToolKind = 'dialog' | 'agent' | 'desktop' | 'pending';

export type Tool = {
	key: string;
	category: string;
	icon: string;
	title: string;
	description: string;
	kind: ToolKind;
	/** For an agent tool: what the PC's agent must declare. */
	capability?: AgentCapability;
	/** For a folder shortcut: the folder the agent opens. */
	folder?: AgentFolder;
};

export const toolCategories = [
	{ key: 'image', get label() { return m.view_tools_pictures_header(); } },
	{ key: 'shortcuts', get label() { return m.view_tools_shortcuts_header(); } },
	{ key: 'group', get label() { return m.view_tools_group_header(); } },
	{ key: 'system', get label() { return m.view_tools_system_tools_header(); } },
	{ key: 'marketplace', get label() { return m.view_tools_marketplace_header(); } },
	{ key: 'other', get label() { return m.view_tools_other_header(); } },
	{ key: 'user', get label() { return m.view_tools_export_header(); } }
];

export const tools: Tool[] = [
	{ key: 'screenshot-metadata', category: 'image', icon: 'ri-camera-line', get title() { return m.view_tools_pictures_screenshot(); }, get description() { return m.view_tools_pictures_screenshot_description(); }, kind: 'dialog' },
	{ key: 'gallery', category: 'image', icon: 'ri-image-line', get title() { return m.view_tools_pictures_gallery(); }, get description() { return m.view_tools_pictures_gallery_description(); }, kind: 'dialog' },
	{ key: 'vrc-photos', category: 'shortcuts', icon: 'ri-folder-image-line', get title() { return m.view_tools_pictures_pictures_vrc_photos(); }, get description() { return m.view_tools_pictures_pictures_vrc_photos_description(); }, kind: 'agent', capability: 'folders', folder: 'photos' },
	{ key: 'steam-screenshots', category: 'shortcuts', icon: 'ri-folder-image-line', get title() { return m.view_tools_pictures_pictures_steam_screenshots(); }, get description() { return m.view_tools_pictures_pictures_steam_screenshots_description(); }, kind: 'agent', capability: 'folders', folder: 'screenshots' },
	{ key: 'vrcx-data', category: 'shortcuts', icon: 'ri-folder-settings-line', get title() { return m.view_tools_shortcuts_vrcx_data(); }, get description() { return m.view_tools_shortcuts_vrcx_data_description(); }, kind: 'agent', capability: 'folders', folder: 'agent_data' },
	{ key: 'vrchat-data', category: 'shortcuts', icon: 'ri-folder-settings-line', get title() { return m.view_tools_shortcuts_vrchat_data(); }, get description() { return m.view_tools_shortcuts_vrchat_data_description(); }, kind: 'agent', capability: 'folders', folder: 'app_data' },
	{ key: 'crash-dumps', category: 'shortcuts', icon: 'ri-folder-warning-line', get title() { return m.view_tools_shortcuts_crash_dumps(); }, get description() { return m.view_tools_shortcuts_crash_dumps_description(); }, kind: 'agent', capability: 'folders', folder: 'crash_dumps' },
	{ key: 'vrchat-config', category: 'system', icon: 'ri-settings-3-line', get title() { return m.view_tools_system_tools_vrchat_config(); }, get description() { return m.view_tools_system_tools_vrchat_config_description(); }, kind: 'agent', capability: 'vrchat_config' },
	{ key: 'launch-options', category: 'system', icon: 'ri-terminal-box-line', get title() { return m.view_settings_advanced_advanced_launch_options(); }, get description() { return m.view_tools_system_tools_launch_options_description(); }, kind: 'desktop' },
	{ key: 'registry-backup', category: 'system', icon: 'ri-archive-stack-line', get title() { return m.view_tools_system_tools_registry_backup(); }, get description() { return m.view_tools_system_tools_registry_backup_description(); }, kind: 'agent', capability: 'registry' },
	{ key: 'auto-change-status', category: 'system', icon: 'ri-user-settings-line', get title() { return m.view_settings_general_automation_auto_change_status_switch(); }, description: 'Change your status automatically', kind: 'dialog' },
	{ key: 'group-calendar', category: 'group', icon: 'ri-calendar-event-line', get title() { return m.view_tools_group_calendar(); }, get description() { return m.view_tools_group_calendar_description(); }, kind: 'dialog' },
	{ key: 'blocked-groups', category: 'group', icon: 'ri-forbid-line', get title() { return m.view_tools_group_blocked_groups(); }, get description() { return m.view_tools_group_blocked_groups_description(); }, kind: 'dialog' },
	{ key: 'group-invites', category: 'group', icon: 'ri-mail-line', get title() { return m.view_tools_group_group_invites(); }, get description() { return m.view_tools_group_group_invites_description(); }, kind: 'dialog' },
	{ key: 'discord-names', category: 'user', icon: 'ri-discord-line', get title() { return m.view_tools_export_discord_names(); }, get description() { return m.view_tools_user_discord_names_description(); }, kind: 'dialog' },
	{ key: 'export-notes', category: 'user', icon: 'ri-file-list-3-line', get title() { return m.view_tools_export_export_notes(); }, get description() { return m.view_tools_export_export_notes_description(); }, kind: 'dialog' },
	{ key: 'export-friend-list', category: 'user', icon: 'ri-file-list-3-line', get title() { return m.view_tools_export_export_friend_list(); }, get description() { return m.view_tools_user_export_friend_list_description(); }, kind: 'dialog' },
	{ key: 'export-own-avatars', category: 'user', icon: 'ri-file-list-3-line', get title() { return m.view_tools_export_export_own_avatars(); }, get description() { return m.view_tools_user_export_own_avatars_description(); }, kind: 'dialog' },
	{ key: 'vrchat-store', category: 'marketplace', icon: 'ri-store-2-line', get title() { return m.view_tools_marketplace_vrchat_store(); }, get description() { return m.view_tools_marketplace_vrchat_store_description(); }, kind: 'dialog' },
	{ key: 'purchase-history', category: 'marketplace', icon: 'ri-shopping-bag-line', get title() { return m.view_tools_other_purchase_history(); }, get description() { return m.view_tools_other_purchase_history_description(); }, kind: 'dialog' },
	{ key: 'edit-invite-message', category: 'other', icon: 'ri-quill-pen-line', get title() { return m.view_tools_other_edit_invite_message(); }, get description() { return m.view_tools_other_edit_invite_message_description(); }, kind: 'dialog' },
	{ key: 'moderation-reports', category: 'other', icon: 'ri-flag-line', get title() { return m.view_tools_other_moderation_reports(); }, get description() { return m.view_tools_other_moderation_reports_description(); }, kind: 'dialog' },
	{ key: 'info-push', category: 'other', icon: 'ri-megaphone-line', get title() { return m.view_tools_other_info_push(); }, get description() { return m.view_tools_other_info_push_description(); }, kind: 'dialog' }
];

/** Quotes a CSV field the way the original's export dialogs do. */
export function csvField(value: string): string {
	// eslint-disable-next-line no-control-regex
	return /[\x00-\x1f,"]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/** The original's Discord name pattern, applied to status text and then the bio. */
export function findDiscordName(text: string | null | undefined): string {
	if (!text) return '';
	return /(?:discord|dc|dis)(?: |=|:|˸|;)(.*)/i.exec(text)?.[1] ?? '';
}

const PINS_KEY = 'vrcx.pinnedTools';

export function readPinnedTools(): string[] {
	const parsed = readSetting<unknown>(PINS_KEY, []);
	return Array.isArray(parsed) ? parsed.filter((k): k is string => typeof k === 'string') : [];
}

export function writePinnedTools(keys: string[]): void {
	storeSetting(PINS_KEY, keys);
}

export type FriendExportRow = {
	userId: string;
	displayName: string;
	memo: string | null;
	note: string | null;
	bio: string | null;
	statusDescription: string | null;
	thumbnailImageUrl: string | null;
};

export type GroupByStatus = {
	id: string;
	groupId: string;
	name: string | null;
	shortCode: string | null;
	discriminator: string | null;
	iconUrl: string | null;
	memberCount: number;
};

export type InviteMessage = {
	id: string;
	slot: number;
	message: string;
	messageType: string;
	updatedAt: string | null;
};

export type ModerationReport = {
	id: string;
	type: string | null;
	contentId: string | null;
	contentName: string | null;
	contentThumbnailImageUrl: string | null;
	category: string | null;
	reason: string | null;
	description: string | null;
	created: string | null;
};

export type ReportOptions = {
	types: string[];
	categories: Record<string, string>;
	reasons: Record<string, string>;
	/** Per report type, its categories and the reasons each offers. */
	options?: Record<string, Record<string, string[]>>;
	categoryOrder?: Record<string, number>;
	categoryTooltips?: Record<string, string>;
	reasonTooltips?: Record<string, string>;
};

export type ProductPurchase = {
	purchaseId: string;
	listingDisplayName: string | null;
	listingSubtitle: string | null;
	listingImageId: string | null;
	listingType: string | null;
	sellerDisplayName: string | null;
	purchasePrice: number;
	isGift: boolean;
	purchaseActive: boolean;
	purchaseDate: string | null;
	purchaseEndDate: string | null;
};

export type CalendarEvent = {
	id: string;
	ownerId: string;
	title: string | null;
	description: string | null;
	imageUrl: string | null;
	category: string | null;
	accessType: string | null;
	seriesId: string | null;
	interestedUserCount: number;
	closeInstanceAfterEndMinutes: number;
	startsAt: string;
	endsAt: string;
	createdAt: string | null;
	userInterest?: { isFollowing: boolean } | null;
};

export type CalendarGroup = { name?: string | null; bannerUrl?: string | null };
export type CalendarResponse = { events: CalendarEvent[]; groups: Record<string, CalendarGroup> };

export type EconomyTransaction = {
	transactionLineId: string;
	listingDisplayName: string | null;
	listingType: string | null;
	reasonLabel: string | null;
	fromUserId: string | null;
	fromUserDisplayName: string | null;
	toUserId: string | null;
	toUserDisplayName: string | null;
	platform: string | null;
	amount: number;
	balance: number;
	date: string | null;
};
