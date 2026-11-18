// When the onboarding welcome (spotlight-dialog.svelte) is due. The original mounts its
// SpotlightDialog in MainLayout, the layout of someone already logged in, so it never shows over
// the login page. Here the same layout also asks for the server token and shows the VRChat login,
// so the welcome waits for both: it is for someone who can use what it points at.

export type WelcomeState = {
	/** The welcome was dismissed in this browser before. */
	seen: boolean;
	/** A server token is stored. */
	hasToken: boolean;
	/** The token prompt is up: there is no token, or the server refused the stored one. */
	needsToken: boolean;
	/** A VRChat account is signed in. */
	signedIn: boolean;
};

export function welcomeDue(state: WelcomeState): boolean {
	return !state.seen && state.hasToken && !state.needsToken && state.signedIn;
}
