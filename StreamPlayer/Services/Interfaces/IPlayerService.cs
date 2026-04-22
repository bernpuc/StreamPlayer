using LibVLCSharp.Shared;
using StreamPlayer.Models;

namespace StreamPlayer.Services.Interfaces;

public interface IPlayerService
{
    MediaPlayer MediaPlayer { get; }
    VideoQuality Quality { get; set; }
    bool AudioOnly { get; set; }
    Task<VideoInfo> PlayAsync(string url);
    Task<IReadOnlyList<PlaylistEntry>> GetPlaylistEntriesAsync(string url);
}
