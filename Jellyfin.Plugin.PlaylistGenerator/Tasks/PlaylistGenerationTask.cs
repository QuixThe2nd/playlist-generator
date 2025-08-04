using System.Runtime.InteropServices;
using Jellyfin.Plugin.PlaylistGenerator.Configuration;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

using Jellyfin.Data.Entities;
using Jellyfin.Data.Entities.Libraries;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Playlists;
using Jellyfin.Plugin.PlaylistGenerator.Objects;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.PlaylistGenerator.Tasks;

public class PlaylistGenerationTask(ILibraryManager libraryManager, 
                                    IUserManager userManager, 
                                    IUserDataManager userDataManager, 
                                    ILogger<PlaylistGenerationTask> logManager,
                                    IPlaylistManager playlistManager,
                                    IServerApplicationPaths applicationPaths,
                                    IFileSystem fileSystem
                                    ) : IScheduledTask
{
    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly IUserManager _userManager = userManager;
    private readonly IUserDataManager _userDataManager = userDataManager;
    private readonly IPlaylistManager _playlistManager = playlistManager;
    private readonly ILogger<PlaylistGenerationTask> _logger = logManager;
    private readonly IServerApplicationPaths _paths = applicationPaths;
    private readonly IFileSystem _fileSystem = fileSystem;
    private ActivityDatabase _activityDatabase = null!;



    public string Name => "Generate Personal Playlist";
    public string Key => "PlaylistGenerationTask";
    public string Description => "Generate a playlist based on previous listen data + similarity.";
    public string Category => "Library";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        try
        {
            _activityDatabase = new ActivityDatabase(_logger, _paths, _fileSystem, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while generating the playlist.");
            return Task.CompletedTask;
        }

        _logger.LogInformation($"Start generating playlist with Exploration {Config.ExplorationCoefficient} " +
                               $"for {Config.PlaylistUserName}");
        
        // get user to identify listen data
        var currentUser = _userManager.GetUserByName(Config.PlaylistUserName);

        if (currentUser == null)
        {
            _logger.LogWarning($"User: {Config.PlaylistUserName} not found. Aborting.");
            return Task.CompletedTask;
        }
        
        // get all libraries and then filter for music libraries
        var allFolders = _libraryManager.GetUserRootFolder()
            .GetChildren(currentUser, true)
            .OfType<Folder>()
            .ToList();
        
        var musicLibraries = allFolders.Select(folder => folder as ICollectionFolder)
            .Where(collectionFolder => collectionFolder?.CollectionType == CollectionType.music).ToList();

        // first get all songs
        var songList = new List<ScoredSong>();

        // search for songs in the music libraries
        var allAudio = new List<BaseItem>();
        foreach (var library in musicLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (library == null)
            {
                _logger.LogWarning("No library here, skipping.");
                continue;
            }
            _logger.LogInformation($"Searching for songs in library: {library.Name}");
            var songQuery = new InternalItemsQuery{
                IncludeItemTypes = [BaseItemKind.Audio], 
                ParentId = library.Id,
                Recursive = true
            };
            var audioItems = _libraryManager.GetItemList(songQuery);
            allAudio.AddRange(audioItems);
        }

        if (allAudio.Count <= 0)
        {
            _logger.LogWarning("No music found.");
            return Task.CompletedTask;
        }
        
        // filter out theme songs and songs that are too short
        _logger.LogInformation($"Found {allAudio.Count} songs, filtering out theme songs and short songs.");
        var noThemeSongs = allAudio.Where(song => song.IsThemeMedia == false).ToList();
        var songs = noThemeSongs.Where(song => (int)((long)(song.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond) > Config.ExcludeTime).ToList();
        
        if (songs.Count <= 0)
        {
            _logger.LogWarning("No music found after filtering.");
            return Task.CompletedTask;
        }

        _logger.LogInformation($"Found {songs.Count} songs");

        foreach (var song in songs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            songList.Add(new ScoredSong(song, currentUser, _userDataManager, _libraryManager, _activityDatabase));
        }

        // initialise the Recommenders and get some recommendations based on our top
        var experimentalFilter = Config.ExperimentalFilter;
        PlaylistService playlistServer = new(_playlistManager, _libraryManager);
        Recommender playlistRecommender = new(_libraryManager, _userDataManager, _activityDatabase, Config.ExplorationCoefficient);
        
        List<ScoredSong> topSongs = [.. songList.OrderByDescending(song => song.Score).Take(20)];
        var similarBySong = playlistRecommender.RecommendSimilar(topSongs, currentUser);
        var similarByGenre = playlistRecommender.RecommendByGenre(topSongs, currentUser, experimentalFilter);
        var similarByArtist = playlistRecommender.RecommendByArtist(topSongs, currentUser, experimentalFilter);
        var favouriteSongs = playlistRecommender.RecommendByFavourite(topSongs, currentUser);

        List<ScoredSong> allSongs = [..topSongs];
        allSongs.AddRange(similarBySong);
        allSongs.AddRange(similarByGenre);
        allSongs.AddRange(similarByArtist);
        allSongs.AddRange(favouriteSongs);
        
        // prune songs that are too short or have no ParentId 
        allSongs = allSongs.Where(song => (song.Song.RunTimeTicks ?? 0 / TimeSpan.TicksPerSecond) >= Config.ExcludeTime 
                                          && song.Song.ParentId != Guid.Empty).ToList();

        _logger.LogInformation($"Highest score: {allSongs[0].Score} for song: {allSongs[0].Song.Name}");
        var assembledPlaylist = PlaylistService.AssemblePlaylist(allSongs, Config.PlaylistDuration, 
            playlistRecommender, currentUser);
        assembledPlaylist = PlaylistService.GentleShuffle(assembledPlaylist, 10);

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

