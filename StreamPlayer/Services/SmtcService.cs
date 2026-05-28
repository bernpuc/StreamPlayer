using StreamPlayer.Services.Interfaces;
using Windows.Media;
using Windows.Media.Playback;

namespace StreamPlayer.Services;

public class SmtcService : ISmtcService
{
    private MediaPlayer? _mediaPlayer;
    private SystemMediaTransportControls? _smtc;
    private SystemMediaTransportControlsDisplayUpdater? _updater;

    public event EventHandler? PlayPausePressed;
    public event EventHandler? NextPressed;
    public event EventHandler? PreviousPressed;

    public void Initialize(IntPtr hwnd)
    {
        _mediaPlayer = new MediaPlayer();
        _smtc        = _mediaPlayer.SystemMediaTransportControls;

        _smtc.IsEnabled         = true;
        _smtc.IsPlayEnabled     = true;
        _smtc.IsPauseEnabled    = true;
        _smtc.IsNextEnabled     = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.PlaybackStatus    = MediaPlaybackStatus.Closed;

        _updater      = _smtc.DisplayUpdater;
        _updater.Type = MediaPlaybackType.Music;

        _smtc.ButtonPressed += OnButtonPressed;
    }

    public void UpdatePlayback(string title, string artist, bool isPlaying)
    {
        if (_smtc is null || _updater is null) return;

        _updater.MusicProperties.Title  = title;
        _updater.MusicProperties.Artist = artist;
        _updater.Update();
        _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
    }

    public void UpdateStopped()
    {
        if (_smtc is null) return;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
            case SystemMediaTransportControlsButton.Pause:
                PlayPausePressed?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Next:
                NextPressed?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousPressed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
