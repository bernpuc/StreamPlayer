namespace StreamPlayer.Models;

public record VideoInfo(
    string Title,
    string Channel,
    string ThumbnailUrl,
    long DurationMs,
    IReadOnlyList<ChapterInfo> Chapters
);

public record ChapterInfo(string Title, long StartMs, long EndMs);
