using LibVLCSharp.Shared;
using Prism.Commands;
using Prism.Mvvm;
using StreamPlayer.Models;
using StreamPlayer.Services.Interfaces;
using System.Diagnostics;
using System.Windows;

namespace StreamPlayer.ViewModels;

public class MainWindowViewModel : BindableBase
{
    private readonly IPlayerService _playerService;
    private readonly IHistoryService _historyService;
    private readonly IAcrCloudService _acrCloudService;

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
    private string _chapterArtist       = string.Empty;
    private string _chapterSong         = string.Empty;

    // History state
    private IReadOnlyList<HistoryEntry> _historyEntries = [];
    private bool _isHistoryOpen;

    // Volume state
    private int  _volume       = 100;
    private bool _isMuted      = false;
    private bool _isVolumeOpen = false;

    // ACRCloud state
    private bool   _isIdentifying    = false;
    private bool   _isAcrSettingsOpen = false;
    private string _acrHost          = string.Empty;
    private string _acrKey           = string.Empty;
    private string _acrSecret        = string.Empty;

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
            IdentifyCommand?.RaiseCanExecuteChanged();
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

    // --- History properties ---

    public IReadOnlyList<HistoryEntry> HistoryEntries
    {
        get => _historyEntries;
        private set { SetProperty(ref _historyEntries, value); RaisePropertyChanged(nameof(HasHistory)); }
    }

    public bool HasHistory => _historyEntries.Count > 0;

    public bool IsHistoryOpen
    {
        get => _isHistoryOpen;
        set => SetProperty(ref _isHistoryOpen, value);
    }

    // --- Volume properties ---

