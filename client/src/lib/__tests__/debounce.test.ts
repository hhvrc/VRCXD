import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest';
import { debounce } from '../helpers/debounce.js';

// The refreshes behind live events: quiet for `wait`, but never held longer than `maxWait`, so a
// steady stream of events (a busy instance) still refreshes.
describe('debounce with a maximum wait', () => {
	beforeEach(() => vi.useFakeTimers());
	afterEach(() => vi.useRealTimers());

	test('runs once, `wait` after the last of a burst', () => {
		const run = vi.fn();
		const call = debounce(run, { wait: 1000, maxWait: 10_000 });
		call();
		vi.advanceTimersByTime(500);
		call();
		vi.advanceTimersByTime(999);
		expect(run).not.toHaveBeenCalled();
		vi.advanceTimersByTime(1);
		expect(run).toHaveBeenCalledTimes(1);
		vi.advanceTimersByTime(20_000);
		expect(run).toHaveBeenCalledTimes(1);
	});

	test('a steady stream still runs every `maxWait`, where a plain debounce never would', () => {
		const run = vi.fn();
		const call = debounce(run, { wait: 1500, maxWait: 10_000 });
		// An event every second for 35 seconds: always inside the quiet time.
		for (let t = 0; t < 35; t++) {
			call();
			vi.advanceTimersByTime(1000);
		}
		expect(run).toHaveBeenCalledTimes(3);
		vi.advanceTimersByTime(1500);
		expect(run).toHaveBeenCalledTimes(4);
	});

	test('maxWait defaults to five times the wait', () => {
		const run = vi.fn();
		const call = debounce(run, { wait: 100 });
		for (let t = 0; t < 10; t++) {
			call();
			vi.advanceTimersByTime(50);
		}
		expect(run).toHaveBeenCalledTimes(1);
	});

	test('minInterval spaces runs out, counting from a run before it took over', () => {
		const run = vi.fn();
		const call = debounce(run, { wait: 1000, maxWait: 30_000, minInterval: 30_000, lastRun: Date.now() });
		call();
		vi.advanceTimersByTime(29_999);
		expect(run).not.toHaveBeenCalled();
		vi.advanceTimersByTime(1);
		expect(run).toHaveBeenCalledTimes(1);
		call();
		vi.advanceTimersByTime(1000);
		expect(run).toHaveBeenCalledTimes(1);
		vi.advanceTimersByTime(29_000);
		expect(run).toHaveBeenCalledTimes(2);
	});

	test('cancel drops a held call and flush runs it now', () => {
		const run = vi.fn();
		const call = debounce(run, { wait: 1000 });
		call();
		expect(call.pending).toBe(true);
		call.cancel();
		expect(call.pending).toBe(false);
		vi.advanceTimersByTime(5000);
		expect(run).not.toHaveBeenCalled();
		call();
		call.flush();
		expect(run).toHaveBeenCalledTimes(1);
		vi.advanceTimersByTime(5000);
		expect(run).toHaveBeenCalledTimes(1);
	});
});
