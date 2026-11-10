import { createFavoriteTableQueries, createObjectCacheQueries } from './localFavoriteTables.js';

const favorites = createFavoriteTableQueries('favorite_world', 'world_id', 'worldId');
const cache = createObjectCacheQueries('cache_world');

const worldFavorites = {
    addWorldToCache: cache.add,
    addWorldToFavorites: favorites.add,
    renameWorldFavoriteGroup: favorites.renameGroup,
    deleteWorldFavoriteGroup: favorites.deleteGroup,
    removeWorldFromFavorites: favorites.remove,
    getWorldFavorites: favorites.getAll,
    removeWorldFromCache: cache.remove,
    getWorldCache: cache.getAll,
    getCachedWorldById: cache.getById
};

export { worldFavorites };
