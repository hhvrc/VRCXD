import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';

// The original's dashboard store: dashboards are rows of one or two panels, each panel a page key
// or a widget with its own config, kept under VRCX_dashboardConfigs.
export type PanelValue = string | { key: string; config: Record<string, unknown> } | null;
export type DashboardRow = { panels: PanelValue[]; direction: 'horizontal' | 'vertical' };
export type Dashboard = { id: string; name: string; icon: string; rows: DashboardRow[] };

export const DASHBOARD_STORAGE_KEY = 'VRCX_dashboardConfigs';
export const DEFAULT_DASHBOARD_ICON = 'ri-dashboard-line';
export const NEW_DASHBOARD_BUTTON_KEY = 'VRCX_showNewDashboardButton';

function clonePanel(panel: unknown): PanelValue {
	if (typeof panel === 'string' && panel) return panel;
	if (panel && typeof panel === 'object' && typeof (panel as { key?: unknown }).key === 'string' && (panel as { key: string }).key) {
		const p = panel as { key: string; config?: unknown };
		return { key: p.key, config: p.config && typeof p.config === 'object' ? JSON.parse(JSON.stringify(p.config)) : {} };
	}
	return null;
}

export function cloneRows(rows: unknown): DashboardRow[] {
	if (!Array.isArray(rows)) return [];
	return rows
		.map((row): DashboardRow | null => {
			const panels = Array.isArray(row?.panels) ? row.panels.slice(0, 2).map(clonePanel) : [];
			if (!panels.length) return null;
			return { panels, direction: row?.direction === 'vertical' ? 'vertical' : 'horizontal' };
		})
		.filter((row): row is DashboardRow => row !== null);
}

function sanitize(value: unknown): Dashboard | null {
	if (!value || typeof value !== 'object') return null;
	const d = value as Partial<Dashboard>;
	if (typeof d.id !== 'string' || !d.id) return null;
	return {
		id: d.id,
		name: typeof d.name === 'string' && d.name.trim() ? d.name.trim() : 'Dashboard',
		icon: typeof d.icon === 'string' && d.icon.trim() ? d.icon.trim() : DEFAULT_DASHBOARD_ICON,
		rows: cloneRows(d.rows)
	};
}

function load(): Dashboard[] {
	const parsed = readSetting<{ dashboards?: unknown } | null>(DASHBOARD_STORAGE_KEY, null);
	return (Array.isArray(parsed?.dashboards) ? parsed.dashboards : []).map(sanitize).filter((d: Dashboard | null): d is Dashboard => d !== null);
}

class Dashboards {
	list = $state<Dashboard[]>(load());
	editingId = $state<string | null>(null);
	showNewButton = $state(readSetting<boolean>(NEW_DASHBOARD_BUTTON_KEY, true) === true);

	private save() {
		storeSetting(DASHBOARD_STORAGE_KEY, { dashboards: this.list });
	}

	get(id: string): Dashboard | undefined {
		return this.list.find((d) => d.id === id);
	}

	private nextName(base: string): string {
		const names = new Set(this.list.map((d) => d.name));
		if (!names.has(base)) return base;
		let n = 1;
		while (names.has(`${base} ${n}`)) n++;
		return `${base} ${n}`;
	}

	create(base = 'Dashboard'): Dashboard {
		const dashboard: Dashboard = { id: crypto.randomUUID(), name: this.nextName(base), icon: DEFAULT_DASHBOARD_ICON, rows: [] };
		this.list.push(dashboard);
		this.save();
		return dashboard;
	}

	update(id: string, updates: Partial<Omit<Dashboard, 'id'>>) {
		const index = this.list.findIndex((d) => d.id === id);
		if (index < 0) return;
		const next = sanitize({ ...$state.snapshot(this.list[index]), ...updates, id });
		if (!next) return;
		this.list[index] = next;
		this.save();
	}

	remove(id: string) {
		this.list = this.list.filter((d) => d.id !== id);
		if (this.editingId === id) this.editingId = null;
		this.save();
	}

	setShowNewButton(value: boolean) {
		this.showNewButton = value;
		storeSetting(NEW_DASHBOARD_BUTTON_KEY, value);
	}
}

export const dashboards = new Dashboards();

export const panelKey = (panel: PanelValue): string | null => !panel
	? null
	: typeof panel === 'string' ? panel : panel.key || null;

export const panelConfig = (panel: PanelValue): Record<string, unknown> => !panel || typeof panel === 'string' ? {} : panel.config || {};
