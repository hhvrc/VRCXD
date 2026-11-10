import { beforeEach, describe, expect, test, vi } from 'vitest';
import { render } from 'svelte/server';

const get = vi.hoisted(() => vi.fn());
vi.mock('../api/client.js', async (original) => ({ ...(await original<typeof import('../api/client.js')>()), api: { get, post: vi.fn() } }));

import type { Component } from 'svelte';
import { appearance } from '../settings/appearance.svelte.js';
import Location from '../components/instance/location.svelte';
import LocationWorld from '../components/instance/location-world.svelte';
import { closedInstances } from '../instances/instance-launch.svelte.js';
import Host from './fixtures/tooltip-render-host.svelte';

// The original's Location.vue and LocationWorld.vue tests, as server renders of the port's
// location.svelte and location-world.svelte.

const html = <P extends Record<string, unknown>>(component: Component<P>, props: P) =>
	render(Host, { props: { component: component as unknown as Component<Record<string, unknown>>, props } }).body;
const text = (body: string) => body.replace(/<!--.*?-->/g, '').replace(/<[^>]+>/g, '');
const location = (props: Parameters<typeof Location>[1]) => html(Location, props);
const pill = (props: Parameters<typeof LocationWorld>[1]) => html(LocationWorld, props);
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

beforeEach(() => {
	get.mockReset();
	get.mockRejectedValue(new Error('unknown'));
	closedInstances.clear();
	appearance.isAgeGatedInstancesVisible.value = false;
	appearance.showInstanceIdInLocation.value = false;
});

describe('location', () => {
	test('offline and private read as such, with no region flag', () => {
		expect(text(location({ location: 'offline' }))).toContain('Offline');
		expect(location({ location: 'offline' })).not.toContain('flags');
		expect(text(location({ location: 'private' }))).toContain('Private');
	});

	test('traveling shows a spinner and the destination', () => {
		const body = location({ location: 'traveling', traveling: 'wrld_12345:67890~region(us)' });
		expect(body).toContain('role="status"');
		expect(text(body)).toContain('wrld_12345');
	});

	test('an empty location is a transparent dash', () => {
		expect(location({ location: '' })).toContain('text-transparent');
	});

	test('classes given to it stay on the location', () => {
		expect(location({ location: 'wrld_12345:67890', class: 'text-xs custom-location' })).toContain('class="cursor-pointer text-xs custom-location"');
	});

	test('the hint, with the access type only when there is an instance', () => {
		expect(text(location({ location: 'wrld_12345:67890', hint: 'My World' }))).toContain('My World · Public');
		const worldOnly = text(location({ location: 'wrld_12345', hint: 'Just World Name' }));
		expect(worldOnly).toContain('Just World Name');
		expect(worldOnly).not.toContain('Public');
	});

	test('the world id until the world name has loaded', async () => {
		get.mockImplementation((url: string) => (url === '/1/worlds/wrld_named' ? Promise.resolve({ name: 'Amazing World' }) : Promise.reject(new Error('unknown'))));
		expect(text(location({ location: 'wrld_named:67890' }))).toContain('wrld_named');
		await settle();
		expect(text(location({ location: 'wrld_named:67890' }))).toContain('Amazing World');
		expect(text(location({ location: 'wrld_unknown:67890' }))).toContain('wrld_unknown · Public');
	});

	test('the region flag, us when the tag has none', () => {
		expect(location({ location: 'wrld_12345:67890~region(eu)' })).toContain('class="flags shrink-0 eu"');
		expect(location({ location: 'wrld_12345:67890' })).toContain('class="flags shrink-0 us"');
	});

	test('the group hint is shown in parentheses', () => {
		expect(text(location({ location: 'wrld_12345:67890~group(grp_hint)', grouphint: 'My Group' }))).toContain('(My Group)');
	});

	test('without a hint the group name is fetched and shown once loaded', async () => {
		get.mockImplementation((url: string) => (url === '/1/groups/grp_fetch' ? Promise.resolve({ name: 'Community Group' }) : Promise.reject(new Error('unknown'))));
		location({ location: 'wrld_12345:67890~group(grp_fetch)' });
		expect(get).toHaveBeenCalledWith('/1/groups/grp_fetch');
		await settle();
		expect(text(location({ location: 'wrld_12345:67890~group(grp_fetch)' }))).toContain('(Community Group)');
	});

	test('a lock for strict instances only', () => {
		expect(location({ location: 'wrld_12345:67890~strict' })).toContain('lucide-lock ');
		expect(location({ location: 'wrld_12345:67890' })).not.toContain('lucide-lock ');
	});

	test('the access type names', () => {
		expect(text(location({ location: 'wrld_12345:67890', hint: 'Test' }))).toContain('Public');
		expect(text(location({ location: 'wrld_12345:67890~private(usr_123)', hint: 'Test' }))).toContain('Invite');
		expect(text(location({ location: 'wrld_12345:67890~hidden(usr_123)', hint: 'Test' }))).toContain('Friends+');
		expect(text(location({ location: 'wrld_12345:67890~group(grp_123)~groupAccessType(public)', hint: 'Test', excludeGroupName: true }))).toContain('Group Public');
	});

	test('age-gated instances read Restricted with a lock while they are hidden', () => {
		const body = location({ location: 'wrld_12345:67890~ageGate', hint: 'Test World' });
		expect(text(body)).toContain('Restricted');
		expect(body).toContain('lucide-lock ');
		expect(text(body)).not.toContain('Test World');
	});

	test('age-gated instances read normally when they are shown', () => {
		appearance.isAgeGatedInstancesVisible.value = true;
		const body = text(location({ location: 'wrld_12345:67890~ageGate', hint: 'Test World' }));
		expect(body).toContain('Test World');
		expect(body).not.toContain('Restricted');
	});

	test('other instances read normally while age-gated ones are hidden', () => {
		const body = text(location({ location: 'wrld_12345:67890', hint: 'Normal World' }));
		expect(body).toContain('Normal World');
		expect(body).not.toContain('Restricted');
	});
});

