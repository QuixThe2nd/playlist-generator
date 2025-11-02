using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistGenerator.Configuration
{
    public class PlaylistConfig
    {
        public string PlaylistName { get; set; } = "My Personal Mix";
        public string PlaylistUserName { get; set; } = "username";
        public int PlaylistDuration { get; set; } = 360;
        public List<Guid> SelectedLibraryIds { get; set; } = [];
        public double ExplorationCoefficient { get; set; } = 3;
        public int ExcludeTime { get; set; } = 0;
        public bool ExperimentalFilter { get; set; } = false;
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public List<PlaylistConfig> Playlists { get; set; } = [];
    }
}
