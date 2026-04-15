using StreamPlayer.Models;

namespace StreamPlayer.Services.Interfaces;

public interface IAcrCloudService
{
    bool IsConfigured { get; }
    AcrCloudSettings? Settings { get; }
    void SaveSettings(AcrCloudSettings settings);
    Task<AcrCloudResult> IdentifyAsync();
}
