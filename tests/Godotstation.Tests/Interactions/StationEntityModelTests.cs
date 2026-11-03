using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Interactions;
using Godotstation.Engine.Rounds;
using Xunit;

namespace Godotstation.Tests.Interactions;

public sealed class StationEntityModelTests
{
    [Fact]
    public void CodecRoundTripsEveryCatalogEntityExactly()
    {
        IReadOnlyList<StationEntityState> states = SharedStationInventoryWorld.InitialStates();

        Assert.True(StationEntityStateCodec.TryDecode(StationEntityStateCodec.Encode(states),
            out IReadOnlyList<StationEntityState> decoded));

        Assert.Equal(states, decoded);
        Assert.Equal(StationEntityCatalog.Playtest.Select(spawn => spawn.Id), decoded.Select(state => state.Id));
    }

    [Fact]
    public void CodecRejectsTruncatedTrailingAndForeignPayloads()
    {
        byte[] payload = StationEntityStateCodec.Encode(SharedStationInventoryWorld.InitialStates());

        Assert.False(StationEntityStateCodec.TryDecode(payload[..^3], out _));
        Assert.False(StationEntityStateCodec.TryDecode([.. payload, 0], out _));
        Assert.False(StationEntityStateCodec.TryDecode([99, .. payload[1..]], out _));
        Assert.False(StationEntityStateCodec.TryDecode([], out _));
        Assert.False(StationEntityStateCodec.TryDecode(null, out _));
    }

    [Fact]
    public void PublishSendsOnlyEntitiesWhoseStateChanged()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(-1, 1)));
        var batches = new List<IReadOnlyList<StationEntityState>>();
        world.EntitiesChanged += batches.Add;

        world.PublishChanges();
        Assert.Empty(batches);

        Assert.True(world.TryInteract(1, StationEntityCatalog.WrenchId, Intent.Help, false));
        StationEntityState wrench = Assert.Single(Assert.Single(batches));
        Assert.Equal(StationEntityCatalog.WrenchId, wrench.Id);
        Assert.Equal(1, wrench.Holder);

        world.PublishChanges();
        Assert.Single(batches);
    }

    [Fact]
    public void ANewCatalogEntryOfAnExistingKindWorksWithoutAnyOtherChange()
    {
        const int spareWrenchId = 9101;
        const int secondCartId = 9102;
        StationEntitySpawn[] spawns =
        [
            .. StationEntityCatalog.Playtest,
            new(spareWrenchId, StationEntityKind.Wrench, new Vector2(4, 4)),
            new(secondCartId, StationEntityKind.ServiceCart, new Vector2(5, 4)),
        ];
        var world = new SharedStationInventoryWorld(spawns: spawns);
        Assert.True(world.AddActor(3, new Vector2(4, 3)));

        Assert.True(world.TryInteract(3, spareWrenchId, Intent.Help, false));
        Assert.True(world.TryInteract(3, secondCartId, Intent.Help, false));

        Assert.Equal(3, world.GetState(spareWrenchId)!.Holder);
        Assert.False(world.GetState(secondCartId)!.Flag(StationEntityField.Anchored));
        Assert.True(world.GetState(secondCartId)!.IsPushable);
        Assert.Null(world.GetState(StationEntityCatalog.WrenchId)!.Holder);
        Assert.True(world.GetState(StationEntityCatalog.ServiceCartId)!.Flag(StationEntityField.Anchored));
    }

    [Fact]
    public void UnknownKindsDuplicateIdsAndUnknownTargetsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SharedStationInventoryWorld(spawns:
            [new StationEntitySpawn(1, "teleporter", Vector2.Zero)]));
        Assert.Throws<ArgumentException>(() => new SharedStationInventoryWorld(spawns:
        [
            new StationEntitySpawn(1, StationEntityKind.Wrench, Vector2.Zero),
            new StationEntitySpawn(1, StationEntityKind.Girder, Vector2.One),
        ]));

        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(1, new Vector2(10, -6)));
        Assert.False(world.TryInteract(1, 123456, Intent.Help, false));
        Assert.False(world.TryInteract(1, StationEntityCatalog.ApcLampId, Intent.Help, false));
        Assert.Null(world.GetState(123456));
        Assert.False(world.SetEntityPosition(StationEntityCatalog.WrenchId, new Vector2(3, 3)));
    }

    [Fact]
    public void RoundEndReportReadsTrackedFactsFromEntityStates()
    {
        var world = new SharedStationInventoryWorld();
        Assert.True(world.AddActor(7, new Vector2(1, 1)));
        Assert.True(world.TryInteract(7, StationEntityCatalog.MetalSheetsId, Intent.Help, false));
        Assert.True(world.TryInteract(7, StationEntityCatalog.GirderId, Intent.Help, false));
        world.AdvanceConstruction(7, 4);

        RoundEndReport report = RoundEndReport.Create(1, 30, ["Assistant"], world.CaptureAll());

        Assert.True(report.GirderPlated);
        Assert.Equal(0, report.MetalSheetsRemaining);
        Assert.False(report.EngineeringAirlockOpen);
        Assert.False(report.GeneratorRunning);
        Assert.False(report.DevicePowered);
        Assert.Equal(0, report.StoredEnergy);
    }
}
