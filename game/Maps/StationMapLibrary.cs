using System.Collections.Generic;
using System.Linq;
using Godot;
using Godotstation.Engine.Maps;

namespace Godotstation.Game.Maps;

/// <summary>One map file the player can pick: bundled with the game or saved by the community.</summary>
public sealed record StationMapEntry(string DisplayName, string Path, bool Bundled)
{
    public string Label => Bundled ? $"{DisplayName}  ·  bundled" : $"{DisplayName}  ·  user";
}

/// <summary>Godot file access for station maps. Bundled maps ship in res://maps; authored maps live in user://maps.</summary>
public static class StationMapLibrary
{
    public const string BundledDirectory = "res://maps";
    public const string UserDirectory = "user://maps";

    public static string UserPathFor(string stem) => $"{UserDirectory}/{StationMaps.ToFileStem(stem)}{StationMaps.FileExtension}";

    public static string DefaultBundledPath => $"{BundledDirectory}/{StationMaps.DefaultMapFileName}{StationMaps.FileExtension}";

    /// <summary>Every readable map, bundled first, each sorted by display name.</summary>
    public static IReadOnlyList<StationMapEntry> List()
    {
        var entries = new List<StationMapEntry>();
        entries.AddRange(ListDirectory(BundledDirectory, bundled: true));
        entries.AddRange(ListDirectory(UserDirectory, bundled: false));
        return entries;
    }

    private static IEnumerable<StationMapEntry> ListDirectory(string directory, bool bundled)
    {
        if (!DirAccess.DirExistsAbsolute(directory)) return Enumerable.Empty<StationMapEntry>();
        var entries = new List<StationMapEntry>();
        foreach (string file in DirAccess.GetFilesAt(directory))
        {
            if (!file.EndsWith(StationMaps.FileExtension, System.StringComparison.OrdinalIgnoreCase)) continue;
            string path = $"{directory}/{file}";
            if (!TryLoad(path, out StationMap map, out _)) continue;
            entries.Add(new StationMapEntry(map.Name, path, bundled));
        }
        return entries.OrderBy(entry => entry.DisplayName, System.StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryLoad(string path, out StationMap map, out string error)
    {
        map = null!;
        if (string.IsNullOrWhiteSpace(path) || !FileAccess.FileExists(path))
        {
            error = $"Map file not found: {path}";
            return false;
        }
        string text = FileAccess.GetFileAsString(path);
        Error openError = FileAccess.GetOpenError();
        if (openError != Error.Ok)
        {
            error = $"Could not read {path}: {openError}";
            return false;
        }
        return StationMapJson.TryParse(text, out map, out error);
    }

    /// <summary>Writes the map to user://maps under a file-safe stem and reports the resulting path.</summary>
    public static bool TrySave(StationMap map, string stem, out string path, out string error)
    {
        path = UserPathFor(stem);
        error = string.Empty;
        Error makeDir = DirAccess.MakeDirRecursiveAbsolute(UserDirectory);
        if (makeDir != Error.Ok && makeDir != Error.AlreadyExists)
        {
            error = $"Could not create {UserDirectory}: {makeDir}";
            return false;
        }
        using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            error = $"Could not write {path}: {FileAccess.GetOpenError()}";
            return false;
        }
        file.StoreString(StationMapJson.Serialize(map));
        return true;
    }

    /// <summary>
    /// The map the host should run: an in-memory map handed over by the editor, then the
    /// launch path, then the built-in default. Unplayable maps fall back to the default so a
    /// bad file never strands the host on a blank screen; <paramref name="note"/> explains why.
    /// </summary>
    public static StationMap ResolveLaunchMap(out string source, out string note)
    {
        note = string.Empty;
        if (Playtest.PlaytestLaunchOptions.Map is { } handedOver)
        {
            source = "editor";
            if (StationMapValidator.IsPlayable(handedOver)) return handedOver;
            note = "The edited map has validation errors; using the default station instead.";
            return StationMaps.CreateDefault();
        }
        string? path = Playtest.PlaytestLaunchOptions.MapPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            source = "default";
            return StationMaps.CreateDefault();
        }
        source = path;
        if (!TryLoad(path, out StationMap map, out string error))
        {
            note = $"{error} Using the default station instead.";
            return StationMaps.CreateDefault();
        }
        StationMapProblem? firstError = StationMapValidator.Validate(map).FirstOrDefault(problem => problem.IsError);
        if (firstError != null)
        {
            note = $"{path} is not playable ({firstError}). Using the default station instead.";
            return StationMaps.CreateDefault();
        }
        return map;
    }
}
