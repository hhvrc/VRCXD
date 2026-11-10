// The original's login network issue hint (stores/auth.js): three sign-in failures VRChat answers
// with a 401 that is not a wrong password, within 90 seconds and with the same username and
// password, point at the troubleshooting guide once.

export const TROUBLESHOOTING_URL = 'https://github.com/vrcx-team/VRCX/wiki/Troubleshooting#401-authuser--connection-issues';

const WINDOW_MS = 90_000;
const THRESHOLD = 3;

export class LoginNetworkHint {
	#timestamps: number[] = [];
	#fingerprint = '';
	hinted = false;

	/** Forgets the failures; typing other credentials does this too. */
	reset() {
		this.#timestamps = [];
		this.#fingerprint = '';
		this.hinted = false;
	}

	/** Records one attempt's credentials; a change starts the count again and returns true. */
	attempt(username: string, password: string): boolean {
		const fingerprint = `${username}\u0000${password}`;
		const changed = this.#fingerprint !== '' && this.#fingerprint !== fingerprint;
		if (changed) this.reset();
		this.#fingerprint = fingerprint;
		return changed;
	}

	/** Whether a failed attempt counts: a 401 whose message is not VRChat's wrong-password one. */
	static counts(status: number, message: string | undefined): boolean {
		return status === 401 && !(message ?? '').includes('Invalid Username/Email or Password');
	}

	/** Records a counted failure; true exactly when the hint should show now. */
	failed(now = Date.now()): boolean {
		this.#timestamps = this.#timestamps.filter((t) => t > now - WINDOW_MS);
		this.#timestamps.push(now);
		if (this.hinted || this.#timestamps.length < THRESHOLD) return false;
		this.hinted = true;
		return true;
	}
}
