using System.Collections.Generic;
using System.Linq;
using Godotstation.Engine.Entities;
using Godotstation.Engine.Health;
using Godotstation.Engine.Rounds;
using InventoryNamedSlot = Godotstation.Engine.Inventory.NamedSlot;

namespace Godotstation.Engine.Interactions;

public enum ExamineResult
{
    Ok,
    UnknownExaminer,
    /// <summary>Knocked-out crew cannot see.</summary>
    Unconscious,
    UnknownTarget,
    /// <summary>A wall stands between a living examiner and the target.</summary>
    OutOfView,
}

/// <summary>
/// Shift-click examine, after tgstation's <c>atom/ShiftClick</c> → <c>examinate</c> → <c>examine</c>
/// at commit f37d36c. Examining needs sight of the target, not reach or free hands.
/// </summary>
public sealed partial class SharedStationInventoryWorld
{
    /// <summary>Descriptions for object kinds. The wrench and bruise pack texts are tgstation's <c>desc</c>.</summary>
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [StationEntityKind.Wrench] = "A wrench with common uses. Can be found in your hand.",
        [StationEntityKind.BruisePack] = "A therapeutic gel pack and bandages designed to treat blunt-force trauma.",
        [StationEntityKind.MetalSheets] = "Sheets of metal, used to plate girders into walls.",
        [StationEntityKind.GeneratorFuel] = "Solid fuel for a portable generator.",
        [StationEntityKind.ServiceCart] = "A wheeled cart. A wrench anchors it to the floor or frees it.",
        [StationEntityKind.Girder] = "A structural frame for a wall.",
        [StationEntityKind.EngineeringAirlock] = "An airlock that opens for Engineering ID access.",
        [StationEntityKind.FuelGenerator] = "A portable generator that burns solid fuel to charge power storage.",
        [StationEntityKind.PowerStorage] = "A power storage unit. Its output feeds the APC.",
        [StationEntityKind.ApcLamp] = "A lamp fed by the area power controller.",
    };

    /// <summary>
    /// Describes a target to one examiner. The living need an unobstructed line to the target;
    /// the dead see through walls; knocked-out crew see nothing.
    /// </summary>
    public ExamineResult TryExamine(int examinerPeerId, int targetId, out IReadOnlyList<string> lines)
    {
        lines = System.Array.Empty<string>();
        if (!crew.TryGetValue(examinerPeerId, out CrewMember? examiner)) return ExamineResult.UnknownExaminer;
        if (examiner.Health.Stat == CrewStat.HardCrit) return ExamineResult.Unconscious;
        if (!entities.TryGetValue(targetId, out StationEntity? target) ||
            target is ItemEntity { ItemSlot: not null } or ItemEntity { IsInWorld: false })
            return ExamineResult.UnknownTarget;
        if (!examiner.Health.IsDead && targetId != examinerPeerId &&
            !reachabilityMap.IsPassableForClick(examiner.Position, target.WorldPosition, true, target))
            return ExamineResult.OutOfView;

        var text = new List<string>();
        if (target is CrewBody body) DescribeCrew(body.Member, text);
        else DescribeObject(target, text);
        lines = text.AsReadOnly();
        return ExamineResult.Ok;
    }

    private static string JobName(int jobId) =>
        StationCrewJobCatalog.Jobs.FirstOrDefault(job => job.Id == jobId)?.Name ?? "Crew member";

    private static void DescribeCrew(CrewMember member, List<string> text)
    {
        text.Add($"This is Crew {member.PeerId}, {JobName(member.JobId)}.");
        foreach ((InventoryNamedSlot slot, string hand) in new[]
                 {
                     (InventoryNamedSlot.leftHand, "left"), (InventoryNamedSlot.rightHand, "right"),
                 })
            if (member.Inventory.GetNamedItemSlot(slot)?.Item is { } held)
                text.Add($"They are holding {WithArticle(held.Name)} in their {hand} hand.");

        double brute = member.Health.BruteLoss;
        if (brute > 0)
            text.Add(brute < 25 ? "They have minor bruising."
                : brute < 50 ? "They have moderate bruising!"
                : "They have severe bruising!");

        switch (member.Health.Stat)
        {
            case CrewStat.Dead:
                text.Add(member.GhostPosition.HasValue
                    ? "They are limp and unresponsive; there are no signs of life and their soul has departed..."
                    : "They are limp and unresponsive; there are no signs of life...");
                break;
            case CrewStat.HardCrit:
                text.Add("They aren't responding to anything around them and seem to be asleep.");
                break;
            case CrewStat.SoftCrit:
                text.Add("They are barely conscious.");
                break;
        }
    }

    private void DescribeObject(StationEntity target, List<string> text)
    {
        text.Add($"That's {WithArticle(target.Name)}.");
        if (Descriptions.TryGetValue(target.Kind, out string? description)) text.Add(description);
        switch (target)
        {
            case ItemEntity { Stackable: { } stack } item:
                string piece = item.Kind == StationEntityKind.GeneratorFuel ? "unit of generator fuel"
                    : item.Name.EndsWith('s') ? item.Name[..^1] : item.Name;
                string pieces = item.Kind == StationEntityKind.GeneratorFuel ? "units of generator fuel" : piece + "s";
                text.Add(stack.Amount > 1
                    ? $"There are {stack.Amount} {pieces} in the stack."
                    : $"There is {stack.Amount} {piece} in the stack.");
                break;
            case ServiceCart cart:
                text.Add(cart.Anchored ? "It is anchored to the floor." : "It is unanchored and can be pushed.");
                break;
            case Girder girder:
                text.Add(girder.Plated ? "It has been plated into a wall."
                    : girder.Task.WorkerPeerId.HasValue ? "Someone is plating it."
                    : "It is bare. Two metal sheets would plate it.");
                break;
            case AccessDoor door:
                text.Add(door.IsOpen ? "It is open." : "It is closed.");
                break;
            case PowerNetworkEntity power:
                text.Add(power.Kind switch
                {
                    StationEntityKind.FuelGenerator => power.Loop.GeneratorRunning
                        ? $"It is running, with about {power.Loop.FuelSeconds:0} seconds of fuel left."
                        : power.Loop.FuelSeconds > 0 ? "It is off, with fuel loaded." : "It is off and has no fuel.",
                    StationEntityKind.PowerStorage =>
                        $"Its output is {(power.Loop.OutputEnabled ? "on" : "off")} and it holds {power.Loop.StoredEnergy:0.0} units of charge.",
                    _ => power.Loop.DevicePowered ? "It is lit." : "It is dark.",
                });
                break;
        }
    }

    private static string WithArticle(string name) =>
        name.EndsWith('s') || name.StartsWith("generator fuel", System.StringComparison.Ordinal)
            ? $"some {name}"
            : "aeiouAEIOU".Contains(name[0]) ? $"an {name}" : $"a {name}";
}
