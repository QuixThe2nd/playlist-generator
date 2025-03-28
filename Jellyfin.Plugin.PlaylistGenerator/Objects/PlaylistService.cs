using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Controller.Playlists;

using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using System.Collections;

namespace Jellyfin.Plugin.PlaylistGenerator.Objects;

// Service to create and delete playlists
public class PlaylistService(IPlaylistManager playlistManager, ILibraryManager libraryManager)
{
    private readonly IPlaylistManager _playlistManager = playlistManager;
    private readonly ILibraryManager _libraryManager = libraryManager;

    public static List<ScoredSong> AssemblePlaylist(List<ScoredSong> songs, int maxLength, Recommender recommender, User user)
    {
        int maxLengthSeconds = maxLength * 60;
        int totalSeconds = 0;
        int i = 0;
        HashSet<Guid> seenGuids = []; 
        List<ScoredSong> assembledPlaylist = [];
        while (totalSeconds < maxLengthSeconds && i < songs.Count)
        {   
            // make sure song is valid and is not already in the playlist
            if (songs[i].Song.RunTimeTicks == null || seenGuids.Contains(songs[i].Song.Id))
            {
                i++;
                continue;
            }

            assembledPlaylist.Add(songs[i]);
            totalSeconds += (int)((long)(songs[i].Song.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond);
            seenGuids.Add(songs[i].Song.Id);
            i++;
        }
        if (totalSeconds > maxLengthSeconds)
        {
            Console.WriteLine($"Stopped because of time length: {totalSeconds} vs {maxLengthSeconds}");
        }
        else
        {
            // if we run out of recommendations we just fill it up with songs similar to the ones
            // we already have in the playlist
            while (totalSeconds < maxLengthSeconds)
            {
                List<ScoredSong> randomFiller = recommender.RecommendSimilar(
                    [assembledPlaylist[new Random().Next(0, assembledPlaylist.Count)]], user);
                foreach (ScoredSong filler in randomFiller)
                {
                    if (seenGuids.Contains(filler.Song.Id) || filler.Song.ParentId == Guid.Empty) continue;
                    
                    assembledPlaylist.Add(filler);
                    totalSeconds += (int)((long)(filler.Song.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond);
                    seenGuids.Add(filler.Song.Id);
                }
            }
        }
        return assembledPlaylist;
    }

    public static List<T> GentleShuffle<T>(List<T> array, int k)
    {
        Random random = new();
        List<T> shuffledArray = new(array);
        HashSet<int> usedIndices = [];
        var n = array.Count;

        for (int i = 0; i < n; i++)
        {
            var availableIndices = new HashSet<int>(Enumerable.Range(Math.Max(0, i - k),
                Math.Min(n, i + k + 1) - Math.Max(0, i - k)));
            availableIndices.ExceptWith(usedIndices);
            var nextAvailableIndices = new HashSet<int>(Enumerable.Range(Math.Max(0, i + 1 - k),
                Math.Min(n, i + 1 + k + 1) - Math.Max(0, i + 1 - k)));
            nextAvailableIndices.ExceptWith(usedIndices);

            if (availableIndices.Count == 0 || (nextAvailableIndices.Count == 0 && i < n - 1))
            {
                break;
            }
            
            int newIdx;
            if (i < n - 1 && availableIndices.Min() != nextAvailableIndices.Min())
            {
                newIdx = availableIndices.Min();
            }
            else
            {
                // get a random index from the available indices
                newIdx = availableIndices.ToList()[random.Next(availableIndices.Count)];
            }

            shuffledArray[newIdx] = array[i];
            usedIndices.Add(newIdx);
        }
        return shuffledArray;
    }

    public void CreatePlaylist(string playlistName, User user, List<ScoredSong> items)
    {

        // Create the playlist
        var request = new PlaylistCreationRequest
        {
            Name = playlistName,
            ItemIdList = items.Select(item => item.Song.Id).ToArray(),
            MediaType = MediaType.Audio,
            UserId = user.Id
        };
        var playlist = _playlistManager.CreatePlaylist(request);
    }

    public void RemovePlaylist(string playlistName)
    {
        // Find the playlist by name
        var playlists = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Playlist]
        });

        var playlist = playlists.FirstOrDefault(p => p.Name.Equals(playlistName));

        if (playlist == null) return;
        // Delete the playlist
        var options = new DeleteOptions{DeleteFileLocation = true};
        _libraryManager.DeleteItem(playlist, options);
    }
}
