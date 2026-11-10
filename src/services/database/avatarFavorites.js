import { dbVars } from '../database';

import sqliteService from '../sqlite.js';
import { createFavoriteTableQueries, createObjectCacheQueries } from './localFavoriteTables.js';

const favorites = createFavoriteTableQueries('favorite_avatar', 'avatar_id', 'avatarId');
const cache = createObjectCacheQueries('cache_avatar');

const avatarFavorites = {
    addAvatarToCache: cache.add,

    addAvatarToHistory(avatarId) {
        sqliteService.executeNonQuery(
            `INSERT INTO ${dbVars.userPrefix}_avatar_history (avatar_id, created_at, time)
            VALUES (@avatar_id, @created_at, 0)
            ON CONFLICT(avatar_id) DO UPDATE SET created_at = @created_at`,
            {
                '@avatar_id': avatarId,
                '@created_at': new Date().toJSON()
            }
        );
    },

    async getAvatarTimeSpent(avatarId) {
        var ref = {
            timeSpent: 0,
            avatarId
        };
        await sqliteService.execute(
            (row) => {
                ref.timeSpent = row[0];
            },
            `SELECT time FROM ${dbVars.userPrefix}_avatar_history WHERE avatar_id = @avatarId`,
            {
                '@avatarId': avatarId
            }
        );

        return ref;
    },

    async getAllAvatarTimeSpent() {
        const map = new Map();
        await sqliteService.execute((row) => {
            map.set(row[0], row[1] || 0);
        }, `SELECT avatar_id, time FROM ${dbVars.userPrefix}_avatar_history`);

        return map;
    },

    addAvatarTimeSpent(avatarId, timeSpent) {
        sqliteService.executeNonQuery(
            `UPDATE ${dbVars.userPrefix}_avatar_history SET time = time + @timeSpent WHERE avatar_id = @avatarId`,
            {
                '@avatarId': avatarId,
                '@timeSpent': timeSpent
            }
        );
    },

    async getAvatarHistory(currentUserId, limit = 100) {
        var data = [];
        await sqliteService.execute((dbRow) => {
            var row = {
                id: dbRow[0],
                authorId: dbRow[5],
                authorName: dbRow[6],
                created_at: dbRow[7],
                description: dbRow[8],
                imageUrl: dbRow[9],
                name: dbRow[10],
                releaseStatus: dbRow[11],
                thumbnailImageUrl: dbRow[12],
                updated_at: dbRow[13],
                version: dbRow[14]
            };
            data.push(row);
        }, `SELECT * FROM ${dbVars.userPrefix}_avatar_history INNER JOIN cache_avatar ON cache_avatar.id = ${dbVars.userPrefix}_avatar_history.avatar_id WHERE author_id != '${currentUserId}' ORDER BY ${dbVars.userPrefix}_avatar_history.created_at DESC LIMIT ${limit}`);
        return data;
    },

    getCachedAvatarById: cache.getById,

    clearAvatarHistory() {
        sqliteService.executeNonQuery(`DELETE FROM ${dbVars.userPrefix}_avatar_history`);
        sqliteService.executeNonQuery('DELETE FROM cache_avatar');
    },

    addAvatarToFavorites: favorites.add,
    renameAvatarFavoriteGroup: favorites.renameGroup,
    deleteAvatarFavoriteGroup: favorites.deleteGroup,
    removeAvatarFromFavorites: favorites.remove,
    getAvatarFavorites: favorites.getAll,
    removeAvatarFromCache: cache.remove,
    getAvatarCache: cache.getAll
};

export { avatarFavorites };
