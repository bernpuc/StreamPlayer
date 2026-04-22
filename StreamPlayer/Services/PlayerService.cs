using LibVLCSharp.Shared;
using StreamPlayer.Models;
using StreamPlayer.Services.Interfaces;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace StreamPlayer.Services;

public sealed class PlayerService : IPlayerService, IDisposable
{
    private readonly LibVLC _libVlc;

    public MediaPlayer MediaPlayer { get; }

    public PlayerService()
    {
        Core.Initialize();
        _libVlc = new LibVLC("--no-disable-screensaver");
        MediaPlayer = new MediaPlayer(_libVlc);
    }

    public async Task<VideoInfo> PlayAsync(string url)
    {
        var (streamUrls, info) = await FetchInfoAsync(url);

        var media = new Media(_libVlc, new Uri(streamUrls[0]));
        if (streamUrls.Length >= 2)
            media.AddOption($":input-slave={streamUrls[1]}");

        MediaPlayer.Play(media);
        media.Dispose();

        return info;
    }

    private static readonly string CookiesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StreamPlayer", "cookies.txt");

    private static async Task<(string[] StreamUrls, VideoInfo Info)> FetchInfoAsync(string url)
    {
        var cookiesArg = File.Exists(CookiesPath) ? $"--cookies \"{CookiesPath}\"" : string.Empty;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"--js-runtimes node --remote-components ejs:github {cookiesArg} -f \"bestvideo[height<=1080]+bestaudio/best\" --dump-json --no-playlist \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask  = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error  = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("yt-dlp returned no output.");

        return ParseYtDlpJson(output.Trim());
    }

    private static (string[] StreamUrls, VideoInfo Info) ParseYtDlpJson(string json)
    {
        using var doc  = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // --- Metadata ---
        var title   = root.TryGetProperty("title",   out var t) ? t.GetString() ?? "" : "";
        var channel = root.TryGetProperty("channel", out var c) ? c.GetString() ?? ""
                    : root.TryGetProperty("uploader", out var u) ? u.GetString() ?? "" : "";
        var thumb   = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : "";
        var durMs   = root.TryGetProperty("duration", out var d)
                    ? (long)(d.GetDouble() * 1000) : 0L;

        var chapters = new List<ChapterInfo>();
        if (root.TryGetProperty("chapters", out var chapArr) &&
            chapArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var ch in chapArr.EnumerateArray())
            {
                var chTitle = ch.TryGetProperty("title",      out var ct) ? ct.GetString() ?? "" : "";
                var startMs = ch.TryGetProperty("start_time", out var cs) ? (long)(cs.GetDouble() * 1000) : 0L;
                var endMs   = ch.TryGetProperty("end_time",   out var ce) ? (long)(ce.GetDouble() * 1000) : durMs;
                chapters.Add(new ChapterInfo(chTitle, startMs, endMs));
            }
        }

        var info = new VideoInfo(title, channel, thumb, durMs, chapters);

        // --- Stream URLs ---
        string[] streamUrls;

        // DASH: requested_formats has separate video + audio entries
        if (root.TryGetProperty("requested_formats", out var fmts) &&
            fmts.ValueKind == JsonValueKind.Array)
        {
            streamUrls = fmts.EnumerateArray()
                .Select(f => f.TryGetProperty("url", out var fu) ? fu.GetString() : null)
                .Where(u => u != null)
                .Select(u => u!)
                .ToArray();
        }
        // Single progressive stream
        else if (root.TryGetProperty("url", out var urlProp) &&
                 urlProp.GetString() is { Length: > 0 } singleUrl)
        {
            streamUrls = [singleUrl];
        }
        else
        {
            throw new InvalidOperationException("yt-dlp JSON contained no usable stream URLs.");
        }

        if (streamUrls.Length == 0)
            throw new InvalidOperationException("yt-dlp returned no stream URLs.");

        return (streamUrls, info);
    }

    public async Task<IReadOnlyList<PlaylistEntry>> GetPlaylistEntriesAsync(string url)
    {
        var cookiesArg = File.Exists(CookiesPath) ? $"--cookies \"{CookiesPath}\"" : string.Empty;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "yt-dlp",
                Arguments = $"--js-runtimes node --remote-components ejs:github {cookiesArg} --flat-playlist --yes-playlist --dump-json \"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask  = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error  = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim());

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("yt-dlp returned no playlist data.");

        var entries = new List<PlaylistEntry>();
        var index = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? $"Track {index + 1}" : $"Track {index + 1}";
                string? entryUrl = null;
                if (root.TryGetProperty("url", out var u)) entryUrl = u.GetString();
                if (entryUrl is null && root.TryGetProperty("webpage_url", out var wu)) entryUrl = wu.GetString();
                if (entryUrl is null && root.TryGetProperty("id", out var id) && id.GetString() is { } videoId)
                    entryUrl = $"https://www.youtube.com/watch?v={videoId}";
                if (entryUrl is not null)
                    entries.Add(new PlaylistEntry(index++, title, entryUrl));
            }
            catch (JsonException) { }
        }

        return entries;
    }

    public void Dispose()
    {
        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }
}
