using LibVLCSharp.Shared;
using StreamPlayer.Services.Interfaces;
using System.Diagnostics;

namespace StreamPlayer.Services;

public sealed class PlayerService : IPlayerService, IDisposable
{
    private readonly LibVLC _libVlc;

    public MediaPlayer MediaPlayer { get; }

    public PlayerService()
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        MediaPlayer = new MediaPlayer(_libVlc);
    }

    public async Task PlayAsync(string url)
    {
        var streamUrls = await ResolveStreamUrlsAsync(url);

        var media = new Media(_libVlc, new Uri(streamUrls[0]));
        if (streamUrls.Length >= 2)
            media.AddOption($":input-slave={streamUrls[1]}");

        MediaPlayer.Play(media);
        media.Dispose();
    }

    private static async Task<string[]> ResolveStreamUrlsAsync(string url)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"-f \"bestvideo[height<=1080]+bestaudio/best\" --get-url --no-playlist \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());

        var urls = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0)
            throw new InvalidOperationException("yt-dlp returned no stream URLs.");

        return urls;
    }

    public void Dispose()
    {
        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
