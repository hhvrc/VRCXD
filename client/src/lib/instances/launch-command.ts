// The original's eventLaunchCommand (stores/vrcx.js): what a vrcx:// link asks for, as
// "command/argument". Parsed here; run by the /launch page.

export const SWITCH_AVATAR_CONFIRM_KEY = 'VRCX_showConfirmationOnSwitchAvatar';

export type LaunchCommand =
	| { kind: 'world'; input: string; worldId: string }
	| { kind: 'avatar'; avatarId: string }
	| { kind: 'user'; userId: string }
	| { kind: 'group'; groupId: string }
	| { kind: 'local-favorite'; type: 'World' | 'Avatar'; targetId: string; group: string }
	| { kind: 'add-avatar-provider'; url: string }
	| { kind: 'switch-avatar'; avatarId: string }
	| { kind: 'import'; type: 'avatar' | 'world' | 'friend'; data: string }
	| { kind: 'invalid'; message: string }
	| { kind: 'unknown' };

const AVATAR_ID = /^avtr_[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}$/;

/** "vrcx://world/wrld_..." or "world/wrld_...", as the desktop passes it on. */
export function parseLaunchCommand(raw: string): LaunchCommand {
	const input = raw.replace(/^vrcx:\/\//i, '').replace(/^\/+/, '');
	const args = input.split('/');
	const command = args[0];
	const arg = args[1]?.trim() ?? '';
	switch (command) {
		case 'world':
			return { kind: 'world', input: input.replace('world/', ''), worldId: arg };
		case 'avatar':
			return arg ? { kind: 'avatar', avatarId: arg } : { kind: 'unknown' };
		case 'user':
			return arg ? { kind: 'user', userId: arg } : { kind: 'unknown' };
		case 'group':
			return arg ? { kind: 'group', groupId: arg } : { kind: 'unknown' };
		case 'local-favorite-world':
		case 'local-favorite-avatar': {
			const [targetId, group] = arg.split(':');
			const type = command === 'local-favorite-world' ? 'World' : 'Avatar';
			if (!targetId || !group) return { kind: 'invalid', message: `Invalid local favorite ${type.toLowerCase()} command` };
			return { kind: 'local-favorite', type, targetId, group };
		}
		case 'addavatardb': {
			const url = input.replace('addavatardb/', '');
			return url ? { kind: 'add-avatar-provider', url } : { kind: 'unknown' };
		}
		case 'switchavatar':
			return AVATAR_ID.test(arg) ? { kind: 'switch-avatar', avatarId: arg } : { kind: 'invalid', message: 'Invalid Avatar ID' };
		case 'import': {
			const type = args[1];
			if (type !== 'avatar' && type !== 'world' && type !== 'friend') return { kind: 'unknown' };
			return { kind: 'import', type, data: input.replace(`import/${type}/`, '') };
		}
		default:
			return { kind: 'unknown' };
	}
}
