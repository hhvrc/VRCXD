import { beforeEach, describe, expect, test, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    execute: vi.fn()
}));

vi.mock('../../sqlite.js', () => ({
    default: {
        execute: mocks.execute,
        executeNonQuery: vi.fn()
    }
}));
vi.mock('../index.js', () => ({
    dbVars: {
        maxTableSize: 500,
        searchTableSize: 50,
        userId: 'usr_me',
        userPrefix: ''
    }
}));

import { gameLog } from '../gameLog.js';

describe('gameLog.getMyTopWorlds', () => {
    beforeEach(() => {
        mocks.execute.mockReset();
    });

    test('adds an exclude clause when a home world id is provided', async () => {
        mocks.execute.mockImplementation(async (callback, _sql, _params) => {
            callback(['wrld_1', 'World One', 3, 9000]);
            return undefined;
        });

        const result = await gameLog.getMyTopWorlds(30, 5, 'time', 'wrld_home');

        expect(result).toEqual([
            {
                worldId: 'wrld_1',
                worldName: 'World One',
                visitCount: 3,
                totalTime: 9000
            }
        ]);
        expect(mocks.execute).toHaveBeenCalledTimes(1);
        expect(mocks.execute.mock.calls[0][1]).toContain('AND world_id != @excludeWorldId');
        expect(mocks.execute.mock.calls[0][2]).toMatchObject({
            '@limit': 5,
            '@daysOffset': '-30 days',
            '@excludeWorldId': 'wrld_home'
        });
    });
});

