import { describe, expect, it } from 'vitest';
import { parseLaunchCommand } from '../instances/launch-command.js';

describe('parseLaunchCommand', () => {
	it('reads dialogs to open, with or without the scheme', () => {
		expect(parseLaunchCommand('vrcx://user/usr_1')).toEqual({ kind: 'user', userId: 'usr_1' });
		expect(parseLaunchCommand('avatar/avtr_x')).toEqual({ kind: 'avatar', avatarId: 'avtr_x' });
		expect(parseLaunchCommand('group/grp_1')).toEqual({ kind: 'group', groupId: 'grp_1' });
		expect(parseLaunchCommand('world/wrld_1:123~private(usr_1)')).toEqual({ kind: 'world', input: 'wrld_1:123~private(usr_1)', worldId: 'wrld_1:123~private(usr_1)' });
	});

	it('reads local favorites and rejects ones without a group', () => {
		expect(parseLaunchCommand('local-favorite-world/wrld_1:Chill')).toEqual({ kind: 'local-favorite', type: 'World', targetId: 'wrld_1', group: 'Chill' });
		expect(parseLaunchCommand('local-favorite-avatar/avtr_1')).toEqual({ kind: 'invalid', message: 'Invalid local favorite avatar command' });
	});

	it('keeps the whole provider address and import data', () => {
		expect(parseLaunchCommand('addavatardb/https://example.com/search')).toEqual({ kind: 'add-avatar-provider', url: 'https://example.com/search' });
		expect(parseLaunchCommand('import/world/wrld_1,wrld_2')).toEqual({ kind: 'import', type: 'world', data: 'wrld_1,wrld_2' });
		expect(parseLaunchCommand('import/other/x')).toEqual({ kind: 'unknown' });
	});

	it('checks switchavatar ids as the original does', () => {
		const id = 'avtr_0123abcd-0123-4567-89ab-0123456789ab';
		expect(parseLaunchCommand(`switchavatar/${id}`)).toEqual({ kind: 'switch-avatar', avatarId: id });
		expect(parseLaunchCommand('switchavatar/avtr_nope')).toEqual({ kind: 'invalid', message: 'Invalid Avatar ID' });
		expect(parseLaunchCommand('nothing/here')).toEqual({ kind: 'unknown' });
	});
});
