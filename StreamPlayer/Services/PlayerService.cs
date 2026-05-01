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
    public VideoQuality Quality { get; set; } = VideoQuality.Low;
    public bool AudioOnly { get; set; } = false;

    private string FormatString => AudioOnly
        ? "bestaudio[ext=webm]/bestaudio/best"
        : Quality == VideoQuality.High
            ? "bestvideo[height<=1080]+bestaudio"
            : "bestvideo[height<=480][fps<=30]+bestaudio[ext=webm]/bestvideo[height<=480]+bestaudio/best";

    public PlayerService()
    {
        Core.Initialize();
        _libVlc = new LibVLC(
            "--no-disable-screensaver",
            "--network-caching=8000",
            "--live-caching=8000",
            "--audio-resampler=soxr");
        MediaPlayer = new MediaPlayer(_libVlc);
    }

    public async Task<VideoInfo> PlayAsync(string url)
    {
        var (streamUrls, info) = await FetchInfoAsync(url);

        var media = new Media(_libVlc, new Uri(streamUrls[0]));
        if (streamUrls.Length >= 2 &&
            Uri.TryCreate(streamUrls[1], UriKind.Absolute, out var slaveUri) &&
            slaveUri.Scheme is "http" or "https")
            media.AddOption($":input-slave={streamUrls[1]}");

        MediaPlayer.Play(media);
        media.Dispose();

        return info;
    }

    private static readonly string CookiesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "StreamPlayer", "cookies.txt");

    private async Task<(string[] StreamUrls, VideoInfo Info)> FetchInfoAsync(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add("node");
        psi.ArgumentList.Add("--remote-components");
        psi.ArgumentList.Add("ejs:github");
        if (File.Exists(CookiesPath)) { psi.ArgumentList.Add("--cookies"); psi.ArgumentList.Add(CookiesPath); }
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(FormatString);
        psi.ArgumentList.Add("--dump-json");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add(url);

        using var process = new Process { StartInfo = psi };

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

        // --- Stream URLs + quality ---
        string[] streamUrls;
        int? videoHeight = null, videoFps = null, audioBitrateKbps = null, audioSampleRateHz = null;
        string? videoCodec = null, audioCodec = null;

        // DASH: requested_formats has separate video + audio entries
        if (root.TryGetProperty("requested_formats", out var fmts) &&
            fmts.ValueKind == JsonValueKind.Array)
        {
            var fmtList = fmts.EnumerateArray().ToList();
            streamUrls = fmtList
                .Select(f => f.TryGetProperty("url", out var fu) ? fu.GetString() : null)
                .Where(u => u != null).Select(u => u!).ToArray();

            var videoFmt = fmtList.FirstOrDefault(f =>
                f.TryGetProperty("vcodec", out var vc) && vc.GetString() is { } s && s != "none");
            var audioFmt = fmtList.FirstOrDefault(f =>
                f.TryGetProperty("acodec", out var ac) && ac.GetString() is { } s && s != "none");

            if (videoFmt.ValueKind != JsonValueKind.Undefined)
            {
                if (videoFmt.TryGetProperty("height", out var vh) && vh.ValueKind == JsonValueKind.Number) videoHeight = vh.GetInt32();
                if (videoFmt.TryGetProperty("fps",    out var vf) && vf.ValueKind == JsonValueKind.Number) videoFps    = (int)Math.Round(vf.GetDouble());
                if (videoFmt.TryGetProperty("vcodec", out var vc) && vc.ValueKind == JsonValueKind.String) videoCodec  = NormalizeCodec(vc.GetString());
            }
            if (audioFmt.ValueKind != JsonValueKind.Undefined)
            {
                if (audioFmt.TryGetProperty("abr",   out var ab) && ab.ValueKind == JsonValueKind.Number) audioBitrateKbps = (int)Math.Round(ab.GetDouble());
                if (audioFmt.TryGetProperty("acodec", out var ac) && ac.ValueKind == JsonValueKind.String) audioCodec       = NormalizeCodec(ac.GetString());
                if (audioFmt.TryGetProperty("asr",   out var sr) && sr.ValueKind == JsonValueKind.Number) audioSampleRateHz = sr.GetInt32();
            }
        }
        // Single progressive stream
        else if (root.TryGetProperty("url", out var urlProp) &&
                 urlProp.GetString() is { Length: > 0 } singleUrl)
        {
            streamUrls = [singleUrl];
            if (root.TryGetProperty("height", out var vh) && vh.ValueKind == JsonValueKind.Number) videoHeight = vh.GetInt32();
            if (root.TryGetProperty("fps",    out var vf) && vf.ValueKind == JsonValueKind.Number) videoFps    = (int)Math.Round(vf.GetDouble());
            if (root.TryGetProperty("vcodec", out var vc) && vc.ValueKind == JsonValueKind.String) videoCodec  = NormalizeCodec(vc.GetString());
            if (root.TryGetProperty("abr",    out var ab) && ab.ValueKind == JsonValueKind.Number) audioBitrateKbps = (int)Math.Round(ab.GetDouble());
            if (root.TryGetProperty("acodec", out var ac) && ac.ValueKind == JsonValueKind.String) audioCodec       = NormalizeCodec(ac.GetString());
            if (root.TryGetProperty("asr",    out var sr) && sr.ValueKind == JsonValueKind.Number) audioSampleRateHz = sr.GetInt32();
        }
        else
        {
            throw new InvalidOperationException("yt-dlp JSON contained no usable stream URLs.");
        }

        if (streamUrls.Length == 0)
            throw new InvalidOperationException("yt-dlp returned no stream URLs.");

        var info = new VideoInfo(title, channel, thumb, durMs, chapters,
            videoHeight, videoFps, videoCodec,
            audioBitrateKbps, audioCodec, audioSampleRateHz);

        return (streamUrls, info);
    }

    private static string? NormalizeCodec(string? codec)
    {
        if (string.IsNullOrEmpty(codec) || codec == "none") return null;
        if (codec.StartsWith("avc1",   StringComparison.OrdinalIgnoreCase)) return "H.264";
        if (codec.StartsWith("vp9",    StringComparison.OrdinalIgnoreCase)) return "VP9";
        if (codec.StartsWith("av01",   StringComparison.OrdinalIgnoreCase)) return "AV1";
        if (codec.StartsWith("hev1",   StringComparison.OrdinalIgnoreCase)) return "H.265";
        if (codec.StartsWith("hvc1",   StringComparison.OrdinalIgnoreCase)) return "H.265";
        if (codec.StartsWith("opus",   StringComparison.OrdinalIgnoreCase)) return "Opus";
        if (codec.StartsWith("mp4a",   StringComparison.OrdinalIgnoreCase)) return "AAC";
        if (codec.StartsWith("vorbis", StringComparison.OrdinalIgnoreCase)) return "Vorbis";
        return codec.Split('.')[0].ToUpperInvariant();
    }

    public async Task<IReadOnlyList<PlaylistEntry>> GetPlaylistEntriesAsync(string url)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--js-runtimes");
        psi.ArgumentList.Add("node");
        psi.ArgumentList.Add("--remote-components");
        psi.ArgumentList.Add("ejs:github");
        if (File.Exists(CookiesPath)) { psi.ArgumentList.Add("--cookies"); psi.ArgumentList.Add(CookiesPath); }
        psi.ArgumentList.Add("--flat-playlist");
        psi.ArgumentList.Add("--yes-playlist");
        psi.ArgumentList.Add("--dump-json");
        psi.ArgumentList.Add(url);

        using var process = new Process { StartInfo = psi };

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