describe('gameLog table queries', () => {
    beforeEach(() => {
        mocks.execute.mockReset();
        mocks.execute.mockResolvedValue(undefined);
    });

    const subSelect = (sql, table) => sql.split(' UNION ALL ').find((part) => part.includes(`FROM ${table} `));

    test('lookup queries every table and inlines escaped VIP ids', async () => {
        await gameLog.lookupGameLogDatabase([], ['usr_a', "usr_b'c"], 20);

        const [, sql, args] = mocks.execute.mock.calls[0];
        expect(sql.match(/FROM gamelog_\w+/g)).toEqual([
            'FROM gamelog_location',
            'FROM gamelog_join_leave',
            'FROM gamelog_portal_spawn',
            'FROM gamelog_event',
            'FROM gamelog_external',
            'FROM gamelog_video_play',
            'FROM gamelog_resource_load'
        ]);
        expect(sql).toMatch(/^SELECT id, created_at, .*, data, message FROM \(/);
        expect(subSelect(sql, 'gamelog_join_leave')).toContain("WHERE user_id IN ('usr_a', 'usr_b''c') ORDER BY");
        expect(subSelect(sql, 'gamelog_join_leave')).not.toContain('usr_me');
        expect(subSelect(sql, 'gamelog_location')).not.toContain('WHERE');
        expect(subSelect(sql, 'gamelog_event')).not.toContain('WHERE');
        expect(args).toEqual({ '@limit': 20, '@perTable': 20 });
    });

    test('filters select tables and sub types', async () => {
        await gameLog.lookupGameLogDatabase(['OnPlayerLeft', 'ImageLoad'], []);

        const [, sql, args] = mocks.execute.mock.calls[0];
        expect(sql.match(/FROM gamelog_\w+/g)).toEqual(['FROM gamelog_join_leave', 'FROM gamelog_resource_load']);
        expect(subSelect(sql, 'gamelog_join_leave')).toContain("WHERE type = 'OnPlayerLeft' ORDER BY");
        expect(subSelect(sql, 'gamelog_resource_load')).toContain("WHERE resource_type != 'StringLoad' ORDER BY");
        expect(args).toEqual({ '@limit': 500, '@perTable': 500 });
    });

    test('returns nothing without querying when no table matches the filters', async () => {
        expect(await gameLog.getGameLogByLocation('wrld_1', ['Event', 'External'])).toEqual([]);
        expect(await gameLog.lookupGameLogDatabase(['Unknown'], [])).toEqual([]);
        expect(mocks.execute).not.toHaveBeenCalled();
    });

    test('search binds VIP ids and hides own join/leave rows', async () => {
        await gameLog.searchGameLogDatabase('foo', [], ['usr_a']);

        const [, sql, args] = mocks.execute.mock.calls[0];
        expect(subSelect(sql, 'gamelog_join_leave')).toContain(
            "WHERE display_name LIKE @searchLike AND user_id != 'usr_me' AND user_id IN (@vip_0) ORDER BY"
        );
        expect(subSelect(sql, 'gamelog_video_play')).toContain(
            'WHERE (video_url LIKE @searchLike OR video_name LIKE @searchLike OR display_name LIKE @searchLike) AND user_id IN (@vip_0) ORDER BY'
        );
        expect(subSelect(sql, 'gamelog_event')).toContain('WHERE data LIKE @searchLike ORDER BY');
        expect(args).toEqual({ '@searchLike': '%foo%', '@limit': 50, '@perTable': 50, '@vip_0': 'usr_a' });
    });

    test('searching a world or group id queries by location without event or external tables', async () => {
        await gameLog.searchGameLogDatabase('wrld_1', [], [], 999);

        const [, sql, args] = mocks.execute.mock.calls[0];
        expect(sql.match(/FROM gamelog_\w+/g)).toEqual([
            'FROM gamelog_location',
            'FROM gamelog_join_leave',
            'FROM gamelog_portal_spawn',
            'FROM gamelog_video_play',
            'FROM gamelog_resource_load'
        ]);
        expect(sql).toMatch(/^SELECT id, .*, resource_type FROM \(/);
        expect(subSelect(sql, 'gamelog_join_leave')).toContain(
            "WHERE location LIKE @locationLike AND user_id != 'usr_me' ORDER BY"
        );
        expect(args).toEqual({ '@locationLike': '%wrld_1%', '@limit': 50, '@perTable': 50 });
    });

    test('maps rows by type', async () => {
        mocks.execute.mockImplementation(async (callback) => {
            const row = (values) => {
                const dbRow = Array(18).fill(null);
                for (const [i, value] of Object.entries(values)) {
                    dbRow[i] = value;
                }
                return dbRow;
            };
            callback(row({ 0: 1, 1: 't1', 2: 'Location', 4: 'wrld_1:1', 6: 60, 7: 'wrld_1', 8: 'World', 9: 'Group' }));
            callback(row({ 0: 2, 1: 't2', 2: 'OnPlayerJoined', 3: 'Alice', 4: 'wrld_1:1', 5: 'usr_a', 6: 5 }));
            callback(row({ 0: 3, 1: 't3', 2: 'PortalSpawn', 3: 'Bob', 4: 'loc', 5: 'usr_b', 8: 'W', 10: 'inst' }));
            callback(row({ 0: 4, 1: 't4', 2: 'Event', 16: 'data' }));
            callback(row({ 0: 5, 1: 't5', 2: 'External', 3: 'Ext', 4: 'loc', 5: 'usr_e', 17: 'msg' }));
            callback(
                row({ 0: 6, 1: 't6', 2: 'VideoPlay', 3: 'Vid', 4: 'loc', 5: 'usr_v', 11: 'url', 12: 'name', 13: 'vid' })
            );
            callback(row({ 0: 7, 1: 't7', 2: 'ImageLoad', 4: 'loc', 14: 'img', 15: 'ImageLoad' }));
            callback(row({ 0: 8, 1: 't8', 2: 'Other', 4: 'loc' }));
        });

        const result = await gameLog.lookupGameLogDatabase([], []);

        expect(result).toEqual([
            {
                rowId: 1,
                created_at: 't1',
                type: 'Location',
                location: 'wrld_1:1',
                worldId: 'wrld_1',
                worldName: 'World',
                time: 60,
                groupName: 'Group'
            },
            {
                rowId: 2,
                created_at: 't2',
                type: 'OnPlayerJoined',
                displayName: 'Alice',
                location: 'wrld_1:1',
                userId: 'usr_a',
                time: 5
            },
            {
                rowId: 3,
                created_at: 't3',
                type: 'PortalSpawn',
                displayName: 'Bob',
                location: 'loc',
                userId: 'usr_b',
                instanceId: 'inst',
                worldName: 'W'
            },
            { rowId: 4, created_at: 't4', type: 'Event', data: 'data' },
            {
                rowId: 5,
                created_at: 't5',
                type: 'External',
                message: 'msg',
                displayName: 'Ext',
                userId: 'usr_e',
                location: 'loc'
            },
            {
                rowId: 6,
                created_at: 't6',
                type: 'VideoPlay',
                videoUrl: 'url',
                videoName: 'name',
                videoId: 'vid',
                location: 'loc',
                displayName: 'Vid',
                userId: 'usr_v'
            },
            { rowId: 7, created_at: 't7', type: 'ImageLoad', resourceUrl: 'img', location: 'loc' },
            { rowId: 8, created_at: 't8', type: 'Other' }
        ]);
    });
});
