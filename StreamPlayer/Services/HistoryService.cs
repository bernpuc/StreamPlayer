using System.IO;
using System.Text.Json;
using StreamPlayer.Models;
using StreamPlayer.Services.Interfaces;

namespace StreamPlayer.Services;

public class HistoryService : IHistoryService
{
    private const int MaxEntries = 20;
    private readonly string _filePath;
    private readonly List<HistoryEntry> _entries = [];

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public HistoryService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StreamPlayer");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "history.json");
        Load();
    }

    public void Add(string title, string url)
    {
        _entries.RemoveAll(e => e.Url == url);
        _entries.Insert(0, new HistoryEntry(title, url));
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        Save();
    }

    public void Remove(string url)
    {
        _entries.RemoveAll(e => e.Url == url);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (list is not null) _entries.AddRange(list);
        }
        catch { /* ignore corrupt history file */ }
    }

    private void Save()
    {
        File.WriteAllText(_filePath,
            JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}
