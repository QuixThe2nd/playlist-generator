using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

using Jellyfin.Data.Entities;

using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.PlaylistGenerator.Objects;


public class Recommender(ILibraryManager libraryManager, IUserDataManager userDataManager, double explorationCoefficient = 3)
{
    public List<ScoredSong> RecommendSimilar(List<ScoredSong> songBasis, User user)
    {
        List<ScoredSong> recommendations = [];
        foreach (ScoredSong song in songBasis)
        {
            var query = new InternalItemsQuery
            {
                SimilarTo = song.Song,
                Limit = 3,
                IncludeItemTypes = [BaseItemKind.Audio]
            };

            var similarSongs = libraryManager.GetItemList(query);
            recommendations.AddRange(similarSongs.Select(song => 
                new ScoredSong(song, user, userDataManager, libraryManager)).ToList());
        }
        return recommendations;
    }

    // songs by the same artist are more similar than songs of the same genre (in general)
    public List<ScoredSong> RecommendByArtist(List<ScoredSong> songBasis, User user)
    {
        List<ScoredSong> recommendations = [];
        HashSet<Guid> allArtists = [];
        foreach (var song in songBasis)
        {
            allArtists.Add(song.ArtistId);
        }

        var query = new InternalItemsQuery
        {
            ArtistIds = [.. allArtists],
            Limit = 50,
            IncludeItemTypes = [BaseItemKind.Audio]
        };

        var similarSongs = libraryManager.GetItemList(query);
        List<ScoredSong> potentialSongs = similarSongs.Select(song => 
            new ScoredSong(song, user, userDataManager, libraryManager)).ToList();
        potentialSongs = FilterByExploration(potentialSongs);
        recommendations.AddRange(potentialSongs);

        return recommendations;
    }

    public List<ScoredSong> RecommendByGenre(List<ScoredSong> songBasis, User user)
    {
        List<ScoredSong> recommendations = [];
        HashSet<string> allGenres = [];
        foreach (var song in songBasis)
        {
            var genres = song.Song.Genres;
            allGenres.UnionWith(genres);
        }
        
        var query = new InternalItemsQuery
        {
            Genres = [.. allGenres],
            Limit = 50,
            IncludeItemTypes = [BaseItemKind.Audio]
        };

        var similarSongs = libraryManager.GetItemList(query);
        var potentialSongs = similarSongs.Select(song => 
            new ScoredSong(song, user, userDataManager, libraryManager)).ToList();
        potentialSongs = FilterByExploration(potentialSongs);

        recommendations.AddRange(potentialSongs);
        return recommendations;
    }

    private List<ScoredSong> FilterByExploration(List<ScoredSong> potentialSongs)
    {
        List<ScoredSong> filteredSongs = [];
        var minScore = potentialSongs.Min(song => song.Score);
        var maxScore = potentialSongs.Max(song => song.Score);
        filteredSongs = explorationCoefficient switch
        {
            1 => potentialSongs.Where(song => song.Score > maxScore / 2).ToList(),
            2 => potentialSongs.Where(song => song.Score > Math.Min(maxScore, 0.2) / 4).ToList(),
            3 => potentialSongs,
            4 => potentialSongs.Where(song => song.Score < Math.Max(minScore, 0.2) * 4).ToList(),
            5 => potentialSongs.Where(song => song.Score < minScore * 2).ToList(),
            _ => potentialSongs,
        };
        return filteredSongs;
    }
}