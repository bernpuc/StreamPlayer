using StreamPlayer.Models;

namespace StreamPlayer.Services.Interfaces;

public interface IHistoryService
{
    IReadOnlyList<HistoryEntry> Entries { get; }
    void Add(string title, string url);
}
