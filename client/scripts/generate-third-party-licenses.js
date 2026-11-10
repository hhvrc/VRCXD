// The original's build-scripts/generate-third-party-licenses.js, for this client: after `vite build`
// it reads the license file Vite writes for the bundled npm packages (build.license) and the NuGet
// packages the server ships with (each project's obj/project.assets.json and the NuGet cache), and
// writes the manifest the Open Source Software Notice dialog reads, plus a plain-text notice file,
// into build/licenses/.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const frontendLicensePath = path.join(clientDir, '.svelte-kit', 'output', 'client', '.vite', 'license.md');
const outputDir = path.join(clientDir, 'build', 'licenses');
const serverDir = path.join(clientDir, '..', 'Server');
const nugetCacheDir = process.env.NUGET_PACKAGES || path.join(os.homedir(), '.nuget', 'packages');
// What the server ships: not its tests, test hosts or the migration helper.
const shippedProjects = ['API', 'Common', 'Vrchat', 'LegacyImport'];

const isFile = (file) => !!file && fs.existsSync(file) && fs.statSync(file).isFile();
const readIfExists = (file) => (isFile(file) ? fs.readFileSync(file, 'utf8') : null);
const normalize = (value) => value?.replace(/\r\n/g, '\n').trim() || '';
const sanitizeId = (value) =>
	value
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-|-$/g, '');
const xmlTag = (xml, tag) => xml.match(new RegExp(`<${tag}(?:\\s[^>]*)?>([\\s\\S]*?)<\\/${tag}>`, 'i'))?.[1]?.trim() || '';
const xmlTagAttribute = (xml, tag, attribute) =>
	xml.match(new RegExp(`<${tag}[^>]*${attribute}="([^"]+)"[^>]*>(?:[\\s\\S]*?)<\\/${tag}>`, 'i'))?.[1]?.trim() || '';
const repositoryUrl = (xml) => xml.match(/<repository[^>]*url="([^"]+)"/i)?.[1]?.trim() || '';

function findLicenseFile(packageDir) {
	if (!fs.existsSync(packageDir)) return null;
	const stack = [packageDir];
	while (stack.length) {
		const dir = stack.pop();
		for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
			const full = path.join(dir, entry.name);
			if (entry.isDirectory()) {
				if (!full.includes(`${path.sep}tools${path.sep}`)) stack.push(full);
			} else if (/^(license|licence|notice|copying)(\.[^.]+)?$/i.test(entry.name)) {
				return full;
			}
		}
	}
	return null;
}

