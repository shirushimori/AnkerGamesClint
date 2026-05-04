using System.IO;
using System.Text.Json;
using AnkerGamesClient.Models;

namespace AnkerGamesClient.Services;

public class LibraryService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _libraryFile;

    public LibraryService(string installPath)
    {
        _libraryFile = Path.Combine(installPath, "library.json");
    }

    public List<GameEntry> LoadAll()
    {
        EnsureFile();
        try
        {
            var json = File.ReadAllText(_libraryFile);
            return JsonSerializer.Deserialize<List<GameEntry>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void AddGame(GameEntry entry)
    {
        var list = LoadAll();
        // Avoid duplicates by name
        list.RemoveAll(g => g.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        list.Add(entry);
        Save(list);
    }

    public void RemoveGame(string name)
    {
        var list = LoadAll();
        list.RemoveAll(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Save(list);
    }

    private void Save(List<GameEntry> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_libraryFile)!);
        File.WriteAllText(_libraryFile, JsonSerializer.Serialize(list, JsonOpts));
    }

    private void EnsureFile()
    {
        if (!File.Exists(_libraryFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_libraryFile)!);
            File.WriteAllText(_libraryFile, "[]");
        }
    }
}
