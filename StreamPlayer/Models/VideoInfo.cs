namespace StreamPlayer.Models;

public record VideoInfo(
    string Title,
    string Channel,
    string ThumbnailUrl,
    long DurationMs,
    IReadOnlyList<ChapterInfo> Chapters,
    int? VideoHeight = null,
    int? VideoFps = null,
    string? VideoCodec = null,
    int? AudioBitrateKbps = null,
    string? AudioCodec = null,
    int? AudioSampleRateHz = null
);

public record ChapterInfo(string Title, long StartMs, long EndMs);
