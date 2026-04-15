using LibVLCSharp.Shared;
using Prism.Commands;
using Prism.Mvvm;
using StreamPlayer.Models;
using StreamPlayer.Services.Interfaces;
using System.Windows;

namespace StreamPlayer.ViewModels;

public class MainWindowViewModel : BindableBase
{
    private readonly IPlayerService _playerService;

    // URL bar state
    private string _url = string.Empty;
    private string _validationMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isValidUrl;
    private bool _isLoading;

    // Transport state
    private bool _isPlaying;
    private bool _canControl;
    private double _sliderPosition;
    private string _timeDisplay = "0:00 / 0:00";
    private bool _isSeeking;
    private long _duration;

    // Metadata state
    private VideoInfo? _videoInfo;
    private string _currentChapterTitle = string.Empty;

    private const long SeekStepMs = 10_000;

    private static readonly HashSet<string> YouTubeHosts =
    [
        "youtube.com",
        "www.youtube.com",
        "youtu.be",
        "m.youtube.com"
    ];

    // --- URL bar properties ---

    public string Url
    {
        get => _url;
        set { SetProperty(ref _url, value); ValidateUrl(); }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsValidUrl
    {
        get => _isValidUrl;
        private set { SetProperty(ref _isValidUrl, value); PlayCommand.RaiseCanExecuteChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { SetProperty(ref _isLoading, value); PlayCommand.RaiseCanExecuteChanged(); }
    }

    // --- Transport properties ---

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    public bool CanControl
    {
        get => _canControl;
        private set
        {
            SetProperty(ref _canControl, value);
            PauseResumeCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            RewindCommand.RaiseCanExecuteChanged();
            FastForwardCommand.RaiseCanExecuteChanged();
        }
    }

    public double SliderPosition
    {
        get => _sliderPosition;
        set => SetProperty(ref _sliderPosition, value);
    }

    public string TimeDisplay
    {
        get => _timeDisplay;
        private set => SetProperty(ref _timeDisplay, value);
    }

    // --- Metadata properties ---

    public VideoInfo? VideoInfo
    {
        get => _videoInfo;
        private set { SetProperty(ref _videoInfo, value); RaisePropertyChanged(nameof(HasMetadata)); }
    }

    public bool HasMetadata => _videoInfo != null;

    public string CurrentChapterTitle
    {
        get => _currentChapterTitle;
        private set => SetProperty(ref _currentChapterTitle, value);
    }

    public MediaPlayer MediaPlayer => _playerService.MediaPlayer;

    // --- Commands ---

    public DelegateCommand PlayCommand        { get; }
    public DelegateCommand PauseResumeCommand { get; }
    public DelegateCommand StopCommand        { get; }
    public DelegateCommand RewindCommand      { get; }
    public DelegateCommand FastForwardCommand { get; }

    public MainWindowViewModel(IPlayerService playerService)
    {
        _playerService = playerService;

        PlayCommand        = new DelegateCommand(async () => await ExecutePlayAsync(), () => IsValidUrl && !IsLoading);
        PauseResumeCommand = new DelegateCommand(ExecutePauseResume, () => CanControl);
        StopCommand        = new DelegateCommand(ExecuteStop,        () => CanControl);
        RewindCommand      = new DelegateCommand(ExecuteRewind,      () => CanControl);
        FastForwardCommand = new DelegateCommand(ExecuteFastForward, () => CanControl);

        // All handlers use BeginInvoke (async) — never blocks the VLC internal thread.
        MediaPlayer.Playing += (_, _) =>
        {
            Log("[VLC] Playing");
            Dispatch(() => { IsPlaying = true; CanControl = true; StatusMessage = string.Empty; });
        };
        MediaPlayer.Paused += (_, _) =>
        {
            Log("[VLC] Paused");
            Dispatch(() => IsPlaying = false);
        };
        MediaPlayer.Stopped += (_, _) =>
        {
            Log("[VLC] Stopped");
            Dispatch(() => { IsPlaying = false; CanControl = false; ResetPlayback(); StatusMessage = string.Empty; });
        };
        MediaPlayer.EndReached += (_, _) =>
        {
            Log("[VLC] EndReached");
            Dispatch(() => { IsPlaying = false; CanControl = false; ResetPlayback(); StatusMessage = "Playback finished."; });
        };
        MediaPlayer.Buffering += (_, e) =>
        {
            Log($"[VLC] Buffering {e.Cache:F0}%");
            Dispatch(() => StatusMessage = e.Cache < 100 ? $"Buffering {e.Cache:F0}%…" : string.Empty);
        };
        MediaPlayer.EncounteredError += (_, _) =>
        {
            Log("[VLC] EncounteredError");
            Dispatch(() => { IsPlaying = false; CanControl = false; StatusMessage = "Playback error — check the URL and try again."; });
        };
        MediaPlayer.TimeChanged += (_, e) =>
        {
            Dispatch(() => UpdateProgress(e.Time));
        };
    }

    // --- URL validation ---

    private void ValidateUrl()
    {
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(_url))
        {
            ValidationMessage = string.Empty;
            IsValidUrl = false;
            return;
        }

        if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not "http" and not "https")
        {
            ValidationMessage = "Not a valid URL.";
            IsValidUrl = false;
            return;
        }

        if (!YouTubeHosts.Contains(uri.Host.ToLowerInvariant()))
        {
            ValidationMessage = "Not a YouTube URL.";
            IsValidUrl = false;
            return;
        }

        ValidationMessage = string.Empty;
        IsValidUrl = true;
    }

