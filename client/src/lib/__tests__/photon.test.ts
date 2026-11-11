import { describe, expect, it } from 'vitest';
import { filterPhotonEvents, photonMarks, type PhotonEvent, type PhotonSnapshot } from '../photon/photon.svelte.js';

const lobby: PhotonSnapshot = {
	enabled: true,
	master: 2,
	currentUser: 1,
	players: [
		{ photonId: 1, userId: 'usr_me', displayName: 'Me', isMaster: false },
		{ photonId: 2, userId: 'usr_ann', displayName: 'Ann', isMaster: true, join: { hasInstantiated: true, canModerateInstance: true, inVrMode: true } },
		{ photonId: 3, displayName: 'Bob', isMaster: false }
	],
	timeouts: [{ userId: 'usr_bob', displayName: 'Bob', time: 9, rawTime: 9000 }],
	events: [],
	previousEvents: []
};

describe('the Photon lobby in the player list', () => {
	it('matches players by id, else by name, as updatePlayerListDebounce did', () => {
		expect(photonMarks(lobby, 'usr_ann', 'Ann')).toEqual({ photonId: 2, isMaster: true, isModerator: true, inVrMode: true, groupOnNameplate: '', timeoutTime: 0 });
		expect(photonMarks(lobby, 'usr_bob', 'Bob')).toMatchObject({ photonId: 3, isMaster: false, timeoutTime: 9 });
		expect(photonMarks(lobby, 'usr_x', 'Nobody').photonId).toBe(-1);
		expect(photonMarks({ ...lobby, master: 0 }, 'usr_ann', 'Ann').isMaster).toBe(false);
	});

	it('filters the event table by type and by name or text', () => {
		const row = (type: string, displayName: string, text: string): PhotonEvent => ({ createdAt: '2026-11-10T20:00:00Z', type, displayName, userId: '', isMaster: false, text });
		const rows = [row('ChatBoxMessage', 'Ann', 'Hello'), row('OnPlayerJoined', 'Bob', 'has joined')];
		expect(filterPhotonEvents(rows, '', [])).toHaveLength(2);
		expect(filterPhotonEvents(rows, 'hello', []).map((r) => r.displayName)).toEqual(['Ann']);
		expect(filterPhotonEvents(rows, 'bob', []).map((r) => r.type)).toEqual(['OnPlayerJoined']);
		expect(filterPhotonEvents(rows, '', ['ChatBoxMessage'])).toHaveLength(1);
	});
});
