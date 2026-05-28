namespace StreamPlayer.Services.Interfaces;

public interface ISmtcService
{
    event EventHandler PlayPausePressed;
    event EventHandler NextPressed;
    event EventHandler PreviousPressed;

    void Initialize(IntPtr hwnd);
    void UpdatePlayback(string title, string artist, bool isPlaying);
    void UpdateStopped();
}