    // --- Command implementations ---

    private async Task ExecutePlayAsync()
    {
        IsLoading = true;
        StatusMessage = "Fetching stream info…";
        Log($"[Play] URL: {_url}");

        try
        {
            var info = await _playerService.PlayAsync(_url);
            Log($"[Play] Metadata: \"{info.Title}\" by {info.Channel}, {info.DurationMs}ms, {info.Chapters.Count} chapters");

            VideoInfo = info;

            // Seed duration from metadata so the seek bar is accurate before VLC's LengthChanged fires.
            if (info.DurationMs > 0)
                _duration = info.DurationMs;

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log($"[Play] Error: {ex.Message}");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecutePauseResume()
    {
        bool pausing = MediaPlayer.IsPlaying;
        Log($"[PauseResume] IsPlaying={pausing} → SetPause({pausing})");
        MediaPlayer.SetPause(pausing);
    }

    private void ExecuteStop()
    {
        Log("[Stop] Calling MediaPlayer.Stop()");
        MediaPlayer.Stop();
    }

    private void ExecuteRewind()
    {
        long before = MediaPlayer.Time;
        long target = Math.Max(0, before - SeekStepMs);
        Log($"[Rewind] {FormatMs(before)} → {FormatMs(target)}");
        MediaPlayer.Time = target;
    }

    private void ExecuteFastForward()
    {
        long before = MediaPlayer.Time;
        long len    = MediaPlayer.Length;
        long target = len > 0 ? Math.Min(len, before + SeekStepMs) : before;
        Log($"[FastForward] {FormatMs(before)} → {FormatMs(target)}  (len={FormatMs(len)})");
        MediaPlayer.Time = target;
    }

    // --- Seek helpers (called from code-behind) ---

    public void OnSeekStarted()
    {
        _isSeeking = true;
        Log("[Seek] DragStarted");
    }

    public void OnSeekCompleted(double sliderValue)
    {
        _isSeeking = false;
        if (CanControl && _duration > 0)
        {
            long target = (long)(sliderValue * _duration);
            Log($"[Seek] DragCompleted sliderValue={sliderValue:F3} → {FormatMs(target)}");
            MediaPlayer.Time = target;
        }
        else
        {
            Log($"[Seek] DragCompleted ignored — CanControl={CanControl}, _duration={_duration}");
        }
    }

    // --- Progress helpers ---

    private void UpdateProgress(long timeMs)
    {
        if (_isSeeking) return;

        // Prefer VLC's live Length; fall back to metadata duration.
        var liveDuration = MediaPlayer.Length;
        if (liveDuration > 0) _duration = liveDuration;

        SliderPosition = _duration > 0 ? (double)timeMs / _duration : 0;
        TimeDisplay    = $"{FormatMs(timeMs)} / {FormatMs(_duration > 0 ? _duration : 0)}";

        // Current chapter
        if (_videoInfo?.Chapters.Count > 0)
        {
            var ch = _videoInfo.Chapters.LastOrDefault(c => timeMs >= c.StartMs);
            CurrentChapterTitle = ch?.Title ?? string.Empty;
        }
    }

    private void ResetPlayback()
    {
        _duration = 0;
        SliderPosition = 0;
        TimeDisplay = "0:00 / 0:00";
        CurrentChapterTitle = string.Empty;
        VideoInfo = null;
    }

    private static string FormatMs(long ms)
    {
        if (ms < 0) return "?";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // BeginInvoke (async) — never blocks the VLC internal thread.
    private static void Dispatch(Action a) => Application.Current.Dispatcher.BeginInvoke(a);

    private static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
}
