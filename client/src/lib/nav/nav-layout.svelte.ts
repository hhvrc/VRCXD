import { readText, storeSetting } from '#lib/settings/settings-storage.js';
import { m } from '#lib/paraglide/messages.js';
import { allNavItems } from '#lib/nav/nav-items.js';
import { dashboards } from '#lib/nav/dashboards.svelte.js';
import { pinnedTools } from '#lib/nav/pinned-tools.svelte.js';
import { tools, writePinnedTools } from '#lib/nav/tools.js';
import {
	DASHBOARD_NAV_KEY_PREFIX,
	NAV_CONFIG_KEY,
	TOOL_NAV_KEY_PREFIX,
	defaultNavLayout,
	isToolNavKey,
	normalizeHiddenKeys,
	readNavConfig,
	sanitizeLayout,
	type NavConfig,
	type NavDefinition,
	type NavEntry
} from '#lib/nav/nav-layout.js';

// The original's useNavLayout: the saved layout, kept per browser like the original's per-install
// config, and what the nav shows from it. Dashboards and pinned tools are entries too.
class NavLayoutState {
	config = $state<NavConfig>(readNavConfig(readText(NAV_CONFIG_KEY) || null));
	/** Whether the Custom Nav dialog is open. */
	editing = $state(false);

	definitions = $derived<NavDefinition[]>([
		...allNavItems.map((item) => ({ ...item, href: `/${item.key}` })),
		...dashboards.list.map((d) => ({ key: `${DASHBOARD_NAV_KEY_PREFIX}${d.id}`, icon: d.icon, label: d.name, href: `/dashboard/${d.id}` })),
		...tools.filter((t) => pinnedTools.keys.includes(t.key)).map((t) => ({ key: `${TOOL_NAV_KEY_PREFIX}${t.key}`, icon: t.icon, label: t.title, href: `/tools?open=${t.key}` })),
		{ key: 'direct-access', icon: 'ri-compass-3-line', get label() { return m.prompt_direct_access_omni_header(); }, action: 'direct-access' as const }
	]);
	byKey = $derived(new Map(this.definitions.map((d) => [d.key, d])));
	hiddenKeys = $derived(normalizeHiddenKeys(this.config.hiddenKeys, new Set(this.byKey.keys())));
	entries = $derived(sanitizeLayout(this.config.layout, this.hiddenKeys, this.definitions));
	defaults = $derived(sanitizeLayout(defaultNavLayout(), [], this.definitions));

	/** applyCustomNavLayout. A pinned tool left out of the layout is unpinned, as removing it is in the original. */
	save(layout: NavEntry[], hiddenKeys: string[]) {
		const placed = new Set(layout.flatMap((e) => (e.type === 'item' ? [e.key] : e.items)));
		const keptPins = pinnedTools.keys.filter((key) => placed.has(`${TOOL_NAV_KEY_PREFIX}${key}`));
		if (keptPins.length !== pinnedTools.keys.length) {
			pinnedTools.keys = keptPins;
			writePinnedTools(keptPins);
		}
		this.config = { layout: sanitizeLayout(layout, hiddenKeys, this.definitions), hiddenKeys: hiddenKeys.filter((k) => !isToolNavKey(k)) };
		storeSetting(NAV_CONFIG_KEY, this.config);
	}
}

export const navLayout = new NavLayoutState();
