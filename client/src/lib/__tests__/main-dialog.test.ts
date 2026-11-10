import { beforeEach, describe, expect, test } from 'vitest';

const { userDialog } = await import('../people/user-dialog.svelte.js');

// The original's showWorldDialog(tag, shortName): the world pop-up keeps the instance and the
// short name it was opened with, and the crumbs walk back to them.
describe('the world pop-up keeps its short name', () => {
	beforeEach(() => userDialog.close());

	test('a world opened from an instance keeps the location and the short name', () => {
		userDialog.openWorld('wrld_a:123~hidden(usr_x)', 'AbCdEfGh');
		expect(userDialog.kind).toBe('world');
		expect(userDialog.id).toBe('wrld_a');
		expect(userDialog.location).toBe('wrld_a:123~hidden(usr_x)');
		expect(userDialog.shortName).toBe('AbCdEfGh');
	});

	test('a world opened by id, or with a null short name, has none', () => {
		userDialog.openWorld('wrld_a');
		expect(userDialog.location).toBeUndefined();
		expect(userDialog.shortName).toBeUndefined();
		userDialog.openWorld('wrld_b:1', null);
		expect(userDialog.shortName).toBeUndefined();
	});

	test('going back through the crumbs returns to the short name', () => {
		userDialog.openWorld('wrld_a:1', 'AbCdEfGh');
		userDialog.open('usr_b');
		expect(userDialog.shortName).toBeUndefined();
		userDialog.back();
		expect(userDialog.shortName).toBe('AbCdEfGh');
	});
});
