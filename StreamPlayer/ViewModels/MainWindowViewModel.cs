using LibVLCSharp.Shared;
using Prism.Commands;
using Prism.Mvvm;
using StreamPlayer.Services.Interfaces;

namespace StreamPlayer.ViewModels;

public class MainWindowViewModel : BindableBase
{
    private readonly IPlayerService _playerService;
    private string _url = string.Empty;
    private string _validationMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isValidUrl;
    private bool _isLoading;

    private static readonly HashSet<string> YouTubeHosts =
    [
        "youtube.com",
        "www.youtube.com",
        "youtu.be",
        "m.youtube.com"
    ];

    public string Url
    {
        get => _url;
        set
        {
            SetProperty(ref _url, value);
            ValidateUrl();
        }
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
        private set
        {
            SetProperty(ref _isValidUrl, value);
            PlayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            SetProperty(ref _isLoading, value);
            PlayCommand.RaiseCanExecuteChanged();
        }
    }

    public MediaPlayer MediaPlayer => _playerService.MediaPlayer;

    public DelegateCommand PlayCommand { get; }

    public MainWindowViewModel(IPlayerService playerService)
    {
        _playerService = playerService;
        PlayCommand = new DelegateCommand(async () => await ExecutePlayAsync(), () => IsValidUrl && !IsLoading);
    }

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

    private async Task ExecutePlayAsync()
    {
        IsLoading = true;
        StatusMessage = "Resolving stream…";

        try
        {
            await _playerService.PlayAsync(_url);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
