import { parseLocation } from '#lib/instances/location.js';
import type { PublicProfile } from '#lib/settings/profile-theme.js';

// What the main pop-up is showing. Opening anything from inside it stacks, and the breadcrumbs
// walk back along the stack, like the original's dialog crumbs (MainDialogContainer.vue).
export type DialogKind = 'user' | 'world' | 'avatar' | 'group' | 'group-moderation' | 'group-roles';
type Entry = { kind: DialogKind; id: string; location?: string; shortName?: string };

class MainDialogState {
	#stack = $state<Entry[]>([]);
	#labels = $state<Record<string, string>>({});
	// Users' VRChat public profiles (theme and background), the original's cachedProfiles.
	#profiles = $state<Record<string, PublicProfile>>({});

	profileOf(userId: string): PublicProfile | undefined {
		return this.#profiles[userId];
	}

	setProfile(userId: string, profile: PublicProfile): void {
		this.#profiles[userId] = profile;
	}

	get id(): string {
		return this.#stack.at(-1)?.id ?? '';
	}

	get kind(): DialogKind {
		return this.#stack.at(-1)?.kind ?? 'user';
	}

	/** The instance a world was opened from, which its Instances tab puts first. */
	get location(): string | undefined {
		return this.#stack.at(-1)?.location;
	}

	/** The short name a world was opened with (a vrch.at link), which unlocks its instance. */
	get shortName(): string | undefined {
		return this.#stack.at(-1)?.shortName;
	}

	get isOpen(): boolean {
		return this.#stack.length > 0;
	}

	get crumbs(): { id: string; label: string }[] {
		return this.#stack.map(({ id }) => ({ id, label: this.#labels[id] ?? id }));
	}

	#push(entry: Entry): void {
		if (!entry.id) return;
		const below = entry.id === this.id && entry.kind === this.kind ? this.#stack.slice(0, -1) : this.#stack;
		this.#stack = [...below, entry];
	}

	open(userId: string): void {
		this.#push({ kind: 'user', id: userId });
	}

	/** Takes a world id or a full instance location, and the short name it was opened with, the way the original's showWorldDialog does. */
	openWorld(location: string, shortName?: string | null): void {
		const L = parseLocation(location);
		this.#push({ kind: 'world', id: L.worldId, location: L.instanceId ? location : undefined, shortName: shortName || undefined });
	}

	openAvatar(avatarId: string): void {
		this.#push({ kind: 'avatar', id: avatarId });
	}

	openGroup(groupId: string): void {
		this.#push({ kind: 'group', id: groupId });
	}

	/** The group member moderation tool; open it through showGroupMemberModeration (group-moderation.svelte.ts). */
	openGroupModeration(groupId: string): void {
		this.#push({ kind: 'group-moderation', id: groupId });
	}

	/** The original's showGroupRolesDialog: manage a group's roles, or view them. */
	openGroupRoles(groupId: string): void {
		this.#push({ kind: 'group-roles', id: groupId });
	}

	goTo(index: number): void {
		this.#stack = this.#stack.slice(0, index + 1);
	}

	/** Steps back one, closing the pop-up when there is nothing behind. */
	back(): void {
		this.goTo(this.#stack.length - 2);
	}

	setLabel(id: string, label: string): void {
		this.#labels[id] = label;
	}

	close(): void {
		this.#stack = [];
	}
}

export const userDialog = new MainDialogState();
