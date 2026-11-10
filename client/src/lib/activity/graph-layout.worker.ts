// The original's graphLayoutWorker.js: ForceAtlas2 then Noverlap, off the main thread.
// Main → worker: { requestId, nodes, edges, settings }; worker → main: { requestId, positions } | { requestId, error }.
import Graph from 'graphology';
import forceAtlas2 from 'graphology-layout-forceatlas2';
import noverlap from 'graphology-layout-noverlap';

type NodeIn = { id: string; attributes: Record<string, unknown> };
type EdgeIn = { key: string; source: string; target: string; attributes: Record<string, unknown> };
type Settings = { layoutIterations: number; layoutSpacing: number; deltaSpacing?: number; reinitialize?: boolean };

const LAYOUT_SPACING_MIN = 8;
const LAYOUT_SPACING_MAX = 240;
const LAYOUT_ITERATIONS_MIN = 300;
const LAYOUT_ITERATIONS_MAX = 1500;

function clamp(value: number, min: number, max: number): number {
	const normalized = Number.isFinite(value) ? value : min;
	return Math.min(max, Math.max(min, normalized));
}

function lerp(a: number, b: number, t: number): number {
	return a + (b - a) * t;
}

function jitter(graph: Graph, magnitude: number) {
	graph.forEachNode((node, attrs) => {
		if (!Number.isFinite(attrs.x) || !Number.isFinite(attrs.y)) return;
		graph.mergeNodeAttributes(node, { x: attrs.x + (Math.random() - 0.5) * magnitude, y: attrs.y + (Math.random() - 0.5) * magnitude });
	});
}

function initPositions(graph: Graph) {
	const radius = Math.max(50, Math.sqrt(graph.order) * 30);
	graph.forEachNode((node) => {
		const a = Math.random() * Math.PI * 2;
		const r = Math.sqrt(Math.random()) * radius;
		graph.mergeNodeAttributes(node, { x: Math.cos(a) * r, y: Math.sin(a) * r });
	});
}

function runLayout(data: { nodes: NodeIn[]; edges: EdgeIn[]; settings: Settings }): Record<string, { x: number; y: number }> {
	const graph = new Graph({ type: 'undirected', multi: false, allowSelfLoops: false });
	for (const node of data.nodes) graph.addNode(node.id, node.attributes);
	for (const edge of data.edges) graph.addEdgeWithKey(edge.key, edge.source, edge.target, edge.attributes);

	if (data.settings.reinitialize) initPositions(graph);

	const iterations = clamp(data.settings.layoutIterations, LAYOUT_ITERATIONS_MIN, LAYOUT_ITERATIONS_MAX);
	const spacing = clamp(data.settings.layoutSpacing, LAYOUT_SPACING_MIN, LAYOUT_SPACING_MAX);
	const t = clamp((spacing - LAYOUT_SPACING_MIN) / (LAYOUT_SPACING_MAX - LAYOUT_SPACING_MIN), 0, 1);
	const deltaSpacing = data.settings.deltaSpacing ?? 0;

	const inferred = forceAtlas2.inferSettings ? forceAtlas2.inferSettings(graph) : {};
	if (Math.abs(deltaSpacing) >= 8) jitter(graph, lerp(0.5, 2.0, t));
	forceAtlas2.assign(graph, {
		iterations,
		settings: { ...inferred, barnesHutOptimize: true, barnesHutTheta: 0.8, strongGravityMode: true, gravity: lerp(1.6, 0.6, t), scalingRatio: spacing, slowDown: 2 }
	});

	noverlap.assign(graph, {
		maxIterations: clamp(Math.round(Math.sqrt(graph.order) * 6), 200, 600),
		settings: { ratio: lerp(1.05, 1.35, t), margin: lerp(1, 8, t) }
	});

	const positions: Record<string, { x: number; y: number }> = {};
	graph.forEachNode((node, attrs) => {
		positions[node] = { x: attrs.x, y: attrs.y };
	});
	return positions;
}

self.addEventListener('message', (event: MessageEvent) => {
	const { requestId } = event.data;
	try {
		self.postMessage({ requestId, positions: runLayout(event.data) });
	} catch (err) {
		self.postMessage({ requestId, error: (err as Error).message });
	}
});
