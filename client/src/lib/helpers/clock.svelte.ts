import { createSubscriber } from 'svelte/reactivity';

// A shared "now" for elapsed-time text. One interval per tick length, running only while something
// on screen reads it, instead of a setInterval in every component.

type Clock = { readonly now: number };
const clocks = new Map<number, Clock>();

/** `clock(1000).now` is the current time, re-read every `ms` by whatever displays it. */
export function clock(ms = 1000): Clock {
	let found = clocks.get(ms);
	if (found) return found;
	let now = Date.now();
	const subscribe = createSubscriber((update) => {
		now = Date.now();
		const timer = setInterval(() => {
			now = Date.now();
			update();
		}, ms);
		return () => clearInterval(timer);
	});
	found = {
		get now() {
			if (!$effect.tracking()) return Date.now();
			subscribe();
			return now;
		}
	};
	clocks.set(ms, found);
	return found;
}
