import { describe, expect, test } from 'vitest';
import { welcomeDue } from '../onboarding/welcome.js';

const ready = { seen: false, hasToken: true, needsToken: false, signedIn: true };

describe('the onboarding welcome', () => {
	test('is due for someone connected and signed in who has not seen it', () => {
		expect(welcomeDue(ready)).toBe(true);
		expect(welcomeDue({ ...ready, seen: true })).toBe(false);
	});

	test('waits behind the server token prompt', () => {
		expect(welcomeDue({ ...ready, hasToken: false, needsToken: true })).toBe(false);
		// A stored token the server refused: the prompt is up again.
		expect(welcomeDue({ ...ready, needsToken: true })).toBe(false);
		// Before the prompt has decided, on the first render.
		expect(welcomeDue({ ...ready, hasToken: false })).toBe(false);
	});

	test('waits for the VRChat login, as the original only shows it inside the logged-in layout', () => {
		expect(welcomeDue({ ...ready, signedIn: false })).toBe(false);
	});
});
