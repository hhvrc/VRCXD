import { ApiError } from '#lib/api/client.js';
import type { Confirming, Prompting } from '#lib/components/dialogs/action-dialogs.svelte';
import { toast } from 'svelte-sonner';
import { copyText } from '#lib/helpers/feedback.js';

// How a pop-up's menu runs things: one at a time, success as a toast like the original's, errors in
// its summary card, and the confirm and prompt modals that action-dialogs.svelte draws.
export class DetailActions {
	busy = $state(false);
	error = $state('');
	confirming = $state<Confirming | undefined>();
	prompting = $state<Prompting | undefined>();

	constructor(private readonly reload: () => Promise<unknown>) {}

	reset(): void {
		this.error = '';
	}

	async act(run: () => Promise<unknown>, done?: string, reload = true): Promise<boolean> {
		this.busy = true;
		this.reset();
		try {
			await run();
			if (done) toast.success(done);
			this.confirming = undefined;
			this.prompting = undefined;
			if (reload) await this.reload();
			return true;
		} catch (err) {
			this.error = ApiError.from(err).message;
			this.confirming = undefined;
			return false;
		} finally {
			this.busy = false;
		}
	}

	confirm(command: string, run: () => Promise<unknown>, done?: string, reload = true): void {
		this.confirming = { command, run: () => this.act(run, done, reload) };
	}

	prompt(options: Omit<Prompting, 'run'>, run: (value: string) => Promise<unknown>, done?: string): void {
		this.prompting = { ...options, run: (value) => this.act(() => run(value), done) };
	}

	/** The original's copyToClipboard, with its default message. */
	copy(text: string, message = 'Copied successfully!'): void {
		this.reset();
		void copyText(text, message);
	}
}
