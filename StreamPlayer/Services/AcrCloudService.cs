using NAudio.Wave;
using StreamPlayer.Models;
using StreamPlayer.Services.Interfaces;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StreamPlayer.Services;

public class AcrCloudService : IAcrCloudService
{
    private static readonly HttpClient Http = new();
    private readonly string _settingsPath;

    public AcrCloudSettings? Settings { get; private set; }
    public bool IsConfigured =>
        Settings is { Host.Length: > 0, AccessKey.Length: > 0, AccessSecret.Length: > 0 };

    public AcrCloudService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StreamPlayer");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "acrcloud.json");
        LoadSettings();
    }

    public void SaveSettings(AcrCloudSettings settings)
    {
        Settings = settings;
        File.WriteAllText(_settingsPath,
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Audio capture + identification ──────────────────────────────────────

    public async Task<AcrCloudResult> IdentifyAsync()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("ACRCloud is not configured.");

        var wavBytes = await CaptureSystemAudioAsync(durationMs: 8_000);
        return await PostToAcrCloudAsync(wavBytes);
    }

    private static async Task<byte[]> CaptureSystemAudioAsync(int durationMs)
    {
        using var capture = new WasapiLoopbackCapture();
        var capturedBytes = new List<byte>();
        var tcs = new TaskCompletionSource<bool>();

        capture.DataAvailable  += (_, e) => capturedBytes.AddRange(e.Buffer.Take(e.BytesRecorded));
        capture.RecordingStopped += (_, _) => tcs.TrySetResult(true);

        capture.StartRecording();
        await Task.Delay(durationMs);
        capture.StopRecording();
        await tcs.Task;

        return BuildWav(capturedBytes.ToArray(), capture.WaveFormat);
    }

    private static byte[] BuildWav(byte[] pcmData, WaveFormat fmt)
    {
        // AudioFormat: 1 = PCM integer, 3 = IEEE float
        short audioFmt = fmt.Encoding == WaveFormatEncoding.IeeeFloat ? (short)3 : (short)1;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        int dataLen    = pcmData.Length;
        int byteRate   = fmt.AverageBytesPerSecond;
        short blockAlign = (short)fmt.BlockAlign;

        // RIFF chunk
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataLen);            // chunk size
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt sub-chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);                      // sub-chunk size
        bw.Write(audioFmt);
        bw.Write((short)fmt.Channels);
        bw.Write(fmt.SampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)fmt.BitsPerSample);

        // data sub-chunk
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLen);
        bw.Write(pcmData);

        return ms.ToArray();
    }

    // ── ACRCloud HTTP call ───────────────────────────────────────────────────

    private async Task<AcrCloudResult> PostToAcrCloudAsync(byte[] wavBytes)
    {
        var s = Settings!;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var sigStr    = $"POST\n/v1/identify\n{s.AccessKey}\naudio\n1\n{timestamp}";
        var sig       = ComputeHmacSha1(sigStr, s.AccessSecret);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(s.AccessKey),    "access_key");
        content.Add(new StringContent("audio"),        "data_type");
        content.Add(new StringContent("1"),            "signature_version");
        content.Add(new StringContent(timestamp),      "timestamp");
        content.Add(new StringContent(sig),            "signature");
        content.Add(new StringContent(wavBytes.Length.ToString()), "sample_bytes");
        content.Add(new ByteArrayContent(wavBytes), "sample", "sample.wav");

        var url = $"http://{s.Host}/v1/identify";
        using var response = await Http.PostAsync(url, content);
        var json = await response.Content.ReadAsStringAsync();

        return ParseAcrResponse(json);
    }

    private static string ComputeHmacSha1(string message, string secret)
    {
        var keyBytes  = Encoding.UTF8.GetBytes(secret);
        var msgBytes  = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA1(keyBytes);
        return Convert.ToBase64String(hmac.ComputeHash(msgBytes));
    }

    private static AcrCloudResult ParseAcrResponse(string json)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root   = doc.RootElement;
            var status = root.GetProperty("status");

            if (status.GetProperty("code").GetInt32() != 0)
                return new AcrCloudResult(false, "", "", "", "");

            var music  = root.GetProperty("metadata").GetProperty("music");
            if (music.GetArrayLength() == 0)
                return new AcrCloudResult(false, "", "", "", "");

            var track  = music[0];
            var title  = track.TryGetProperty("title",   out var t) ? t.GetString() ?? "" : "";
            var album  = track.TryGetProperty("album",   out var al)
                       ? (al.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "") : "";
            var label  = track.TryGetProperty("label",   out var lb) ? lb.GetString() ?? "" : "";

            var artist = "";
            if (track.TryGetProperty("artists", out var artists) &&
                artists.GetArrayLength() > 0 &&
                artists[0].TryGetProperty("name", out var an2))
                artist = an2.GetString() ?? "";

            return new AcrCloudResult(true, artist, title, album, label);
        }
        catch
        {
            return new AcrCloudResult(false, "", "", "", "");
        }
    }

    // ── Settings I/O ────────────────────────────────────────────────────────

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath)) return;
        try
        {
            var json = File.ReadAllText(_settingsPath);
            Settings = JsonSerializer.Deserialize<AcrCloudSettings>(json);
        }
        catch { /* ignore corrupt settings */ }
    }
}
