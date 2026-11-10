import { m } from '#lib/paraglide/messages.js';
import { toast } from 'svelte-sonner';
import { accountPath, api } from '#lib/api/client.js';
import { userDialog } from '#lib/people/user-dialog.svelte.js';
import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

// The original's search store directAccessParse / directAccessWorld / directAccessPaste: a pasted
// VRChat URL or ID opens its pop-up; anything else asks for one.

const account = () => vrchatAuth.ownerUserId ?? '';

/** The original's verifyShortName: a short name to its instance, opened in the world pop-up. */
export async function openShortName(location: string, shortName: string): Promise<void> {
	const instance = await api
		.get<{ location?: string; shortName?: string }>(accountPath(account(), `/search/short-name/${encodeURIComponent(shortName)}`))
		.catch(() => null);
	const target = instance?.location || location;
	if (instance?.shortName && instance.location) userDialog.openWorld(instance.location, instance.shortName);
	else if (target) userDialog.openWorld(target);
	else toast.error(m.prompt_direct_access_omni_message_error());
}

async function openGroupShortCode(shortCode: string): Promise<void> {
	const groups = await api
		.get<{ id: string; shortCode?: string; discriminator?: string }[]>(accountPath(account(), '/search/groups/strict'), { query: shortCode })
		.catch(() => []);
	const group = groups.find((g) => `${g.shortCode}.${g.discriminator}` === shortCode);
	if (group) userDialog.openGroup(group.id);
}

function parseWorld(text: string): boolean {
	let input = text;
	if (input.startsWith('/home/')) input = `https://vrchat.com${input}`;
	if (input.length === 8) {
		void openShortName('', input);
		return true;
	}
	if (/^https:\/\/vrch\.at\/[A-Za-z0-9]{8}$/.test(input)) {
		void openShortName('', input.substring(16, 24));
		return true;
	}
	if (input.startsWith('https://vrchat.')) {
		let url: URL;
		try {
			url = new URL(input);
		} catch {
			return false;
		}
		const parts = url.pathname.split('/');
		if (parts.length >= 4 && parts[2] === 'world') {
			userDialog.openWorld(parts[3]);
			return true;
		}
		if (url.pathname.substring(5, 12) === '/launch') {
			const worldId = url.searchParams.get('worldId');
			const instanceId = url.searchParams.get('instanceId');
			if (worldId && instanceId) {
				const location = `${worldId}:${instanceId}`;
				const shortName = url.searchParams.get('shortName');
				if (shortName) void openShortName(location, shortName);
				else userDialog.openWorld(location);
				return true;
			}
			if (worldId) {
				userDialog.openWorld(worldId);
				return true;
			}
		}
		return false;
	}
	if (input.startsWith('wrld_') || input.startsWith('wld_') || input.startsWith('o_')) {
		// Malformed inputs cut out of a launch URL, as the original accepts.
		if (input.includes('&instanceId=')) return parseWorld(`https://vrchat.com/home/launch?worldId=${input}`);
		userDialog.openWorld(input.trim());
		return true;
	}
	return false;
}

/** Opens what a VRChat URL or ID names; false when it names nothing. */
export function directAccessParse(input: string): boolean {
	if (!input) return false;
	if (parseWorld(input)) return true;
	if (input.startsWith('https://vrchat.')) {
		let parts: string[];
		try {
			parts = new URL(input).pathname.split('/');
		} catch {
			return false;
		}
		if (parts.length < 4) return false;
		const [type, id] = [parts[2], parts[3]];
		if (type === 'user') userDialog.open(id);
		else if (type === 'avatar') userDialog.openAvatar(id);
		else if (type === 'group') userDialog.openGroup(id);
		else return false;
		return true;
	}
	if (input.startsWith('https://vrc.group/')) {
		void openGroupShortCode(input.substring(18));
		return true;
	}
	if (/^[A-Za-z0-9]{3,6}\.[0-9]{4}$/.test(input)) {
		void openGroupShortCode(input);
		return true;
	}
	if (input.startsWith('usr_') || /^[A-Za-z0-9]{10}$/.test(input)) {
		userDialog.open(input);
		return true;
	}
	if (input.startsWith('avtr_') || input.startsWith('b_')) {
		userDialog.openAvatar(input);
		return true;
	}
	if (input.startsWith('grp_')) {
		userDialog.openGroup(input);
		return true;
	}
	return false;
}

class DirectAccess {
	/** The prompt's text while it is open. */
	prompt = $state<string | undefined>();

	/** Ctrl/Cmd+D and the nav entry: open what the clipboard holds, or ask. */
	async paste(): Promise<void> {
		if (this.prompt !== undefined) return;
		const text = await navigator.clipboard?.readText().catch(() => '') ?? '';
		if (directAccessParse(text.trim())) toast.success(m.prompt_direct_access_omni_message_opened_from_clipboard());
		else this.prompt = '';
	}

	submit(value: string): void {
		this.prompt = undefined;
		if (!directAccessParse(value.trim())) toast.error(m.prompt_direct_access_omni_message_error());
	}
}

export const directAccess = new DirectAccess();