describe('location world pill', () => {
	test('the own instance is marked unlocked', () => {
		expect(pill({ location: 'wrld_1:inst_1~private(usr_me)', shortName: 'short-1', currentUserId: 'usr_me' })).toContain('lucide-lock-open');
		expect(pill({ location: 'wrld_1:inst_1~private(usr_other)', shortName: 'short-1', currentUserId: 'usr_me' })).not.toContain('lucide-lock-open');
	});

	test('the instance whose short name the world page was opened with is unlocked too', () => {
		expect(pill({ location: 'wrld_1:inst_1~private(usr_other)', shortName: 'short-1', worldDialogShortName: 'short-1', currentUserId: 'usr_me' })).toContain('lucide-lock-open');
	});

	test('the group hint is shown in parentheses', () => {
		expect(text(pill({ location: 'wrld_1:inst_1~group(grp_1)', groupHint: 'Hint Group' }))).toContain('(Hint Group)');
	});

	test('without a hint the group name is fetched', async () => {
		get.mockImplementation((url: string) => (url === '/1/groups/grp_pill' ? Promise.resolve({ name: 'Fetched Group' }) : Promise.reject(new Error('unknown'))));
		pill({ location: 'wrld_1:inst_1~group(grp_pill)' });
		expect(get).toHaveBeenCalledWith('/1/groups/grp_pill');
		await settle();
		expect(text(pill({ location: 'wrld_1:inst_1~group(grp_pill)' }))).toContain('(Fetched Group)');
	});

	test('the instance name, the closed warning and the strict lock', () => {
		closedInstances.set('wrld_1:inst_1~friends(usr_other)~strict', '2026-01-01T00:00:00.000Z');
		const body = pill({ location: 'wrld_1:inst_1~friends(usr_other)~strict', displayName: 'Resolved Name', currentUserId: 'usr_me' });
		expect(text(body)).toContain('#Resolved Name');
		expect(body).toContain('lucide-triangle-alert');
		expect(body).toContain('lucide-lock ');
	});
});
