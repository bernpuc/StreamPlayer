using LibVLCSharp.Shared;
using StreamPlayer.Models;

namespace StreamPlayer.Services.Interfaces;

public interface IPlayerService
{
    MediaPlayer MediaPlayer { get; }
    Task<VideoInfo> PlayAsync(string url);
}
