import sqliteService from '../sqlite.js';

/**
 * Queries for a local favorite table (favorite_world, favorite_avatar,
 * favorite_friend). Each row is (id, created_at, <idColumn>, group_name).
 *
 * @param {string} table
 * @param {string} idColumn Column holding the favorited object id
 * @param {string} idKey Property name of that id on rows returned by getAll
 */
export function createFavoriteTableQueries(table, idColumn, idKey) {
    return {
        add(objectId, groupName) {
            sqliteService.executeNonQuery(
                `INSERT OR REPLACE INTO ${table} (${idColumn}, group_name, created_at) VALUES (@${idColumn}, @group_name, @created_at)`,
                {
                    [`@${idColumn}`]: objectId,
                    '@group_name': groupName,
                    '@created_at': new Date().toJSON()
                }
            );
        },

        remove(objectId, groupName) {
            sqliteService.executeNonQuery(
                `DELETE FROM ${table} WHERE ${idColumn} = @${idColumn} AND group_name = @group_name`,
                {
                    [`@${idColumn}`]: objectId,
                    '@group_name': groupName
                }
            );
        },

        renameGroup(newGroupName, groupName) {
            sqliteService.executeNonQuery(
                `UPDATE ${table} SET group_name = @new_group_name WHERE group_name = @group_name`,
                {
                    '@new_group_name': newGroupName,
                    '@group_name': groupName
                }
            );
        },

        deleteGroup(groupName) {
            sqliteService.executeNonQuery(`DELETE FROM ${table} WHERE group_name = @group_name`, {
                '@group_name': groupName
            });
        },

        async getAll() {
            const data = [];
            await sqliteService.execute((dbRow) => {
                data.push({
                    created_at: dbRow[1],
                    [idKey]: dbRow[2],
                    groupName: dbRow[3]
                });
            }, `SELECT * FROM ${table}`);
            return data;
        }
    };
}

/**
 * @param {any[]} dbRow
 * @returns {object}
 */
function parseCacheRow(dbRow) {
    return {
        id: dbRow[0],
        // added_at: dbRow[1],
        authorId: dbRow[2],
        authorName: dbRow[3],
        created_at: dbRow[4],
        description: dbRow[5],
        imageUrl: dbRow[6],
        name: dbRow[7],
        releaseStatus: dbRow[8],
        thumbnailImageUrl: dbRow[9],
        updated_at: dbRow[10],
        version: dbRow[11]
    };
}

/**
 * Queries for a local object cache table (cache_world, cache_avatar), which
 * keeps the data of locally favorited objects.
 *
 * @param {string} table
 */
export function createObjectCacheQueries(table) {
    return {
        add(entry) {
            sqliteService.executeNonQuery(
                `INSERT OR REPLACE INTO ${table} (id, added_at, author_id, author_name, created_at, description, image_url, name, release_status, thumbnail_image_url, updated_at, version) VALUES (@id, @added_at, @author_id, @author_name, @created_at, @description, @image_url, @name, @release_status, @thumbnail_image_url, @updated_at, @version)`,
                {
                    '@id': entry.id,
                    '@added_at': new Date().toJSON(),
                    '@author_id': entry.authorId,
                    '@author_name': entry.authorName,
                    '@created_at': entry.created_at,
                    '@description': entry.description,
                    '@image_url': entry.imageUrl,
                    '@name': entry.name,
                    '@release_status': entry.releaseStatus,
                    '@thumbnail_image_url': entry.thumbnailImageUrl,
                    '@updated_at': entry.updated_at,
                    '@version': entry.version
                }
            );
        },

        remove(id) {
            sqliteService.executeNonQuery(`DELETE FROM ${table} WHERE id = @id`, {
                '@id': id
            });
        },

        async getAll() {
            const data = [];
            await sqliteService.execute((dbRow) => {
                data.push(parseCacheRow(dbRow));
            }, `SELECT * FROM ${table}`);
            return data;
        },

        async getById(id) {
            let data = null;
            await sqliteService.execute(
                (dbRow) => {
                    data = parseCacheRow(dbRow);
                },
                `SELECT * FROM ${table} WHERE id = @id`,
                {
                    '@id': id
                }
            );
            return data;
        }
    };
}
