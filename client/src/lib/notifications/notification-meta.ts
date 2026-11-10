import Ban from '@lucide/svelte/icons/ban';
import BellOff from '@lucide/svelte/icons/bell-off';
import Check from '@lucide/svelte/icons/check';
import LinkIcon from '@lucide/svelte/icons/link';
import MessageCircle from '@lucide/svelte/icons/message-circle';
import Reply from '@lucide/svelte/icons/reply';
import Tag from '@lucide/svelte/icons/tag';
import X from '@lucide/svelte/icons/x';
import { groupHref, userHref, worldHref } from '#lib/api/links.js';
import type { Notification } from '#lib/api/types.js';
import { fromNow as relativeTime } from '#lib/helpers/format.js';

// What the Notifications page and the notification center share: type names, categories, links and
// which buttons a notification gets.
export type NotificationDetails = {
	location?: string;
	emojiId?: string;
	emojiVersion?: number | string;
	worldId?: string;
	worldName?: string;
	groupName?: string;
	groupId?: string;
	imageUrl?: string;
	inviteMessage?: string;
	requestMessage?: string;
	responseMessage?: string;
};

export const NOTIFICATION_LABELS: Record<string, string> = {
	requestInvite: 'Request Invite',
	invite: 'Invite',
	requestInviteResponse: 'Request Invite Response',
	inviteResponse: 'Invite Response',
	friendRequest: 'Friend Request',
	ignoredFriendRequest: 'Ignored Friend Request',
	message: 'Message',
	boop: 'Boop',
	'event.announcement': 'Event Announcement',
	groupChange: 'Group Change',
	'group.announcement': 'Group Announcement',
	'group.informative': 'Group Informative',
	'group.invite': 'Group Invite',
	'group.joinRequest': 'Group Join Request',
	'group.transfer': 'Group Transfer',
	'group.queueReady': 'Queue Ready',
	'group.event.created': 'Group Event Created',
	'group.event.starting': 'Group Event Starting',
	'moderation.warning.group': 'Moderation Warning Group',
	'moderation.report.closed': 'Moderation Report Closed',
	'moderation.contentrestriction': 'Moderation Content Restriction',
	'moderation.sanction': 'Moderation Sanction',
	'instance.closed': 'Instance Closed',
	'instance.announcement': 'Instance Announcement',
	'badge.earned': 'Badge Earned',
	'vrcplus.gift': 'VRC+ Gift',
	'economy.received.gift': 'Gift Received',
	'economy.alert': 'Economy Alert',
	'twitchdrop.fulfilled': 'Twitch Drop Fulfilled'
};

export const notificationLabel = (type: string) => NOTIFICATION_LABELS[type] ?? type;

const FRIEND_TYPES = new Set(['friendRequest', 'ignoredFriendRequest', 'invite', 'requestInvite', 'inviteResponse', 'requestInviteResponse', 'boop']);
const GROUP_PREFIXES = ['group.', 'moderation.'];
const GROUP_EXACT = new Set(['groupChange', 'event.announcement']);

/** The original's getNotificationCategory. */
export function notificationCategory(type: string | undefined): 'friend' | 'group' | 'other' {
	if (!type) return 'other';
	if (FRIEND_TYPES.has(type)) return 'friend';
	if (GROUP_EXACT.has(type) || GROUP_PREFIXES.some((p) => type.startsWith(p))) return 'group';
	return 'other';
}

export function detailsOf(row: Notification): NotificationDetails {
	const value = row.details;
	if (!value) return {};
	if (typeof value !== 'string') return value as NotificationDetails;
	try {
		return JSON.parse(value) as NotificationDetails;
	} catch {
		return {};
	}
}

/**
 * The original's applyBoopLegacyHandling, applied when shown: a boop's text is its title, with a
 * default emoji's name after it, and its picture is the emoji file VRChat names in the details.
 */
export function boopView(row: Notification): { text: string; imageUrl?: string } | undefined {
	if (row.type !== 'boop' || !row.title) return undefined;
	const { emojiId, emojiVersion } = detailsOf(row);
	if (emojiId?.startsWith('default_')) return { text: `${row.title} ${emojiId.replace('default_', '')}` };
	return { text: row.title, imageUrl: emojiId ? `https://api.vrchat.cloud/api/1/file/${emojiId}/${emojiVersion ?? 1}` : undefined };
}

export function linkHref(link: string | null | undefined): string | undefined {
	if (!link) return undefined;
	const [kind, rest] = link.split(/:(.*)/s);
	if (kind === 'group') return groupHref(rest);
	if (kind === 'user') return userHref(rest);
	if (kind === 'event') return rest?.includes(',') ? groupHref(rest.split(',')[0]) : undefined;
	if (kind === 'world') return worldHref(rest);
	return undefined;
}

export function openLink(link: string | null | undefined) {
	if (!link) return;
	if (linkHref(link)) return;
	if (link.startsWith('http')) window.open(link, '_blank', 'noopener,noreferrer');
}

/** The original hides Decline on answers, boops, messages and group, moderation, instance and economy notices. */
export function canDecline(row: Notification): boolean {
	return (
		row.type !== 'requestInviteResponse' &&
		row.type !== 'inviteResponse' &&
		row.type !== 'message' &&
		row.type !== 'boop' &&
		row.type !== 'groupChange' &&
		!row.type.includes('group.') &&
		!row.type.includes('moderation.') &&
		!row.type.includes('instance.') &&
		!row.link?.startsWith('economy.')
	);
}

const RESPONSE_ICONS: Record<string, typeof Check> = { check: Check, cancel: X, ban: Ban, 'bell-slash': BellOff };

export function responseIcon(row: Notification, response: NonNullable<Notification['responses']>[number]) {
	if (response.type === 'link') return LinkIcon;
	if (response.icon === 'reply') return row.type === 'boop' ? MessageCircle : Reply;
	return RESPONSE_ICONS[response.icon ?? ''] ?? Tag;
}

export const isNotificationExpired = (row: Notification, now = Date.now()) => row.hidden || (!!row.expiresAt && now >= Date.parse(row.expiresAt));

/** dayjs's fromNow(true), which the notification center shows: "a few seconds", "5 minutes", "a day". */
export const fromNow = (datetime: string | undefined, now = Date.now()): string => relativeTime(datetime, now, true);