    public int Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, Math.Clamp(value, 0, 100))) return;
            MediaPlayer.Volume = _volume;
            RaisePropertyChanged(nameof(VolumeIcon));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value)) return;
            MediaPlayer.Mute = _isMuted;
            RaisePropertyChanged(nameof(VolumeIcon));
        }
    }

    public string VolumeIcon => (_isMuted || _volume == 0) ? "🔇"
                              : _volume <= 33              ? "🔈"
                              : _volume <= 66              ? "🔉"
                              :                              "🔊";

    public bool IsVolumeOpen
    {
        get => _isVolumeOpen;
        set => SetProperty(ref _isVolumeOpen, value);
    }

    // --- ACRCloud properties ---

    public bool IsIdentifying
    {
        get => _isIdentifying;
        private set
        {
            SetProperty(ref _isIdentifying, value);
            IdentifyCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAcrSettingsOpen
    {
        get => _isAcrSettingsOpen;
        set => SetProperty(ref _isAcrSettingsOpen, value);
    }

    public string AcrHost
    {
        get => _acrHost;
        set => SetProperty(ref _acrHost, value);
    }

    public string AcrKey
    {
        get => _acrKey;
        set => SetProperty(ref _acrKey, value);
    }

    public string AcrSecret
    {
        get => _acrSecret;
        set => SetProperty(ref _acrSecret, value);
    }

    // --- Metadata properties ---

    public VideoInfo? VideoInfo
    {
        get => _videoInfo;
        private set { SetProperty(ref _videoInfo, value); RaisePropertyChanged(nameof(HasMetadata)); }
    }

    public bool HasMetadata => _videoInfo != null;

    private void RaiseLookupCanExecuteChanged()
    {
        OpenLastFmCommand.RaiseCanExecuteChanged();
        OpenGeniusCommand.RaiseCanExecuteChanged();
        OpenWikipediaCommand.RaiseCanExecuteChanged();
    }

    public string CurrentChapterTitle
    {
        get => _currentChapterTitle;
        private set
        {
            if (!SetProperty(ref _currentChapterTitle, value)) return;
            ParseChapterTitle(value);
        }
    }

    public string ChapterArtist
    {
        get => _chapterArtist;
        private set { SetProperty(ref _chapterArtist, value); RaisePropertyChanged(nameof(HasChapterArtist)); }
    }

    public string ChapterSong
    {
        get => _chapterSong;
        private set { SetProperty(ref _chapterSong, value); RaisePropertyChanged(nameof(HasChapterSong)); }
    }

    public bool HasChapterArtist => !string.IsNullOrEmpty(_chapterArtist);
    public bool HasChapterSong   => !string.IsNullOrEmpty(_chapterSong);

    public MediaPlayer MediaPlayer => _playerService.MediaPlayer;

    // --- Commands ---

    public DelegateCommand PlayCommand        { get; }
    public DelegateCommand PauseResumeCommand { get; }
    public DelegateCommand StopCommand        { get; }
    public DelegateCommand RewindCommand      { get; }
    public DelegateCommand FastForwardCommand { get; }
    public DelegateCommand ToggleHistoryCommand { get; }
    public DelegateCommand ToggleVolumeCommand  { get; }
    public DelegateCommand ToggleMuteCommand    { get; }
    public DelegateCommand OpenLastFmCommand    { get; }
    public DelegateCommand OpenGeniusCommand    { get; }
    public DelegateCommand OpenWikipediaCommand { get; }
    public DelegateCommand OpenChapterArtistLastFmCommand    { get; }
    public DelegateCommand OpenChapterArtistWikipediaCommand { get; }
    public DelegateCommand OpenChapterSongGeniusCommand      { get; }
    public DelegateCommand IdentifyCommand         { get; }
    public DelegateCommand SaveAcrSettingsCommand  { get; }
    public DelegateCommand CloseAcrSettingsCommand { get; }

    public MainWindowViewModel(IPlayerService playerService, IHistoryService historyService, IAcrCloudService acrCloudService)
    {
        _playerService    = playerService;
        _historyService   = historyService;
        _acrCloudService  = acrCloudService;
        _historyEntries   = [.._historyService.Entries];

        PlayCommand           = new DelegateCommand(async () => await ExecutePlayAsync(), () => IsValidUrl && !IsLoading);
        PauseResumeCommand    = new DelegateCommand(ExecutePauseResume, () => CanControl);
        StopCommand           = new DelegateCommand(ExecuteStop,        () => CanControl);
        RewindCommand         = new DelegateCommand(ExecuteRewind,      () => CanControl);
        FastForwardCommand    = new DelegateCommand(ExecuteFastForward, () => CanControl);
        ToggleHistoryCommand  = new DelegateCommand(() => IsHistoryOpen = !IsHistoryOpen);
        ToggleVolumeCommand   = new DelegateCommand(() => IsVolumeOpen = !IsVolumeOpen);
        ToggleMuteCommand     = new DelegateCommand(() => IsMuted = !IsMuted);
        MediaPlayer.Volume    = _volume;

        OpenLastFmCommand    = new DelegateCommand(ExecuteOpenLastFm,    () => HasMetadata);
        OpenGeniusCommand    = new DelegateCommand(ExecuteOpenGenius,    () => HasMetadata);
        OpenWikipediaCommand = new DelegateCommand(ExecuteOpenWikipedia, () => HasMetadata);

        OpenChapterArtistLastFmCommand    = new DelegateCommand(
            () => OpenUrl($"https://www.last.fm/search?q={Uri.EscapeDataString(_chapterArtist)}"),
            () => HasChapterArtist);
        OpenChapterArtistWikipediaCommand = new DelegateCommand(
            () => OpenUrl($"https://en.wikipedia.org/wiki/{Uri.EscapeDataString(_chapterArtist)}"),
            () => HasChapterArtist);
        OpenChapterSongGeniusCommand      = new DelegateCommand(
            () => OpenUrl($"https://genius.com/search?q={Uri.EscapeDataString(_chapterArtist + " " + _chapterSong)}"),
            () => HasChapterSong);

        IdentifyCommand = new DelegateCommand(
            async () => await ExecuteIdentifyAsync(),
            () => CanControl && !IsIdentifying);

        SaveAcrSettingsCommand = new DelegateCommand(() =>
        {
            _acrCloudService.SaveSettings(new AcrCloudSettings(AcrHost, AcrKey, AcrSecret));
            IsAcrSettingsOpen = false;
            IdentifyCommand.RaiseCanExecuteChanged();
        });

        CloseAcrSettingsCommand = new DelegateCommand(() => IsAcrSettingsOpen = false);

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

        var urlSnapshot = _url;
        try
        {
            var info = await _playerService.PlayAsync(urlSnapshot);
            Log($"[Play] Metadata: \"{info.Title}\" by {info.Channel}, {info.DurationMs}ms, {info.Chapters.Count} chapters");

            VideoInfo = info;
            CurrentChapterTitle = string.Empty;

            // Seed duration from metadata so the seek bar is accurate before VLC's LengthChanged fires.
            if (info.DurationMs > 0)
                _duration = info.DurationMs;

            _historyService.Add(info.Title, urlSnapshot);
            HistoryEntries = [.._historyService.Entries];
            RaiseLookupCanExecuteChanged();

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

    // --- Artist lookup commands ---

    private void ExecuteOpenLastFm()
    {
        if (_videoInfo is null) return;
        var q = Uri.EscapeDataString($"{_videoInfo.Channel} {_videoInfo.Title}");
        OpenUrl($"https://www.last.fm/search?q={q}");
    }

    private void ExecuteOpenGenius()
    {
        if (_videoInfo is null) return;
        var q = Uri.EscapeDataString($"{_videoInfo.Title} {_videoInfo.Channel}");
        OpenUrl($"https://genius.com/search?q={q}");
    }

    private void ExecuteOpenWikipedia()
    {
        if (_videoInfo is null) return;
        var artist = Uri.EscapeDataString(_videoInfo.Channel);
        OpenUrl($"https://en.wikipedia.org/wiki/{artist}");
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void ParseChapterTitle(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            ChapterArtist = string.Empty;
            ChapterSong   = string.Empty;
            RaiseChapterCommandsCanExecuteChanged();
            return;
        }

        // Try common artist–song separators in order
        ReadOnlySpan<string> separators = [" - ", " – ", " — "];
        foreach (var sep in separators)
        {
            var idx = title.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0)
            {
                ChapterArtist = title[..idx].Trim();
                ChapterSong   = StripLabel(title[(idx + sep.Length)..].Trim());
                RaiseChapterCommandsCanExecuteChanged();
                return;
            }
        }

        // No separator — treat full title as the song, no artist
        ChapterArtist = string.Empty;
        ChapterSong   = StripLabel(title.Trim());
        RaiseChapterCommandsCanExecuteChanged();
    }

    // Strips trailing [Label] or (Label) from a song title.
    private static string StripLabel(string s)
    {
        var result = s;
        while (result.Length > 0 && result[^1] is ']' or ')')
        {
            var open  = result[^1] == ']' ? '[' : '(';
            var start = result.LastIndexOf(open);
            if (start <= 0) break;
            result = result[..start].TrimEnd();
        }
        return result;
    }

    // --- ACRCloud identify ---

    private async Task ExecuteIdentifyAsync()
    {
        if (!_acrCloudService.IsConfigured)
        {
            var s = _acrCloudService.Settings;
            AcrHost   = s?.Host          ?? string.Empty;
            AcrKey    = s?.AccessKey     ?? string.Empty;
            AcrSecret = s?.AccessSecret  ?? string.Empty;
            IsAcrSettingsOpen = true;
            return;
        }

        IsIdentifying = true;
        StatusMessage = "Identifying…";
        try
        {
            var result = await _acrCloudService.IdentifyAsync();
            StatusMessage = result.Found
                ? $"♫  {result.Artist} — {result.Title}  ({result.Album})"
                : "No match found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"ID error: {ex.Message}";
        }
        finally
        {
            IsIdentifying = false;
        }
    }

    private void RaiseChapterCommandsCanExecuteChanged()
    {
        OpenChapterArtistLastFmCommand.RaiseCanExecuteChanged();
        OpenChapterArtistWikipediaCommand.RaiseCanExecuteChanged();
        OpenChapterSongGeniusCommand.RaiseCanExecuteChanged();
    }

    // --- History helpers (called from code-behind) ---

    public void SelectHistoryEntry(HistoryEntry entry)
    {
        Url = entry.Url;
        IsHistoryOpen = false;
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
        else
        {
            CurrentChapterTitle = string.Empty;
        }
    }

    private void ResetPlayback()
    {
        _duration = 0;
        SliderPosition = 0;
        TimeDisplay = "0:00 / 0:00";
        CurrentChapterTitle = string.Empty;
        VideoInfo = null;
        RaiseLookupCanExecuteChanged();
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
