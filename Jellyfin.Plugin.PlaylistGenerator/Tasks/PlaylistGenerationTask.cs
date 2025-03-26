using Jellyfin.Plugin.PlaylistGenerator.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Playlists;
using Jellyfin.Plugin.PlaylistGenerator.Objects;

namespace Jellyfin.Plugin.PlaylistGenerator.Tasks;

public class PlaylistGenerationTask(ILibraryManager libraryManager, 
                                    IUserManager userManager, 
                                    IUserDataManager userDataManager, 
                                    ILogger<PlaylistGenerationTask> logManager,
                                    IPlaylistManager playlistManager) : IScheduledTask
{
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<PlaylistGenerationTask> _logger = logManager;
    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
    private readonly IUserManager _userManager = userManager;
    private readonly IUserDataManager _userDataManager = userDataManager;
    private readonly IPlaylistManager _playlistManager = playlistManager;


    public string Name => "Generate Personal Playlist";
    public string Key => "PlaylistGenerationTask";
    public string Description => "Generate a library based on previous listen data + similarity.";
    public string Category => "Library";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); 

        _logger.LogInformation($"Start generating playlist with Exploration {Config.ExplorationCoefficient} " +
                               $"for {Config.PlaylistUserName}");
        
        // first get all songs
        var songList = new List<ScoredSong>();
        var songQuery = new InternalItemsQuery{IncludeItemTypes = [BaseItemKind.Audio], Recursive = true};

        var allAudio = _libraryManager.GetItemList(songQuery);

        if (allAudio.Count <= 0)
        {
            _logger.LogWarning("No music found.");
            return Task.CompletedTask;
        }

        // filter out theme songs and songs that are too short
        var songs = allAudio.Where(song => song.IsThemeMedia == false && 
                                           (int)((long)(song.RunTimeTicks ?? 0) / 10_000_000) > Config.ExcludeTime).ToList();

        if (songs.Count <= 0)
        {
            _logger.LogWarning("No music found after filtering.");
            return Task.CompletedTask;
        }

        _logger.LogInformation($"Found {songs.Count} songs");
        
        // get user to identify listen data
        var currentUser = _userManager.GetUserByName(Config.PlaylistUserName);

        if (currentUser == null)
        {
            _logger.LogWarning($"User: {Config.PlaylistUserName} not found. Aborting.");
            return Task.CompletedTask;
        }

        foreach (var song in songs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            songList.Add(new ScoredSong(song, currentUser, _userDataManager, _libraryManager));
        }

        // initialise the Recommenders and get some recommendations based on our top
        PlaylistService playlistServer = new(_playlistManager, _libraryManager);
        Recommender playlistRecommender = new(_libraryManager, _userDataManager, Config.ExplorationCoefficient);

        List<ScoredSong> topSongs = [.. songList.OrderByDescending(song => song.Score).Take(20)];
        var similarBySong = playlistRecommender.RecommendSimilar(topSongs, currentUser);
        var similarByGenre = playlistRecommender.RecommendByGenre(topSongs, currentUser);
        var similarByArtist = playlistRecommender.RecommendByArtist(topSongs, currentUser);

        List<ScoredSong> allSongs = [..topSongs];
        allSongs.AddRange(similarBySong);
        allSongs.AddRange(similarByGenre);
        allSongs.AddRange(similarByArtist);

        _logger.LogInformation($"Highest score: {allSongs[0].Score} for song: {allSongs[0].Song.Name}");
        var assembledPlaylist = PlaylistService.AssemblePlaylist(allSongs, Config.PlaylistDuration, 
            playlistRecommender, currentUser);
        PlaylistService.GentleShuffle(assembledPlaylist, 5);

        // check if playlist exists
        var allPlaylists = _libraryManager.GetItemList(new InternalItemsQuery{IncludeItemTypes = 
            [BaseItemKind.Playlist]});

        if (allPlaylists.Any(playlist => playlist.Name.Equals(Config.PlaylistName))) 
        {
            _logger.LogInformation($"Playlist {Config.PlaylistName} exists. Overwriting.");
            playlistServer.RemovePlaylist(Config.PlaylistName);
        }

        // make the playlist
        playlistServer.CreatePlaylist(Config.PlaylistName, currentUser, assembledPlaylist);

        _logger.LogInformation($"Generated personal playlist for {currentUser.Username}.");
        return Task.CompletedTask;
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            // Example trigger: Run every day at midnight
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(0).Ticks
            }
        ];
    }
}

