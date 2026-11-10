// The original's showEditProfileDialog: one Edit Profile dialog for the whole app, opened from the
// sidebar's own row and from your own user page, which reload once a save goes through.
class EditProfileState {
	open = $state(false);
	#saved = new Set<() => void>();

	show() {
		this.open = true;
	}

	/** Runs after each successful save; returns the unsubscribe. */
	onSaved(listener: () => void): () => void {
		this.#saved.add(listener);
		return () => this.#saved.delete(listener);
	}

	notifySaved() {
		for (const listener of this.#saved) listener();
	}
}

export const editProfile = new EditProfileState();
