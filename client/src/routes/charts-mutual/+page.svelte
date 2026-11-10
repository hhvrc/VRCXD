<script lang="ts">
	import { readSetting, storeSetting } from '#lib/settings/settings-storage.js';
	import { m } from '#lib/paraglide/messages.js';
	import { userDialog } from '#lib/people/user-dialog.svelte.js';
	import { onMount, tick, untrack } from 'svelte';
	import Graph from 'graphology';
	import louvain from 'graphology-communities-louvain';
	import type Sigma from 'sigma';
	import Check from '@lucide/svelte/icons/check';
	import ChevronsUpDown from '@lucide/svelte/icons/chevrons-up-down';
	import EyeOff from '@lucide/svelte/icons/eye-off';
	import RefreshCw from '@lucide/svelte/icons/refresh-cw';
	import Settings from '@lucide/svelte/icons/settings';
	import UserIcon from '@lucide/svelte/icons/user';
	import { Button } from '#lib/components/ui/button/index.js';
	import * as Card from '#lib/components/ui/card/index.js';
	import * as ContextMenu from '#lib/components/ui/context-menu/index.js';
	import * as Dialog from '#lib/components/ui/dialog/index.js';
	import * as Empty from '#lib/components/ui/empty/index.js';
	import { Input } from '#lib/components/ui/input/index.js';
	import { Label } from '#lib/components/ui/label/index.js';
	import * as Popover from '#lib/components/ui/popover/index.js';
	import { Progress } from '#lib/components/ui/progress/index.js';
	import * as Sheet from '#lib/components/ui/sheet/index.js';
	import { Slider } from '#lib/components/ui/slider/index.js';
	import { Spinner } from '#lib/components/ui/spinner/index.js';
	import * as Tooltip from '#lib/components/ui/tooltip/index.js';
	import { accountPath, api, ApiError } from '#lib/api/client.js';
	import type { MutualGraphFetchStatus, MutualGraphSnapshot, MutualRefreshResult } from '#lib/api/types.js';
	import { isDarkMode } from '#lib/activity/charts.js';
	import { formatDateFilter } from '#lib/helpers/format.js';
	import { vrchatAuth } from '#lib/api/vrchat-auth.svelte.js';

	// The original's Mutual Friend Network: every friend and the friends they share with the
	// account as a force-directed graph (Sigma + graphology, layout in a worker, Louvain
	// communities for colour). The data is fetched by the server on request
	// (POST /1/accounts/{id}/mutuals/fetch) and read back from GET .../mutuals; while a fetch
	// runs the page polls its progress.

	const MAX_LABEL_NAME_LENGTH = 20;
	const COLORS_PALETTE = ['#5470c6', '#91cc75', '#fac858', '#ee6666', '#73c0de', '#3ba272', '#fc8452', '#9a60b4', '#ea7ccc'];
	const LAYOUT_ITERATIONS_MIN = 300;
	const LAYOUT_ITERATIONS_MAX = 1500;
	const LAYOUT_SPACING_MIN = 8;
	const LAYOUT_SPACING_MAX = 240;
	const EDGE_CURVATURE_MIN = 0;
	const EDGE_CURVATURE_MAX = 0.2;
	const COMMUNITY_SEPARATION_MIN = 0;
	const COMMUNITY_SEPARATION_MAX = 3;
	const LAYOUT_DEFAULTS = { layoutIterations: 800, layoutSpacing: 60, edgeCurvature: 0.1, communitySeparation: 0 };
	const KEYS = {
		iterations: 'VRCX_MutualGraphLayoutIterations',
		spacing: 'VRCX_MutualGraphLayoutSpacing',
		curvature: 'VRCX_MutualGraphEdgeCurvature',
		separation: 'VRCX_MutualGraphCommunitySeparation',
		excluded: 'VRCX_MutualGraphExcludedFriends'
	};

	function clamp(value: number, min: number, max: number): number {
		const normalized = Number.isFinite(value) ? value : min;
		return Math.min(max, Math.max(min, normalized));
	}

	type MutualMap = Map<string, string[]>;

	let snapshot = $state<MutualGraphSnapshot | null>(null);
	let fetchStatus = $state<MutualGraphFetchStatus | null>(null);
	let isLoadingSnapshot = $state(false);
	let loadError = $state('');
	let statusMessage = $state('');
	let graphNodeCount = $state(0);
	let hasFetched = $state(false);
	let promptOpen = $state(false);
	let confirmNonFriendId = $state<string | null>(null);
	let isRefreshingNode = $state(false);
	let contextMenuNodeId = $state<string | null>(null);

	let layoutIterations = $state(clamp(readSetting(KEYS.iterations, LAYOUT_DEFAULTS.layoutIterations), LAYOUT_ITERATIONS_MIN, LAYOUT_ITERATIONS_MAX));
	let layoutSpacing = $state(clamp(readSetting(KEYS.spacing, LAYOUT_DEFAULTS.layoutSpacing), LAYOUT_SPACING_MIN, LAYOUT_SPACING_MAX));
	let edgeCurvature = $state(clamp(readSetting(KEYS.curvature, LAYOUT_DEFAULTS.edgeCurvature), EDGE_CURVATURE_MIN, EDGE_CURVATURE_MAX));
	let communitySeparation = $state(clamp(readSetting(KEYS.separation, LAYOUT_DEFAULTS.communitySeparation), COMMUNITY_SEPARATION_MIN, COMMUNITY_SEPARATION_MAX));
	let excludedFriendIds = $state<string[]>(readSetting<string[]>(KEYS.excluded, []));
	let lastLayoutSpacing = untrack(() => layoutSpacing);

	let selectedFriendId = $state<string | null>(null);
	let goToOpen = $state(false);
	let goToSearch = $state('');
	let excludeOpen = $state(false);
	let excludeSearch = $state('');

	let graphContainer = $state<HTMLDivElement | null>(null);
	let sigmaInstance: Sigma | null = null;
	let currentGraph: Graph | null = null;
	let hoveredNode: string | null = null;
	let hoveredNeighbors = new Set<string>();
	let lastMutualMap: MutualMap | null = null;
	let layoutWorker: Worker | null = null;
	let layoutRequestId = 0;
	const layoutResolvers = new Map<number, { resolve: (p: Record<string, { x: number; y: number }>) => void; reject: (e: Error) => void }>();
	let layoutQueue: Promise<unknown> = Promise.resolve();
	let pendingLayoutUpdate: ReturnType<typeof setTimeout> | null = null;
	let pollTimer: ReturnType<typeof setTimeout> | null = null;

	const isFetching = $derived(fetchStatus?.running ?? false);
	const totalFriends = $derived(snapshot ? Object.keys(snapshot.friends).length : 0);
	const isOptOut = $derived(snapshot?.optedOut ?? false);
	const graphReady = $derived(graphNodeCount > 0);
	const fetchButtonDisabled = $derived(isFetching || isOptOut || totalFriends === 0 || isLoadingSnapshot);
	const fetchButtonLabel = $derived(hasFetched ? 'Fetch Again' : 'Start Fetch');

	const progressPercent = $derived(fetchStatus && fetchStatus.total
		? Math.min(100, Math.round(fetchStatus.processed / fetchStatus.total * 100))
		: 0);

	const pickerItems = $derived.by(() => {
		if (!lastMutualMap || !snapshot) return [] as { value: string; label: string }[];
		const me = vrchatAuth.ownerUserId;
		const seen = new Set<string>();
		const items: { value: string; label: string }[] = [];
		for (const [friendId, mutuals] of lastMutualMap.entries()) {
			if (friendId === me || seen.has(friendId)) continue;
			seen.add(friendId);
			items.push({ value: friendId, label: snapshot.names[friendId] || friendId });
			for (const id of mutuals) {
				if (!id || id === me || seen.has(id)) continue;
				seen.add(id);
				items.push({ value: id, label: snapshot.names[id] || id });
			}
		}
		items.sort((a, b) => a.label.localeCompare(b.label));
		return items;
	});
	const goToItems = $derived(pickerItems.filter((i) => i.label.toLowerCase().includes(goToSearch.toLowerCase())).slice(0, 200));
	const excludeItems = $derived(pickerItems.filter((i) => i.label.toLowerCase().includes(excludeSearch.toLowerCase())).slice(0, 200));

	function nameOf(id: string): string {
		return snapshot?.names[id] || id;
	}

	function truncateLabel(text: string): string {
		if (!text) return 'Unknown';
		return text.length > MAX_LABEL_NAME_LENGTH ? `${text.slice(0, MAX_LABEL_NAME_LENGTH)}…` : text;
	}

	// ---- layout worker ---------------------------------------------------------------------
	function getLayoutWorker(): Worker {
		if (!layoutWorker) {
			layoutWorker = new Worker(new URL('../../lib/activity/graph-layout.worker.ts', import.meta.url), { type: 'module' });
			layoutWorker.addEventListener('message', (event: MessageEvent) => {
				const { requestId, positions, error } = event.data;
				const resolver = layoutResolvers.get(requestId);
				if (!resolver) return;
				layoutResolvers.delete(requestId);
				if (error) resolver.reject(new Error(error));
				else resolver.resolve(positions);
			});
			layoutWorker.addEventListener('error', (err) => {
				for (const [id, resolver] of layoutResolvers) {
					resolver.reject(new Error(err.message));
					layoutResolvers.delete(id);
				}
			});
		}
		return layoutWorker;
	}

	function serializeGraph(graph: Graph) {
		const nodes: { id: string; attributes: Record<string, unknown> }[] = [];
		graph.forEachNode((id, attributes) => nodes.push({ id, attributes: { ...attributes } }));
		const edges: { key: string; source: string; target: string; attributes: Record<string, unknown> }[] = [];
		graph.forEachEdge((key, attributes, source, target) => edges.push({ key, source, target, attributes: { ...attributes } }));
		return { nodes, edges };
	}

	async function runLayout(graph: Graph, { reinitialize = false } = {}) {
		const spacing = clamp(layoutSpacing, LAYOUT_SPACING_MIN, LAYOUT_SPACING_MAX);
		const deltaSpacing = spacing - lastLayoutSpacing;
		lastLayoutSpacing = spacing;
		const { nodes, edges } = serializeGraph(graph);
		const worker = getLayoutWorker();
		const id = ++layoutRequestId;
		const task = layoutQueue.then(async () => {
			const positions = await new Promise<Record<string, { x: number; y: number }>>((resolve, reject) => {
				layoutResolvers.set(id, { resolve, reject });
				worker.postMessage({ requestId: id, nodes, edges, settings: { layoutIterations, layoutSpacing: spacing, deltaSpacing, reinitialize } });
			});
			for (const [nodeId, pos] of Object.entries(positions)) {
				if (graph.hasNode(nodeId)) graph.mergeNodeAttributes(nodeId, { x: pos.x, y: pos.y });
			}
		});
		layoutQueue = task.catch(() => {});
		return task;
	}

	function applyEdgeCurvature(graph: Graph) {
		const curvature = clamp(edgeCurvature, EDGE_CURVATURE_MIN, EDGE_CURVATURE_MAX);
		graph.forEachEdge((edge) => graph.mergeEdgeAttributes(edge, { curvature }));
	}

	function applyCommunitySeparation(graph: Graph) {
		const separation = communitySeparation;
		if (separation <= 0) return;
		const communities = new Map<unknown, { nodes: { node: string; x: number; y: number }[]; cx: number; cy: number }>();
		graph.forEachNode((node, attrs) => {
			const cid = attrs.community;
			if (cid === undefined) return;
			if (!communities.has(cid)) communities.set(cid, { nodes: [], cx: 0, cy: 0 });
			(communities.get(cid)!).nodes.push({ node, x: attrs.x, y: attrs.y });
		});
		for (const data of communities.values()) {
			let sx = 0;
			let sy = 0;
			for (const n of data.nodes) (sx += n.x, sy += n.y);
			data.cx = sx / data.nodes.length;
			data.cy = sy / data.nodes.length;
		}
		let gcx = 0;
		let gcy = 0;
		let total = 0;
		for (const data of communities.values()) {
			gcx += data.cx * data.nodes.length;
			gcy += data.cy * data.nodes.length;
			total += data.nodes.length;
		}
		gcx /= total;
		gcy /= total;
		for (const data of communities.values()) {
			const dx = data.cx - gcx;
			const dy = data.cy - gcy;
			const dist = Math.sqrt(dx * dx + dy * dy) || 1;
			const pushX = dx / dist * separation * 100;
			const pushY = dy / dist * separation * 100;
			for (const n of data.nodes) graph.mergeNodeAttributes(n.node, { x: n.x + pushX, y: n.y + pushY });
		}
	}

	function assignCommunitiesAndColors(graph: Graph) {
		const communities = louvain(graph) as Record<string, number>;
		const ids = Array.from(new Set(Object.values(communities)));
		ids.sort((a, b) => String(a).localeCompare(String(b)));
		const idToIndex = new Map(ids.map((id, i) => [id, i]));
		graph.forEachNode((node) => {
			const communityId = communities[node];
			const idx = idToIndex.get(communityId) ?? 0;
			graph.setNodeAttribute(node, 'community', communityId);
			graph.setNodeAttribute(node, 'color', COLORS_PALETTE[idx % COLORS_PALETTE.length]);
		});
	}

	function scheduleLayoutUpdate(shouldRunLayout: boolean) {
		if (!currentGraph) return;
		if (pendingLayoutUpdate) clearTimeout(pendingLayoutUpdate);
		pendingLayoutUpdate = setTimeout(async () => {
				pendingLayoutUpdate = null;
				if (!currentGraph) return;
				try {
					applyEdgeCurvature(currentGraph);
					if (shouldRunLayout) {
						await runLayout(currentGraph, { reinitialize: false });
						applyCommunitySeparation(currentGraph);
					}
					renderGraph(currentGraph);
			} catch (err) {
					console.error('[MutualNetworkGraph] Layout update failed', err);
				}
		}, 100);
	}

	// ---- graph build and render ------------------------------------------------------------
	async function buildGraph(mutualMap: MutualMap): Promise<Graph> {
		const graph = new Graph({ type: 'undirected', multi: false, allowSelfLoops: false });
		const excludeSet = new Set(excludedFriendIds);
		const nodeDegree = new Map<string, number>();
		const ensureNode = (id: string) => {
			if (!id || excludeSet.has(id)) return;
			if (!graph.hasNode(id)) {
				graph.addNode(id);
				nodeDegree.set(id, 0);
			}
		};
		const addEdge = (source: string, target: string) => {
			if (!source || !target || source === target) return;
			if (excludeSet.has(source) || excludeSet.has(target)) return;
			const [a, b] = [source, target].sort();
			const key = `${a}__${b}`;
			if (graph.hasEdge(key)) return;
			graph.addEdgeWithKey(key, a, b, { size: 0.75 });
			nodeDegree.set(a, (nodeDegree.get(a) || 0) + 1);
			nodeDegree.set(b, (nodeDegree.get(b) || 0) + 1);
		};
		for (const [friendId, mutuals] of mutualMap.entries()) {
			ensureNode(friendId);
			for (const id of mutuals) {
				ensureNode(id);
				addEdge(friendId, id);
			}
		}
		const nodeIds = graph.nodes();
		const maxDegree = nodeIds.reduce((max, id) => Math.max(max, nodeDegree.get(id) || 0), 0);
		for (const id of nodeIds) {
			const degree = nodeDegree.get(id) || 0;

			const attrs: Record<string, unknown> = {
				label: truncateLabel(nameOf(id)),
				size: 4 + (maxDegree ? degree / maxDegree * 18 : 0)
			};

			const m = snapshot?.meta[id];
			if (m) {
				attrs.optedOut = m.optedOut;
				attrs.lastFetchedAt = m.lastFetchedAt;
			}
			graph.mergeNodeAttributes(id, attrs);
		}
		if (graph.order > 1) {
			await runLayout(graph, { reinitialize: true });
			assignCommunitiesAndColors(graph);
			applyCommunitySeparation(graph);
			applyEdgeCurvature(graph);
		}
		graphNodeCount = graph.order;
		return graph;
	}

	function renderLastFetchedAttachment({ attributes, pixelRatio }: { attributes: Record<string, unknown>; pixelRatio: number }) {
		if (!attributes.lastFetchedAt) return null;
		const text = `Last fetched: ${formatDateFilter(String(attributes.lastFetchedAt), 'long')}`;
		const fontSize = 10;
		const font = `${fontSize}px sans-serif`;
		const paddingX = 6;
		const paddingY = 3;
		const canvas = document.createElement('canvas');
		const ctx = canvas.getContext('2d')!;
		ctx.font = font;
		const width = Math.ceil(ctx.measureText(text).width) + paddingX * 2;
		const height = fontSize + paddingY * 2;
		canvas.width = width * pixelRatio;
		canvas.height = height * pixelRatio;
		ctx.scale(pixelRatio, pixelRatio);
		ctx.fillStyle = '#ffffff';
		ctx.fillRect(0, 0, width, height);
		ctx.font = font;
		ctx.textBaseline = 'middle';
		ctx.fillStyle = attributes.optedOut ? '#dc2626' : '#6b7280';
		ctx.fillText(text, paddingX, height / 2);
		return { type: 'canvas', canvas } as const;
	}

	// Sigma touches WebGL at import time, so it is loaded in the browser only (the routes are prerendered).
	async function renderGraph(graph: Graph, forceRecreate = false) {
		if (!graphContainer) return;
		const container = graphContainer;
		const { width, height } = container.getBoundingClientRect();
		if (!width || !height) {
			requestAnimationFrame(() => renderGraph(graph, forceRecreate));
			return;
		}
		const [{ default: SigmaClass }, { pathCurved }, { layerBorder }] = await Promise.all([import('sigma'), import('sigma/rendering'), import('@sigma/node-border')]);
		if (!graphContainer) return;
		const dark = isDarkMode();
		const labelColor = dark ? '#ffffff' : '#000000';
		const EDGE_BASE = dark ? '#334155' : '#cbd5e1';
		const EDGE_ACTIVE = dark ? '#bac1c9' : '#475569';
		const NODE_FADED = dark ? 'rgba(148,163,184,0.04)' : 'rgba(100,116,139,0.06)';
		const LABEL_BACKGROUND = dark ? 'rgba(10,10,10,0.65)' : 'rgba(255,255,255,0.7)';

		let cameraState: unknown = null;
		if (sigmaInstance && forceRecreate) {
			try {
				cameraState = sigmaInstance.getCamera().getState();
			} catch {
				// keep default
			}
			sigmaInstance.kill();
			sigmaInstance = null;
		}
		hoveredNode = null;
		hoveredNeighbors = new Set();
		if (sigmaInstance) {
			sigmaInstance.setGraph(graph);
			sigmaInstance.refresh();
			return;
		}

		// eslint-disable-next-line @typescript-eslint/no-explicit-any
		const SigmaCtor = SigmaClass as any;
		const instance = new SigmaCtor(graph, container, {
			primitives: {
				nodes: {
					layers: [layerBorder({ borders: [{ size: 0.1, color: '#f2f2f2', mode: 'relative' }, { size: 0, color: { attribute: 'color' }, fill: true }] })],
					label: {},
					backdrop: {},
					labelAttachments: { lastFetched: renderLastFetchedAttachment }
				},
				edges: { paths: [pathCurved()] }
			},
			settings: { allowInvalidContainer: true, renderLabels: true, itemSizesReference: 'screen', labelRenderedSizeThreshold: 10 },
			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			nodeReducer: (node: string, data: any, attrs: any) => {
				// eslint-disable-next-line @typescript-eslint/no-explicit-any
				const res: any = { labelColor, labelBackgroundColor: LABEL_BACKGROUND, labelBackgroundPadding: 2 };
				if (attrs.optedOut) res.borderColor = '#9ca3af';
				if (!hoveredNode) {
					res.color = attrs.optedOut ? '#d1d5db' : data.color;
					res.zIndex = 1;
					return res;
				}
				if (node === hoveredNode) {
					res.color = '#facc15';
					res.size = (attrs.size || 4) * 1.2;
					res.label = `${data.label} (${hoveredNeighbors.size})`;
					res.labelColor = '#111827';
					res.labelBackgroundColor = null;
					res.labelVisibility = 'visible';
					res.labelAttachment = attrs.lastFetchedAt ? 'lastFetched' : null;
					res.zIndex = 3;
					return res;
				}
				if (hoveredNeighbors.has(node)) {
					res.labelVisibility = 'visible';
					res.zIndex = 2;
					return res;
				}
				res.color = NODE_FADED;
				res.size = 0.7;
				res.labelVisibility = 'hidden';
				res.zIndex = 0;
				return res;
			},
			// eslint-disable-next-line @typescript-eslint/no-explicit-any
			edgeReducer: (edge: string, _data: any, _attrs: any, _state: any, _graphState: any, g: Graph) => {
				if (!hoveredNode) return { color: EDGE_BASE };
				const [s, t] = g.extremities(edge);
				if (s === hoveredNode || t === hoveredNode) return { color: EDGE_ACTIVE };
				return { visibility: 'hidden' };
			}
		});
		sigmaInstance = instance as Sigma;
		if (cameraState) {
			try {
				// eslint-disable-next-line @typescript-eslint/no-explicit-any
				sigmaInstance.getCamera().setState(cameraState as any);
			} catch {
				// keep default
			}
		}
		instance.on('beforeRender', () => {
			if (hoveredNode && instance.labelRenderer) instance.labelRenderer.labelsDirty = true;
		});
		instance.on('enterNode', ({ node }: { node: string }) => {
			hoveredNode = node;
			hoveredNeighbors = new Set(instance.getGraph().neighbors(node));
			instance.refresh();
		});
		instance.on('leaveNode', () => {
			hoveredNode = null;
			hoveredNeighbors = new Set();
			instance.refresh();
		});
		instance.on('clickNode', ({ node }: { node: string }) => {
			if (node) userDialog.open(node);
		});
		instance.on('rightClickNode', ({ node }: { node: string }) => {
			contextMenuNodeId = node || null;
		});
		instance.on('rightClickStage', () => {
			contextMenuNodeId = null;
		});
	}

	async function applyGraph(mutualMap: MutualMap) {
		lastMutualMap = mutualMap;
		const graph = await buildGraph(mutualMap);
		currentGraph = graph;
		await renderGraph(graph);
	}

	function navigateToFriend(friendId: string | null) {
		selectedFriendId = friendId;
		goToOpen = false;
		if (!friendId || !currentGraph || !sigmaInstance || !currentGraph.hasNode(friendId)) return;
		const display = sigmaInstance.getNodeDisplayData(friendId);
		if (!display) return;
		sigmaInstance.getCamera().animate({ x: display.x, y: display.y, ratio: 0.15 }, { duration: 300 });
	}

	// ---- data ------------------------------------------------------------------------------
	function toMutualMap(s: MutualGraphSnapshot): MutualMap {
		return new Map(Object.entries(s.friends));
	}

	async function loadSnapshot({ prompt = true } = {}) {
		const accountId = vrchatAuth.ownerUserId;
		if (!accountId || isLoadingSnapshot) return;
		isLoadingSnapshot = true;
		loadError = '';
		try {
			snapshot = await api.get<MutualGraphSnapshot>(accountPath(accountId, '/mutuals'));
			fetchStatus = snapshot.fetch;
			hasFetched = !!snapshot.fetch.lastCompletedAt || Object.keys(snapshot.meta).length > 0;
			const hasLinks = Object.values(snapshot.friends).some((m) => m.length > 0);
			if (!hasLinks) {
				graphNodeCount = 0;
				if (totalFriends === 0) statusMessage = 'You have no friends to process';
				else if (isOptOut) statusMessage = 'Mutual friends sharing is currently disabled for this account; enable it in VRChat to fetch the graph.';
				else if (prompt && !isFetching && !hasFetched) promptOpen = true;
			} else {
				await applyGraph(toMutualMap(snapshot));
			}
			if (snapshot.fetch.running) startPolling();
		} catch (err) {
			loadError = ApiError.from(err).message;
		} finally {
			isLoadingSnapshot = false;
		}
	}

	function startPolling() {
		if (pollTimer) return;
		const poll = async () => {
			const accountId = vrchatAuth.ownerUserId;
			if (!accountId) return;
			try {
				fetchStatus = await api.get<MutualGraphFetchStatus>(accountPath(accountId, '/mutuals/fetch'));
			} catch {
				// try again next tick
			}
			if (fetchStatus?.running) {
				pollTimer = setTimeout(poll, 1500);
			} else {
				pollTimer = null;
				statusMessage = fetchStatus?.cancelRequested ? 'Fetch cancelled' : 'Mutual friend network graph is ready';
				await loadSnapshot({ prompt: false });
			}
		};
		pollTimer = setTimeout(poll, 1500);
	}

	async function startFetch() {
		const accountId = vrchatAuth.ownerUserId;
		if (!accountId || isFetching || isOptOut) return;
		promptOpen = false;
		try {
			await api.post(accountPath(accountId, '/mutuals/fetch'));
			statusMessage = 'Started fetching';
			fetchStatus = { running: true, cancelRequested: false, processed: 0, total: totalFriends, lastCompletedAt: fetchStatus?.lastCompletedAt ?? null };
			startPolling();
		} catch (err) {
			statusMessage = ApiError.from(err).message;
		}
	}

	async function cancelFetch() {
		const accountId = vrchatAuth.ownerUserId;
		if (!accountId) return;
		try {
			await api.delete(accountPath(accountId, '/mutuals/fetch'));
			if (fetchStatus) fetchStatus = { ...fetchStatus, cancelRequested: true };
		} catch (err) {
			statusMessage = ApiError.from(err).message;
		}
	}

	async function refreshNode(nodeId: string) {
		const accountId = vrchatAuth.ownerUserId;
		if (!accountId || isRefreshingNode) return;
		isRefreshingNode = true;
		try {
			const result = await api.post<MutualRefreshResult>(accountPath(accountId, `/mutuals/${encodeURIComponent(nodeId)}/refresh`));
			if (result.optedOut) statusMessage = 'This user has disabled mutual friends sharing. Existing data has been preserved.';
			else if (result.success) statusMessage = `Mutual friends data updated for ${nameOf(nodeId)}`;
			else statusMessage = 'Failed to refresh mutual friends data';
			await loadSnapshot({ prompt: false });
		} catch (err) {
			statusMessage = `Failed to refresh mutual friends data: ${ApiError.from(err).message}`;
		} finally {
			isRefreshingNode = false;
		}
	}

	function handleNodeMenuRefresh() {
		const nodeId = contextMenuNodeId;
		contextMenuNodeId = null;
		if (!nodeId) return;
		if (snapshot && !(nodeId in snapshot.friends)) {
			confirmNonFriendId = nodeId;
			return;
		}
		refreshNode(nodeId);
	}

	function handleNodeMenuHide() {
		if (contextMenuNodeId && !excludedFriendIds.includes(contextMenuNodeId)) excludedFriendIds = [...excludedFriendIds, contextMenuNodeId];
		contextMenuNodeId = null;
	}

	function toggleExcluded(id: string) {
		excludedFriendIds = excludedFriendIds.includes(id) ? excludedFriendIds.filter((x) => x !== id) : [...excludedFriendIds, id];
	}

	function resetLayoutSettings() {
		layoutIterations = LAYOUT_DEFAULTS.layoutIterations;
		layoutSpacing = LAYOUT_DEFAULTS.layoutSpacing;
		edgeCurvature = LAYOUT_DEFAULTS.edgeCurvature;
		communitySeparation = LAYOUT_DEFAULTS.communitySeparation;
		excludedFriendIds = [];
	}

	// Persist and re-layout on setting changes (the original's watchers).
	let settingsReady = false;
	$effect(() => {
		void layoutIterations;
		void layoutSpacing;
		if (!settingsReady) return;
		storeSetting(KEYS.iterations, layoutIterations);
		storeSetting(KEYS.spacing, layoutSpacing);
		untrack(() => scheduleLayoutUpdate(true));
	});
	$effect(() => {
		void edgeCurvature;
		if (!settingsReady) return;
		storeSetting(KEYS.curvature, edgeCurvature);
		untrack(() => scheduleLayoutUpdate(false));
	});
	$effect(() => {
		void communitySeparation;
		if (!settingsReady) return;
		storeSetting(KEYS.separation, communitySeparation);
		untrack(() => scheduleLayoutUpdate(true));
	});
	$effect(() => {
		void excludedFriendIds;
		if (!settingsReady) return;
		storeSetting(KEYS.excluded, excludedFriendIds);
		untrack(() => {
			if (lastMutualMap) applyGraph(lastMutualMap).catch((err) => console.error('[MutualNetworkGraph] Failed to apply graph after exclude change', err));
		});
	});

	onMount(() => {
		tick().then(() => settingsReady = true);
		const observer = new ResizeObserver(() => sigmaInstance?.refresh());
		if (graphContainer) observer.observe(graphContainer);
		return () => {
			observer.disconnect();
			if (pollTimer) clearTimeout(pollTimer);
			if (pendingLayoutUpdate) clearTimeout(pendingLayoutUpdate);
			sigmaInstance?.kill();
			sigmaInstance = null;
			currentGraph = null;
			if (layoutWorker) {
				for (const [id, resolver] of layoutResolvers) {
					resolver.reject(new Error('Component unmounted'));
					layoutResolvers.delete(id);
				}
				layoutWorker.terminate();
				layoutWorker = null;
			}
		};
	});

	$effect(() => {
		if (!vrchatAuth.ownerUserId) return;
		untrack(() => loadSnapshot());
	});
