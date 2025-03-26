# Playlist Generator

### Manifest url
```bash
https://...
```

### Requirements
Make sure Playback Reporting is installed, otherwise this plugin will not work.

### Options

`Playlist Name` - Set the name for your personal playlist.  
`Playlist Duration` - Set the duration of the playlist in minutes.  
`Minimum Song Time` - Set the minimum duration of a song to be considered (useful for skipping short jingles). 
Specified in seconds  
`Playlist User` - The username of the user to create the playlist for. Currently playlists are user based and
Jellyfin does not yet support ACLs (if I'm wrong, please let me know).
`Exploration Coefficient` - The higher the value, the more the recommender will prefer unknown songs.