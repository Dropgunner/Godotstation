using System;
using Godot;
using Godotstation.Engine.Rounds;

namespace Godotstation.Game.Round;

/// <summary>Godot adapter that forwards round inputs to the headless session.</summary>
public partial class RoundSessionServiceNode : Node
{
    public RoundSession Session { get; } = new(3, 90);
    public string OutcomeName => Session.Outcome.ToString();
    public int ObjectiveProgress => Session.ObjectiveProgress;
    public int ObjectiveActionsRequired => Session.ObjectiveActionsRequired;
    public double TimeRemainingSeconds => Session.TimeRemainingSeconds;
    public event Action? StateChanged;

    public void StartRound()
    {
        Session.Start();
        StateChanged?.Invoke();
    }

    public bool TryPerformObjectiveAction(string toolId)
    {
        bool changed = Session.TryPerformObjectiveAction(toolId);
        if (changed) StateChanged?.Invoke();
        return changed;
    }

    public void AdvanceTime(double elapsedSeconds)
    {
        var previousTime = Session.TimeRemainingSeconds;
        var previousOutcome = Session.Outcome;
        Session.AdvanceTime(elapsedSeconds);
        if (Session.TimeRemainingSeconds != previousTime || Session.Outcome != previousOutcome)
            StateChanged?.Invoke();
    }
}
