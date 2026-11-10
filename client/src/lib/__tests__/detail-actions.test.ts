import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';

const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('svelte-sonner', () => ({ toast }));

const { DetailActions } = await import('../helpers/detail-actions.svelte.js');

// How the world, avatar, user and group pop-ups run their menu commands, as the original's
// use*DialogCommands: confirmed and prompted commands only run once answered.
describe('DetailActions', () => {
	beforeEach(() => {
		toast.success.mockReset();
		toast.error.mockReset();
	});
	afterEach(() => vi.unstubAllGlobals());

	test('a confirmed command waits for the answer and a cancelled one never runs', async () => {
		const reload = vi.fn(async () => {});
		const actions = new DetailActions(reload);
		const run = vi.fn(async () => {});
		actions.confirm('Delete Favorite', run, 'Favorite removed');
		expect(run).not.toHaveBeenCalled();
		expect(actions.confirming?.command).toBe('Delete Favorite');

		actions.confirming = undefined;
		expect(run).not.toHaveBeenCalled();

		actions.confirm('Delete Favorite', run, 'Favorite removed');
		await actions.confirming!.run();
		expect(run).toHaveBeenCalledTimes(1);
		expect(toast.success).toHaveBeenCalledWith('Favorite removed');
		expect(reload).toHaveBeenCalledTimes(1);
		expect(actions.confirming).toBeUndefined();
	});

	test('a prompted command gets the typed value and a cancelled prompt saves nothing', async () => {
		const actions = new DetailActions(async () => {});
		const save = vi.fn(async (_value: string) => {});
		actions.prompt({ title: 'Rename', description: '', errorMessage: '', value: 'Old' }, save, 'World name changed');
		actions.prompting = undefined;
		expect(save).not.toHaveBeenCalled();

		actions.prompt({ title: 'Rename', description: '', errorMessage: '', value: 'Old' }, save, 'World name changed');
		await actions.prompting!.run('New');
		expect(save).toHaveBeenCalledWith('New');
		expect(actions.prompting).toBeUndefined();
	});

	test('a refused command shows the error and does not reload', async () => {
		const reload = vi.fn(async () => {});
		const actions = new DetailActions(reload);
		const ok = await actions.act(async () => {
			throw new Error('boom');
		}, 'done');
		expect(ok).toBe(false);
		expect(actions.error).toBe('boom');
		expect(toast.success).not.toHaveBeenCalled();
		expect(reload).not.toHaveBeenCalled();
		expect(actions.busy).toBe(false);
	});

	test('copy writes the text and says so, or says it failed', async () => {
		const writeText = vi.fn(async (_text: string) => {});
		vi.stubGlobal('navigator', { clipboard: { writeText } });
		const actions = new DetailActions(async () => {});
		actions.copy('https://vrchat.com/home/world/wrld_123', 'World URL copied to clipboard');
		await vi.waitFor(() => expect(toast.success).toHaveBeenCalledWith('World URL copied to clipboard'));
		expect(writeText).toHaveBeenCalledWith('https://vrchat.com/home/world/wrld_123');

		writeText.mockRejectedValueOnce(new Error('denied'));
		actions.copy('wrld_123');
		await vi.waitFor(() => expect(toast.error).toHaveBeenCalledTimes(1));
	});
});
