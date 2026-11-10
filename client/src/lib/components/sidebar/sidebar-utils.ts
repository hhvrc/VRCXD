import type { Friend } from '#lib/api/types.js';
import { nameColour } from '#lib/people/user-colour.js';

// The original's userStatusClass for avatar dots, over the server's friend rows.
export function userStatusClass(user: { state?: string; status?: string; location?: string | null; platform?: string | null }, pendingOffline = false): string {
	const classes = ['status-icon'];
	if (pendingOffline) classes.push('offline');
	else if (user.state === 'active') {
		classes.push(user.status === 'join me' ? 'active-joinme' : user.status === 'ask me' ? 'active-askme' : user.status === 'busy' ? 'active-busy' : 'active');
	} else if (user.state !== 'online' || user.location === 'offline') {
		classes.push('offline');
	} else if (user.status === 'active') classes.push('online');
	else if (user.status === 'join me') classes.push('joinme');
	else if (user.status === 'ask me') classes.push('askme');
	else if (user.status === 'busy') classes.push('busy');
	else return '';
	if (user.platform && user.platform !== 'standalonewindows' && user.platform !== 'web' && user.state === 'online') classes.push('mobile');
	return classes.join(' ');
}

/** Own status dot: the status alone, as the original does for the current user. */
export function ownStatusClass(status: string, platform?: string | null): string {
	const map: Record<string, string> = { active: 'online', 'join me': 'joinme', 'ask me': 'askme', busy: 'busy' };
	const mobile = !!platform && platform !== 'standalonewindows' && platform !== 'web';
	return ['status-icon', map[status] ?? '', mobile ? 'mobile' : ''].filter(Boolean).join(' ');
}

/** The original's $userColour: a colour per user when that option is on, otherwise the rank colour with troll and moderator overrides. */
export function userColour(friend: Friend, dark: boolean): string {
	return nameColour(friend, dark);
}

export type FriendGroup = { key: string; name: string; members: Set<string> };
export type SidebarFriend = Friend;
