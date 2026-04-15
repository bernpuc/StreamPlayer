using LibVLCSharp.Shared;

namespace StreamPlayer.Services.Interfaces;

public interface IPlayerService
{
    MediaPlayer MediaPlayer { get; }
    Task PlayAsync(string url);
}
