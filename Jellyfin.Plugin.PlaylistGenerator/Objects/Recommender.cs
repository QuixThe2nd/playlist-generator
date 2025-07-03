using System.Runtime.InteropServices.ComTypes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

using Jellyfin.Data.Entities;

using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.PlaylistGenerator.Objects;


public class Recommender(ILibraryManager libraryManager, IUserDataManager userDataManager, 
    ActivityDatabase activityDatabase, double explorationCoefficient = 3)
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
                new ScoredSong(song, user, userDataManager, libraryManager, activityDatabase)).ToList());
        }
        return recommendations;
    }

    // songs by the same artist are more similar than songs of the same genre (in general)
    public List<ScoredSong> RecommendByArtist(List<ScoredSong> songBasis, User user, bool experimentalRecommend)
    {
        List<ScoredSong> recommendations = [];
        HashSet<Guid> allArtists = [];
        foreach (var song in songBasis)
        {
            if (song.ArtistId == Guid.Empty) continue;
            allArtists.Add(song.ArtistId);
        }

        var query = new InternalItemsQuery
        {
            ArtistIds = [.. allArtists],
            Limit = 50,
            IncludeItemTypes = [BaseItemKind.Audio]
        };

        var similarSongs = libraryManager.GetItemList(query);
        var potentialSongs = similarSongs.Select(song => 
            new ScoredSong(song, user, userDataManager, libraryManager, activityDatabase)).ToList();
        
        potentialSongs = experimentalRecommend ? FilterByExplorationExperimental(potentialSongs) : FilterByExploration(potentialSongs);
        
        recommendations.AddRange(potentialSongs);

        return recommendations;
    }

    public List<ScoredSong> RecommendByGenre(List<ScoredSong> songBasis, User user, bool experimentalRecommend)
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
            new ScoredSong(song, user, userDataManager, libraryManager, activityDatabase)).ToList();
        
        potentialSongs = experimentalRecommend ? FilterByExplorationExperimental(potentialSongs) : FilterByExploration(potentialSongs);

        recommendations.AddRange(potentialSongs);
        return recommendations;
    }

    public List<ScoredSong> RecommendByFavourite(List<ScoredSong> songBasis, User user)
    {
        List<ScoredSong> recommendations = [];
        // retrieve favourite songs
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Audio]
        };
        var favouriteSongs = libraryManager.GetItemList(query);
        
        favouriteSongs = favouriteSongs.Where(song => song.IsFavoriteOrLiked(user)).ToList();
        
        var potentialSongs = favouriteSongs.Select(song => 
            new ScoredSong(song, user, userDataManager, libraryManager, activityDatabase)).ToList();
        recommendations.AddRange(potentialSongs);
        return recommendations;
    }

    
    private List<ScoredSong> FilterByExploration(List<ScoredSong> potentialSongs)
    {
        var minScore = potentialSongs.Min(song => song.Score);
        var maxScore = potentialSongs.Max(song => song.Score);
        var filteredSongs = explorationCoefficient switch
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
    
    private List<ScoredSong> FilterByExplorationExperimental(List<ScoredSong> potentialSongs)
    {
        // sort by score
        if (potentialSongs.Count == 0) {return [];}
        if (potentialSongs.Count == 1) {return potentialSongs;}
        if ((int)explorationCoefficient == 3) {return potentialSongs;}
        
        double multiplier = new [] { 0.5, 0.8, 1.0, 1.2, 1.5 }[(int)explorationCoefficient - 1];
        int count = potentialSongs.Count;
        int nonZeroScores = potentialSongs.Count(song => song.Score > 0); // K non-zero songs
        if (nonZeroScores < 10) {nonZeroScores = count;} // if there are less than 10 non-zero scores, use all songs
        double epsilon = 0.5; // probability for the song with the lowest non-zero score
        double lambda = -nonZeroScores / Math.Log(epsilon, 2) * multiplier; // λ=−K/ln(ε) * explorationCoefficient
        
        potentialSongs = potentialSongs.OrderByDescending(song => song.Score).ToList();
        
        double[] probabilities = new double[count];
        
        for (int i = 0; i < count; i++)
        {
            probabilities[i] = Math.Exp(-i / lambda);
        }
        
        var chosenSongs = SampleFromDistribution(potentialSongs, probabilities);
        return chosenSongs;
    }
    
    private List<ScoredSong> SampleFromDistribution(List<ScoredSong> songs, double[] probabilities)
    {
        Random random = new();
        List<ScoredSong> sampledSongs = [];
        sampledSongs.AddRange(songs.Where((t, i) => random.NextDouble() < probabilities[i]));
        return sampledSongs;
    }
}