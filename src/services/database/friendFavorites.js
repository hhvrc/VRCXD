import { createFavoriteTableQueries } from './localFavoriteTables.js';

const favorites = createFavoriteTableQueries('favorite_friend', 'user_id', 'userId');

const friendFavorites = {
    addFriendToLocalFavorites: favorites.add,
    removeFriendFromLocalFavorites: favorites.remove,
    renameFriendFavoriteGroup: favorites.renameGroup,
    deleteFriendFavoriteGroup: favorites.deleteGroup,
    getFriendFavorites: favorites.getAll
};

export { friendFavorites };
