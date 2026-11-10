import { m } from '#lib/paraglide/messages.js';
// The original's shared/constants/fonts.js: the Western font choices and the CJK font packs.
// Inter and Noto Sans CJK are bundled (styles/fonts.css); the rest load from their CDN when picked.

export const APP_FONT_DEFAULT_KEY = 'inter';
export const APP_CJK_FONT_PACK_DEFAULT_KEY = 'noto';

export const APP_FONT_CONFIG: Record<string, { label: string; cssName: string; cssImport?: string }> = {
	inter: { get label() { return m.view_settings_appearance_appearance_font_family_inter(); }, cssName: "'Inter Variable'" },
	noto_sans: {
		get label() { return m.view_settings_appearance_appearance_font_family_noto_sans(); },
		cssName: "'Noto Sans'",
		cssImport: "@import url('https://fonts.googleapis.com/css2?family=Noto+Sans:ital,wght@0,100..900;1,100..900&display=swap');"
	},
	geist: {
		get label() { return m.view_settings_appearance_appearance_font_family_geist(); },
		cssName: "'Geist'",
		cssImport: "@import url('https://fonts.googleapis.com/css2?family=Geist:wght@100..900&display=swap');"
	},
	nunito_sans: {
		get label() { return m.view_settings_appearance_appearance_font_family_nunito_sans(); },
		cssName: "'Nunito Sans'",
		cssImport: "@import url('https://fonts.googleapis.com/css2?family=Nunito+Sans:ital,opsz,wght@0,6..12,200..1000;1,6..12,200..1000&display=swap');"
	},
	ibm_plex_sans: {
		get label() { return m.view_settings_appearance_appearance_font_family_ibm_plex_sans(); },
		cssName: "'IBM Plex Sans'",
		cssImport: "@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:ital,wght@0,100..700;1,100..700&display=swap');"
	},
	jetbrains_mono: {
		get label() { return m.view_settings_appearance_appearance_font_family_jetbrains_mono(); },
		cssName: "'JetBrains Mono'",
		cssImport: "@import url('https://fonts.googleapis.com/css2?family=JetBrains+Mono:ital,wght@0,100..800;1,100..800&display=swap');"
	},
	fantasque_sans_mono: {
		get label() { return m.view_settings_appearance_appearance_font_family_fantasque_sans_mono(); },
		cssName: "'Fantasque Sans Mono'",
		cssImport: "@import url('https://fonts.cdnfonts.com/css/fantasque-sans-mono');"
	},
	system_ui: { get label() { return m.view_settings_appearance_appearance_font_family_system_ui(); }, cssName: 'system-ui' },
	custom: { get label() { return m.view_settings_appearance_appearance_font_family_custom(); }, cssName: '' }
};

export const APP_FONT_FAMILIES = Object.keys(APP_FONT_CONFIG);

type CjkNames = { jp: string; kr: string; sc: string; tc: string };

const PHT = 'https://cdn.jsdelivr.net/gh/map1en/pht@1.0.0';
const phtFace = (family: string, file: string, weight: number) =>
	`@font-face { font-family: '${family}'; src: url('${PHT}/${file}') format('woff2'); font-weight: ${weight}; font-display: swap; }`;

export const APP_CJK_FONT_PACK_CONFIG: Record<string, { label: string; cssName: CjkNames; cssImport?: string }> = {
	noto: {
		get label() { return m.view_settings_appearance_appearance_cjk_font_pack_noto(); },
		cssName: { jp: "'Noto Sans JP Variable'", kr: "'Noto Sans KR Variable'", sc: "'Noto Sans SC Variable'", tc: "'Noto Sans TC Variable'" }
	},
	pht: {
		get label() { return m.view_settings_appearance_appearance_cjk_font_pack_pht(); },
		cssName: { jp: "'PHT Sans JP'", kr: "'PHT Sans KR'", sc: "'PHT Sans SC'", tc: "'PHT Sans TC'" },
		cssImport: [
			phtFace('PHT Sans SC', 'sc/phtsansSC-Regular.woff2', 400),
			phtFace('PHT Sans SC', 'sc/phtsansSC-Medium.woff2', 500),
			phtFace('PHT Sans SC', 'sc/phtsansSC-SemiBold.woff2', 600),
			phtFace('PHT Sans SC', 'sc/phtsansSC-Bold.woff2', 700),
			phtFace('PHT Sans TC', 'tc/phtsansTC-55.woff2', 400),
			phtFace('PHT Sans TC', 'tc/phtsansTC-75.woff2', 600),
			phtFace('PHT Sans JP', 'jp/phtsansJP-Regular.woff2', 400),
			phtFace('PHT Sans JP', 'jp/phtsansJP-Medium.woff2', 500),
			phtFace('PHT Sans JP', 'jp/phtsansJP-Bold.woff2', 700),
			phtFace('PHT Sans KR', 'kr/phtsansKR-Regular.woff2', 400),
			phtFace('PHT Sans KR', 'kr/phtsansKR-Medium.woff2', 500),
			phtFace('PHT Sans KR', 'kr/phtsansKR-Bold.woff2', 700)
		].join('\n')
	},
	system: {
		get label() { return m.view_settings_appearance_appearance_font_family_system_ui(); },
		cssName: { jp: 'system-ui', kr: 'system-ui', sc: 'system-ui', tc: 'system-ui' }
	}
};

export const APP_CJK_FONT_PACKS = Object.keys(APP_CJK_FONT_PACK_CONFIG);
