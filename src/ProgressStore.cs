using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Game;

public sealed class ProgressStore
{
    private readonly string _path;
    private readonly Dictionary<int, int> _bestStars = [];

    public ProgressStore(string path)
    {
        _path = path;
        Load();
    }

    public int BestStars(int dungeonNumber) => _bestStars.GetValueOrDefault(dungeonNumber);

    public bool RecordCompletion(int dungeonNumber, int stars)
    {
        var clampedStars = Math.Clamp(stars, 1, 3);
        if (BestStars(dungeonNumber) >= clampedStars)
        {
            return false;
        }

        _bestStars[dungeonNumber] = clampedStars;
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(_bestStars));
        return true;
    }

    public static string DefaultPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("SILENT_LABYRINTH_SAVE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SilentLabyrinth",
            "progress.json");
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<int, int>>(File.ReadAllText(_path));
            if (loaded is null)
            {
                return;
            }

            foreach (var (dungeon, stars) in loaded)
            {
                _bestStars[dungeon] = Math.Clamp(stars, 0, 3);
            }
        }
        catch (JsonException)
        {
            // A corrupt optional save must never prevent the game from launching.
        }
    }
}