</script>

<svelte:head>
	<title>Mutual Friend Network · VRCXD</title>
</svelte:head>

<Tooltip.Provider>
	{#if !vrchatAuth.ownerUserId}
		<Card.Root>
			<Card.Header>
				<Card.Description>Log in to VRChat to see the mutual friend network.</Card.Description>
			</Card.Header>
		</Card.Root>
	{:else}
		<div class="mt-0 flex min-h-[calc(100vh-140px)] flex-col items-center">
			<div class="flex w-full items-center">
				<div class="flex items-center gap-3 pb-3">
					<div>
						{#if isFetching}
							<Button variant="destructive" disabled={fetchStatus?.cancelRequested} title={m.view_charts_mutual_friend_actions_stop_fetching()} onclick={cancelFetch}>
								<Spinner />
								{m.view_charts_mutual_friend_actions_stop()}
							</Button>
						{:else}
							<Button disabled={fetchButtonDisabled} title={fetchButtonLabel} onclick={startFetch}>{fetchButtonLabel}</Button>
						{/if}
					</div>
					{#if graphReady}
						<Popover.Root bind:open={goToOpen}>
							<Popover.Trigger>
								{#snippet child({ props })}
									<Button {...props} variant="outline" class="min-w-60 justify-between font-normal" role="combobox">
										<span class="truncate">{selectedFriendId ? nameOf(selectedFriendId) : 'Go to friend'}</span>
										<ChevronsUpDown class="ml-2 size-4 shrink-0 opacity-50" />
									</Button>
								{/snippet}
							</Popover.Trigger>
							<Popover.Content class="w-72 p-2" align="start">
								<Input placeholder={m.view_charts_mutual_friend_actions_go_to_friend()} bind:value={goToSearch} class="mb-2 h-8" />
								<div class="max-h-72 overflow-y-auto">
									{#each goToItems as item (item.value)}
										<button type="button" class="flex w-full items-center rounded-md p-1.5 text-left text-compact hover:bg-accent" onclick={() => navigateToFriend(item.value === selectedFriendId ? null : item.value)}>
											<span class="block flex-1 truncate font-medium leading-4.5">{item.label}</span>
											<Check class="ml-auto size-4 {item.value === selectedFriendId ? 'opacity-100' : 'opacity-0'}" />
										</button>
									{/each}
								</div>
							</Popover.Content>
						</Popover.Root>
					{/if}
				</div>
				<div class="ml-auto flex items-center gap-2">
					<Sheet.Root>
						<Sheet.Trigger>
							{#snippet child({ props })}
								<Button {...props} class="rounded-full" size="icon" variant="ghost" title={m.view_charts_mutual_friend_settings_title()} aria-label={m.view_charts_mutual_friend_settings_title()}>
									<Settings />
								</Button>
							{/snippet}
						</Sheet.Trigger>
						<Sheet.Content side="right" class="w-90 overflow-y-auto">
							<Sheet.Header>
								<Sheet.Title>{m.view_charts_mutual_friend_settings_title()}</Sheet.Title>
							</Sheet.Header>
							<div class="mt-4 flex flex-col gap-4 p-4">
								<div>
									<Label>{m.view_charts_mutual_friend_settings_layout_iterations()}</Label>
									<div class="mt-2 flex items-center gap-3">
										<Slider type="single" bind:value={layoutIterations} min={LAYOUT_ITERATIONS_MIN} max={LAYOUT_ITERATIONS_MAX} step={100} />
										<span class="min-w-12 text-right text-sm text-muted-foreground tabular-nums">{layoutIterations}</span>
									</div>
									<p class="mt-1 text-xs text-muted-foreground">{m.view_charts_mutual_friend_settings_layout_iterations_help()}</p>
								</div>
								<div>
									<Label>{m.view_charts_mutual_friend_settings_layout_spacing()}</Label>
									<div class="mt-2 flex items-center gap-3">
										<Slider type="single" bind:value={layoutSpacing} min={LAYOUT_SPACING_MIN} max={LAYOUT_SPACING_MAX} step={1} />
										<span class="min-w-12 text-right text-sm text-muted-foreground tabular-nums">{layoutSpacing}</span>
									</div>
									<p class="mt-1 text-xs text-muted-foreground">{m.view_charts_mutual_friend_settings_layout_spacing_help()}</p>
								</div>
								<div>
									<Label>{m.view_charts_mutual_friend_settings_edge_curvature()}</Label>
									<div class="mt-2 flex items-center gap-3">
										<Slider type="single" bind:value={edgeCurvature} min={EDGE_CURVATURE_MIN} max={EDGE_CURVATURE_MAX} step={0.01} />
										<span class="min-w-12 text-right text-sm text-muted-foreground tabular-nums">{edgeCurvature.toFixed(2)}</span>
									</div>
									<p class="mt-1 text-xs text-muted-foreground">{m.view_charts_mutual_friend_settings_edge_curvature_help()}</p>
								</div>
								<div>
									<Label>{m.view_charts_mutual_friend_settings_community_separation()}</Label>
									<div class="mt-2 flex items-center gap-3">
										<Slider type="single" bind:value={communitySeparation} min={COMMUNITY_SEPARATION_MIN} max={COMMUNITY_SEPARATION_MAX} step={0.1} />
										<span class="min-w-12 text-right text-sm text-muted-foreground tabular-nums">{communitySeparation.toFixed(1)}</span>
									</div>
									<p class="mt-1 text-xs text-muted-foreground">{m.view_charts_mutual_friend_settings_community_separation_help()}</p>
								</div>
							</div>
							<div class="flex flex-col gap-4 p-4">
								<div>
									<Label>{m.view_charts_mutual_friend_settings_exclude_friends()}</Label>
									<Popover.Root bind:open={excludeOpen}>
										<Popover.Trigger>
											{#snippet child({ props })}
												<Button {...props} variant="outline" class="mt-2 w-full justify-between font-normal" role="combobox">
													<span class="truncate">{excludedFriendIds.length ? excludedFriendIds.map(nameOf).join(', ') : 'Select friends to exclude'}</span>
													<ChevronsUpDown class="ml-2 size-4 shrink-0 opacity-50" />
												</Button>
											{/snippet}
										</Popover.Trigger>
										<Popover.Content class="w-72 p-2" align="start">
											<Input placeholder={m.view_charts_mutual_friend_actions_go_to_friend()} bind:value={excludeSearch} class="mb-2 h-8" />
											<div class="max-h-72 overflow-y-auto">
												{#each excludeItems as item (item.value)}
													<button type="button" class="flex w-full items-center rounded-md p-1.5 text-left text-compact hover:bg-accent" onclick={() => toggleExcluded(item.value)}>
														<span class="block flex-1 truncate font-medium leading-4.5">{item.label}</span>
														<Check class="ml-auto size-4 {excludedFriendIds.includes(item.value) ? 'opacity-100' : 'opacity-0'}" />
													</button>
												{/each}
											</div>
										</Popover.Content>
									</Popover.Root>
									<p class="mt-1 text-xs text-muted-foreground">{m.view_charts_mutual_friend_settings_exclude_friends_help()}</p>
								</div>
							</div>
							<div class="p-4 pt-0">
								<Button variant="outline" size="sm" class="w-full" onclick={resetLayoutSettings}>{m.view_charts_mutual_friend_settings_reset_defaults()}</Button>
							</div>
						</Sheet.Content>
					</Sheet.Root>

					{#if isFetching && fetchStatus}
						<div class="grid w-70 grid-cols-[repeat(auto-fit,minmax(150px,1fr))] items-center rounded-md bg-transparent p-3">
							<div class="mb-1 flex justify-between text-sm">
								<span class="mr-1">{m.view_charts_mutual_friend_progress_friends_processed()}</span>
								<strong>{fetchStatus.processed} / {fetchStatus.total}</strong>
							</div>
							<Progress value={progressPercent} class="h-3" />
						</div>
					{/if}
				</div>
			</div>

			{#if statusMessage}
				<p class="w-full text-xs text-muted-foreground">{statusMessage}</p>
			{/if}
			{#if loadError}
				<p class="w-full text-sm text-destructive">Couldn't load the mutual graph: {loadError}</p>
			{/if}

			<ContextMenu.Root onOpenChange={(open) => !open && (contextMenuNodeId = null)}>
				<ContextMenu.Trigger>
					{#snippet child({ props })}
						<div
							{...props}
							bind:this={graphContainer}
							class="mt-3 h-[calc(100vh-260px)] min-h-[520px] w-full flex-1 rounded-lg bg-transparent"
							class:hidden={hasFetched && !isFetching && !graphReady}
						></div>
					{/snippet}
				</ContextMenu.Trigger>
				{#if contextMenuNodeId}
					<ContextMenu.Content class="min-w-40">
						<ContextMenu.Item
							onSelect={() => {
								if (contextMenuNodeId) userDialog.open(contextMenuNodeId);
								contextMenuNodeId = null;
							}}
						>
							<UserIcon class="size-4" />
							{m.dialog_avatar_actions_view_details()}
						</ContextMenu.Item>
						<ContextMenu.Separator />
						<ContextMenu.Item onSelect={handleNodeMenuRefresh}>
							<RefreshCw class="size-4" />
							{m.view_charts_mutual_friend_context_menu_refresh_mutuals()}
						</ContextMenu.Item>
						<ContextMenu.Separator />
						<ContextMenu.Item onSelect={handleNodeMenuHide}>
							<EyeOff class="size-4" />
							{m.view_charts_mutual_friend_context_menu_hide_friend()}
						</ContextMenu.Item>
					</ContextMenu.Content>
				{/if}
			</ContextMenu.Root>

			{#if hasFetched && !isFetching && !graphReady}
				<Empty.Root class="mt-3 w-full flex-1">
					<Empty.Header>
						<Empty.Description>{m.view_charts_mutual_friend_progress_no_relationships_discovered()}</Empty.Description>
					</Empty.Header>
				</Empty.Root>
			{/if}
		</div>

		<Dialog.Root bind:open={promptOpen}>
			<Dialog.Content>
				<Dialog.Header>
					<Dialog.Title>{m.view_charts_mutual_friend_tab_label()}</Dialog.Title>
					<Dialog.Description>{m.view_charts_mutual_friend_prompt_message()}</Dialog.Description>
				</Dialog.Header>
				<Dialog.Footer>
					<Button
						variant="outline"
						onclick={() => promptOpen = false}
					>{m.view_charts_mutual_friend_prompt_cancel()}</Button>

					<Button onclick={startFetch}>{m.view_charts_mutual_friend_prompt_confirm()}</Button>
				</Dialog.Footer>
			</Dialog.Content>
		</Dialog.Root>

		<Dialog.Root open={confirmNonFriendId !== null} onOpenChange={(open) => !open && (confirmNonFriendId = null)}>
			<Dialog.Content>
				<Dialog.Header>
					<Dialog.Title>{m.view_charts_mutual_friend_context_menu_confirm_non_friend_title()}</Dialog.Title>
					<Dialog.Description>{m.view_charts_mutual_friend_context_menu_confirm_non_friend_message()}</Dialog.Description>
				</Dialog.Header>
				<Dialog.Footer>
					<Button
						variant="outline"
						onclick={() => confirmNonFriendId = null}
					>{m.view_login_qr_cancel()}</Button>

					<Button
						onclick={() => {
							const id = confirmNonFriendId;
							confirmNonFriendId = null;
							if (id) refreshNode(id);
						}}>{m.dialog_alertdialog_confirm()}</Button
					>
				</Dialog.Footer>
			</Dialog.Content>
		</Dialog.Root>
	{/if}
</Tooltip.Provider>
