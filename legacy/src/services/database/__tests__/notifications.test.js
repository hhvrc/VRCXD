import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    getNotifications: vi.fn(),
    lookupNotifications: vi.fn(),
    getNotificationsV2: vi.fn(),
    addNotificationV1: vi.fn(),
    deleteNotificationV1: vi.fn(),
    updateNotificationV1Expired: vi.fn(),
    addNotificationV2: vi.fn(),
    seenNotificationV2: vi.fn(),
    expireNotificationV2: vi.fn(),
    deleteNotificationV2: vi.fn()
}));

vi.mock('../../grpcClient.js', () => ({ default: mocks }));
vi.mock('../index.js', () => ({ dbVars: { maxTableSize: 500, userPrefix: '' } }));

import { notifications } from '../notifications.js';

describe('notifications', () => {
    beforeEach(() => {
        for (const mock of Object.values(mocks)) {
            mock.mockReset();
        }
    });

    test('reads pass the table-size cap and survive an unreachable server', async () => {
        mocks.getNotifications.mockResolvedValue(null);
        mocks.getNotificationsV2.mockResolvedValue(null);
        mocks.lookupNotifications.mockResolvedValue(null);

        await expect(notifications.getNotifications()).resolves.toEqual([]);
        await expect(notifications.getNotificationsV2()).resolves.toEqual([]);
        await expect(notifications.lookupNotificationDatabase('x', [], [])).resolves.toEqual([]);

        expect(mocks.getNotifications).toHaveBeenCalledWith(500);
        expect(mocks.getNotificationsV2).toHaveBeenCalledWith(500);
        expect(mocks.lookupNotifications).toHaveBeenCalledWith('x', [], [], 500);
    });

    // The local INSERT flattened details.* into columns and defaulted every
    // one of them, so a sparse notification still wrote a full row.
    test('a v1 add flattens details and fills in every missing field', () => {
        notifications.addNotificationToDatabase({
            id: 'not_1',
            created_at: '2026-09-18T10:00:00.000Z',
            type: 'friendRequest',
            senderUserId: 'usr_a',
            details: { worldName: 'World A' }
        });

        expect(mocks.addNotificationV1).toHaveBeenCalledWith({
            id: 'not_1',
            created_at: '2026-09-18T10:00:00.000Z',
            type: 'friendRequest',
            senderUserId: 'usr_a',
            senderUsername: '',
            receiverUserId: '',
            message: '',
            worldId: '',
            worldName: 'World A',
            imageUrl: '',
            inviteMessage: '',
            requestMessage: '',
            responseMessage: '',
            expired: false
        });
    });

    test('a top-level imageUrl fills in for a missing details.imageUrl', () => {
        notifications.addNotificationToDatabase({
            id: 'not_2',
            created_at: '2026-09-18T10:00:00.000Z',
            type: 'invite',
            imageUrl: 'https://img/1'
        });

        expect(mocks.addNotificationV1.mock.calls[0][0].imageUrl).toBe('https://img/1');
    });

    test('a notification missing a required field throws rather than being stored', () => {
        expect(() => notifications.addNotificationToDatabase({ id: 'not_3', type: 'invite' })).toThrow(
            'Notification is missing required field'
        );
        expect(mocks.addNotificationV1).not.toHaveBeenCalled();
    });

    test('a v2 add stringifies its json columns with the same empty defaults', () => {
        notifications.addNotificationV2ToDatabase({
            id: 'not_4',
            createdAt: '2026-09-18T10:00:00.000Z',
            type: 'boop',
            seen: 1
        });

        const sent = mocks.addNotificationV2.mock.calls[0][0];
        expect(sent.seen).toBe(true);
        expect([sent.dataJson, sent.responsesJson, sent.detailsJson]).toEqual(['{}', '[]', '{}']);
    });

    test('the id-only writes forward their id, and expiry coerces its flag', () => {
        notifications.deleteNotification('not_1');
        notifications.updateNotificationExpired({ id: 'not_1', $isExpired: 1 });
        notifications.seenNotificationV2('not_4');
        notifications.expireNotificationV2('not_4');
        notifications.deleteNotificationV2('not_4');

        expect(mocks.deleteNotificationV1).toHaveBeenCalledWith('not_1');
        expect(mocks.updateNotificationV1Expired).toHaveBeenCalledWith('not_1', true);
        expect(mocks.seenNotificationV2).toHaveBeenCalledWith('not_4');
        expect(mocks.expireNotificationV2).toHaveBeenCalledWith('not_4');
        expect(mocks.deleteNotificationV2).toHaveBeenCalledWith('not_4');
    });
});
