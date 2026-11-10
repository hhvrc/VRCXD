import { ApiError } from '#lib/api/client.js';

// The loading flag and error line a tool dialog shows around each fetch.
export class LoadState {
	isLoading = $state(false);
	error = $state('');

	/** Clears the error, runs `task` with the loading flag up, and keeps its failure as the error. */
	async run(task: () => Promise<void>) {
		this.error = '';
		this.isLoading = true;
		try {
			await task();
		} catch (err) {
			this.error = ApiError.from(err).message;
		} finally {
			this.isLoading = false;
		}
	}
}
