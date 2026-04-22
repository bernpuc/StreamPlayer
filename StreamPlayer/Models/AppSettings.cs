namespace StreamPlayer.Models;

public enum VideoQuality { Low, High }

public record AppSettings(
    int Volume = 100,
    bool IsMuted = false,
    VideoQuality Quality = VideoQuality.Low,
    bool AudioOnly = false);