/** Vite's license.md: one "## name - version (license)" section per bundled package. */
function parseFrontendLicenses(markdown) {
	const normalized = normalize(markdown);
	if (!normalized) return [];
	return normalized
		.split(/\n(?=## )/g)
		.slice(1)
		.map((section) => {
			const [header, ...body] = section.split('\n');
			const match = header.match(/^##\s+(.+?)\s+-\s+(.+?)\s+\((.+?)\)$/);
			if (!match) return null;
			const [, name, version, license] = match;
			const noticeText = normalize(body.join('\n'));
			return { id: `frontend-${sanitizeId(`${name}-${version}`)}`, name, version, license, sourceType: 'frontend', sourceLabel: 'Frontend bundle', noticeText, needsReview: !license && !noticeText };
		})
		.filter(Boolean)
		.sort((a, b) => a.name.localeCompare(b.name));
}

/** Every NuGet package a shipped project restores, with the projects that use it. */
function collectServerPackages() {
	const collected = new Map();
	for (const project of shippedProjects) {
		const assets = readIfExists(path.join(serverDir, project, 'obj', 'project.assets.json'));
		if (!assets) continue;
		const libraries = JSON.parse(assets).libraries || {};
		for (const [key, library] of Object.entries(libraries)) {
			if (library?.type && library.type !== 'package') continue;
			const slash = key.lastIndexOf('/');
			const entry = collected.get(key) || { name: key.slice(0, slash), version: key.slice(slash + 1), sourceType: 'dotnet', projects: [] };
			entry.projects = [...new Set([...entry.projects, project])].sort();
			collected.set(key, entry);
		}
	}
	return [...collected.values()].sort((a, b) => a.name.localeCompare(b.name));
}

function nugetMetadata(name, version) {
	const packageDir = path.join(nugetCacheDir, name.toLowerCase(), version);
	const nuspecPath = fs.existsSync(packageDir) ? fs.readdirSync(packageDir).filter((f) => f.endsWith('.nuspec')).map((f) => path.join(packageDir, f))[0] : null;
	const metadata = { license: '', licenseUrl: '', projectUrl: '', noticeText: '' };
	if (nuspecPath) {
		const nuspec = fs.readFileSync(nuspecPath, 'utf8');
		const type = xmlTagAttribute(nuspec, 'license', 'type');
		metadata.license = type === 'expression' ? xmlTag(nuspec, 'license') : '';
		metadata.licenseUrl = xmlTag(nuspec, 'licenseUrl');
		metadata.projectUrl = xmlTag(nuspec, 'projectUrl') || repositoryUrl(nuspec);
		const embedded = type === 'file' ? path.join(packageDir, xmlTag(nuspec, 'license').replaceAll('\\', path.sep)) : null;
		const licenseFile = [embedded, findLicenseFile(packageDir)].find(isFile);
		metadata.noticeText = normalize(readIfExists(licenseFile));
	}
	return metadata;
}

function enrichServerPackages(entries) {
	return entries.map((entry) => {
		const metadata = nugetMetadata(entry.name, entry.version);
		return {
			id: `dotnet-${sanitizeId(`${entry.name}-${entry.version}`)}`,
			...entry,
			license: metadata.license || 'Unknown',
			licenseUrl: metadata.licenseUrl,
			projectUrl: metadata.projectUrl,
			noticeText: metadata.noticeText,
			sourceLabel: 'Server (.NET) component',
			needsReview: !metadata.license && !metadata.noticeText
		};
	});
}

function noticeText(frontendMarkdown, entries) {
	const lines = [
		'VRCXD Third-Party Notices',
		'',
		`Generated: ${new Date().toISOString()}`,
		'',
		'========================================',
		'Frontend bundled dependencies',
		'========================================',
		'',
		normalize(frontendMarkdown) || 'No frontend license manifest was available.',
		'',
		'',
		'========================================',
		'Server (.NET) components',
		'========================================',
		''
	];
	for (const entry of entries.filter((e) => e.sourceType !== 'frontend')) {
		lines.push(`${entry.name}${entry.version ? ` - ${entry.version}` : ''} (${entry.license})`, `Source: ${entry.sourceLabel}`);
		if (entry.projects?.length) lines.push(`Used by: ${entry.projects.join(', ')}`);
		if (entry.projectUrl) lines.push(`Project URL: ${entry.projectUrl}`);
		if (entry.licenseUrl) lines.push(`License URL: ${entry.licenseUrl}`);
		lines.push('', entry.noticeText || 'No local license text was available during generation. Review this component before release.', '', '----------------------------------------', '');
	}
	return `${lines.join('\n').trimEnd()}\n`;
}

fs.mkdirSync(outputDir, { recursive: true });
const frontendMarkdown = readIfExists(frontendLicensePath) || '';
const manifest = {
	generatedAt: new Date().toISOString(),
	noticePath: 'licenses/THIRD_PARTY_NOTICES.txt',
	entries: [...parseFrontendLicenses(frontendMarkdown), ...enrichServerPackages(collectServerPackages())]
};
fs.writeFileSync(path.join(outputDir, 'third-party-licenses.json'), JSON.stringify(manifest, null, 4));
fs.writeFileSync(path.join(outputDir, 'THIRD_PARTY_NOTICES.txt'), noticeText(frontendMarkdown, manifest.entries));
const review = manifest.entries.filter((e) => e.needsReview).length;
console.log(`Generated third-party license manifest with ${manifest.entries.length} entries (${review} requiring review).`);
