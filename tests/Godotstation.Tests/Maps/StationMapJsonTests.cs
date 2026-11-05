using System;
using System.IO;
using System.Linq;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Xunit;

namespace Godotstation.Tests.Maps;

public sealed class StationMapJsonTests
{
    [Fact]
    public void Serialize_RoundTripsTheDefaultMapAndIsStable()
    {
        StationMap map = StationMaps.CreateDefault();
        string json = StationMapJson.Serialize(map);
        StationMap parsed = StationMapJson.Parse(json);

        Assert.True(map.ContentEquals(parsed));
        Assert.Equal(json, StationMapJson.Serialize(parsed));
    }

    [Fact]
    public void Serialize_WritesTileRowsAsAsciiArt()
    {
        string json = StationMapJson.Serialize(StationMaps.CreateDefault());
        Assert.Contains("\"#########################\"", json);
        Assert.Contains("\"tiles\"", json);
        Assert.Contains("\"kind\": \"wrench\"", json);
    }

    [Fact]
    public void TryParse_ReadsAHandWrittenMap()
    {
        const string json = """
        {
          "format": 1,
          "name": "Closet",
          "bounds": { "minX": 0, "minY": 0, "maxX": 4, "maxY": 2 },
          "tiles": ["#####", "#...#", "#####"],
          "spawns": [[1, 1]],
          "rooms": [{ "name": "CLOSET", "minX": 1, "minY": 1, "maxX": 3, "maxY": 1 }],
          "entities": [{ "id": 7, "kind": "metal-sheets", "x": 3, "y": 1, "amount": 2 }]
        }
        """;

        Assert.True(StationMapJson.TryParse(json, out StationMap map, out string error), error);
        Assert.Equal("Closet", map.Name);
        Assert.Equal(12, map.WallCount);
        Assert.False(map.IsWall(2, 1));
        Assert.Single(map.Spawns);
        Assert.Equal(StationMaps.DefaultFloorColor, map.Rooms.Single().FloorColor);
        StationEntitySpawn entity = map.Entities.Single();
        Assert.Equal((7, StationEntityKind.MetalSheets, 2), (entity.Id, entity.Kind, entity.Amount));
        Assert.Empty(StationMapValidator.Validate(map));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "must be a JSON object")]
    [InlineData("""{"format": 2, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.#","###"]}""", "Unsupported map format")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":1,"maxY":1}, "tiles":["##","##"]}""", "Map sides")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.#"]}""", "exactly 3 rows")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.","###"]}""", "3 characters")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#?#","###"]}""", "unknown tile")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.#","###"], "spawns":[[9,9]]}""", "outside the map")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.#","###"], "entities":[{"id":1,"kind":"wrench","x":1,"y":1},{"id":1,"kind":"wrench","x":1,"y":1}]}""", "reuses an id")]
    [InlineData("""{"format": 1, "bounds": {"minX":0,"minY":0,"maxX":2,"maxY":2}, "tiles":["###","#.#","###"], "rooms":[{"minX":1,"minY":1,"maxX":1,"maxY":1,"floorColor":"red"}]}""", "hex")]
    public void TryParse_RejectsMalformedDocumentsWithoutThrowing(string json, string expectedError)
    {
        Assert.False(StationMapJson.TryParse(json, out _, out string error));
        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<FormatException>(() => StationMapJson.Parse(json));
    }

    [Fact]
    public void TryParse_RejectsOversizedDocuments()
    {
        string huge = "{" + new string(' ', StationMapJson.MaximumDocumentBytes) + "}";
        Assert.False(StationMapJson.TryParse(huge, out _, out string error));
        Assert.Contains("too large", error);
    }

    /// <summary>
    /// The bundled file is what players and the editor load; the code default is what every
    /// existing smoke assumes. Set GODOTSTATION_UPDATE_BUNDLED_MAPS=1 to rewrite the file.
    /// </summary>
    [Fact]
    public void BundledPlaytestMap_MatchesTheCodeDefault()
    {
        string path = Path.Combine(RepositoryRoot(), "game", "maps", StationMaps.DefaultMapFileName + StationMaps.FileExtension);
        string expected = StationMapJson.Serialize(StationMaps.CreateDefault());
        if (Environment.GetEnvironmentVariable("GODOTSTATION_UPDATE_BUNDLED_MAPS") == "1")
            File.WriteAllText(path, expected);

        Assert.True(File.Exists(path), $"Missing bundled map {path}");
        Assert.Equal(expected.ReplaceLineEndings(), File.ReadAllText(path).ReplaceLineEndings());
    }

    [Fact]
    public void BundledMaps_AllParseAndArePlayable()
    {
        string directory = Path.Combine(RepositoryRoot(), "game", "maps");
        string[] files = Directory.GetFiles(directory, "*" + StationMaps.FileExtension);
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            Assert.True(StationMapJson.TryParse(File.ReadAllText(file), out StationMap map, out string error), $"{file}: {error}");
            var errors = StationMapValidator.Validate(map).Where(problem => problem.IsError).ToList();
            Assert.True(errors.Count == 0, $"{file}: {string.Join("; ", errors)}");
        }
    }

    internal static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "Godotstation.slnx")))
            directory = Path.GetDirectoryName(directory);
        return directory ?? throw new InvalidOperationException("Could not locate the repository root from the test directory.");
    }
}
