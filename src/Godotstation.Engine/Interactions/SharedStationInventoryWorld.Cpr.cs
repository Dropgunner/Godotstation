using System;
using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Health;

namespace Godotstation.Engine.Interactions;

/// <summary>Help-intent, empty-hand CPR on a critical crew member. See <see cref="CprAttempt"/>.</summary>
public sealed partial class SharedStationInventoryWorld
{
    /// <summary>Raised when a rescuer starts CPR (rescuer, patient).</summary>
    public event Action<int, int>? CprStarted;

    /// <summary>Raised each time a round of CPR completes (rescuer, patient, oxygen loss healed).</summary>
    public event Action<int, int, double>? CprPerformed;

    private void AdvanceCpr(CrewMember rescuer, double elapsedSeconds)
    {
        CprAttempt cpr = rescuer.Cpr;
        if (cpr.PatientPeerId is not int patientId) return;
        bool hasPatient = crew.TryGetValue(patientId, out CrewMember? patient);
        bool valid = hasPatient && rescuer.Health.CanUseHands && rescuer.ActiveHandSlot.Item == null &&
                     !patient!.Health.IsDead && IsReachable(rescuer, patient.Body!) &&
                     Vector2.Distance(rescuer.Position, cpr.StartPosition) <= CprAttempt.HoldStillTolerance;
        if (!cpr.Advance(elapsedSeconds, valid))
        {
            if (!cpr.PatientPeerId.HasValue) PublishChanges();
            return;
        }

        // The source checks again after the wait: a patient who recovered meanwhile gets nothing.
        if (!CprAttempt.CanReceive(patient!.Health))
        {
            cpr.Cancel();
            PublishChanges();
            return;
        }

        CrewStat before = patient.Health.Stat;
        double healed = patient.Health.HealOxy(CprAttempt.OxyHealed);
        NotifyStat(patient, before);
        CprPerformed?.Invoke(rescuer.PeerId, patient.PeerId, healed);
        if (CprAttempt.CanReceive(patient.Health)) cpr.Continue(rescuer.Position);
        else cpr.Cancel();
        PublishChanges();
    }

    private sealed class CprHandler(SharedStationInventoryWorld world, CrewBody owner)
        : IOwnedInteractionHandler<HandApply>
    {
        public WorldEntity Owner { get; } = owner;
        public bool IsFirstInteractable => false;
        public int Priority => 0;

        public bool WillInteract(HandApply interaction, NetworkSide side) =>
            side == NetworkSide.Server && interaction.Performer != null && interaction.TargetObject == Owner &&
            interaction.Intent == Intent.Help && interaction.HandObject == null &&
            interaction.Performer.Id != owner.Member.PeerId &&
            world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? rescuer) &&
            !rescuer.Cpr.PatientPeerId.HasValue && CprAttempt.CanReceive(owner.Member.Health);

        public void ServerPerformInteraction(HandApply interaction)
        {
            if (interaction.Performer == null ||
                !world.crew.TryGetValue(interaction.Performer.Id, out CrewMember? rescuer)) return;
            if (rescuer.Cpr.TryStart(rescuer.PeerId, owner.Member.PeerId, owner.Member.Health, rescuer.Position))
                world.CprStarted?.Invoke(rescuer.PeerId, owner.Member.PeerId);
        }
    }
}
