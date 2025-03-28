# Playlist Generator

<div align="center">
 <p>
    <img src="https://raw.githubusercontent.com/Eeeeelias/playlist-generator/refs/heads/main/images/logo.png" 
    alt="Plugin Banner" />
</p>
<p>
    Create personal playlists based on your listening history.
</p>

</div>

### Manifest url
```bash
https://raw.githubusercontent.com/Eeeeelias/playlist-generator/refs/heads/main/manifest.json
```

### Requirements
Make sure Playback Reporting is installed, otherwise this plugin will not work.

### Options

`Playlist Name` - Set the name for your personal playlist.  
`Playlist Duration` - Set the duration of the playlist in minutes.  
`Minimum Song Time` - Set the minimum duration of a song to be considered (useful for skipping short jingles). 
Specified in seconds  
`Playlist User` - The username of the user to create the playlist for. Currently playlists can be created only for one user.
I plan on expanding on this later.  
`Exploration Coefficient` - The higher the value, the more the recommender will prefer unknown songs.

### Open issues
- Add favourite songs: The original jellyfin music script adds 0-5 favourite songs to the playlist.
- Minimum song length is only considered for the first song retrieval. Songs that are added later on are not 
checked for their length.
- Create a playlist image: Maybe we can create a playlist image based on the songs in the playlist.
