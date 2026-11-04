using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Xunit;

namespace Godotstation.Tests.Chat;

/// <summary>FC-SC-10 from planning/FidelityContract_SurvivalConsciousness.md.</summary>
public sealed class LocalSpeechRulesTests
{
    [Theory]
    [InlineData(ConsciousState.Conscious, true)]
    [InlineData(ConsciousState.BarelyConscious, true)]
    [InlineData(ConsciousState.Unconscious, false)]
    [InlineData(ConsciousState.Dead, false)]
    public void OnlyConsciousAndSoftCritCrewSpeak(ConsciousState state, bool canSpeak)
    {
        Assert.Equal(canSpeak, LocalSpeechRules.CanSpeakLocally(state));
        Assert.Equal(canSpeak, LocalSpeechRules.RefusalReason(state) == null);
    }

    [Fact]
    public void RefusalTextMatchesUnitystationCannotSpeakReason()
    {
        Assert.Equal("because you are dead.", LocalSpeechRules.RefusalReason(ConsciousState.Dead));
        Assert.Equal("because you're not conscious or lack the energy to speak.",
            LocalSpeechRules.RefusalReason(ConsciousState.Unconscious));
    }
}
