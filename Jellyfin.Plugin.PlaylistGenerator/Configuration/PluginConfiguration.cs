using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistGenerator.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
        }

        public int PlaylistDuration { get; set; } = 360;

        public string PlaylistName { get; set; } = "My Personal Mix";

        public string PlaylistUserName { get; set; } = "username";

        public double ExplorationCoefficient { get; set; } = 3;

        public int ExcludeTime { get; set; } = 0;
        
        public bool ExperimentalRecommender { get; set; } = false;
    }
}