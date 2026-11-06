using System.Numerics;
using Godotstation.Common.Interactions;
using Godotstation.Engine.Chat;
using Xunit;

namespace Godotstation.Tests.Chat;

/// <summary>FC-LS-7..9: speech routing for dead and incapacitated crew.</summary>
public sealed class IncapacitatedSpeechTests
{
    private static readonly LocalChatListener[] Crew =
    {
        new(1, Vector2.Zero, State: ConsciousState.Dead),
        new(2, new Vector2(1, 0)),
        new(3, new Vector2(2, 0), State: ConsciousState.Dead),
        new(4, new Vector2(3, 0), State: ConsciousState.Unconscious),
    };

    [Fact]
    public void DeadSpeakerIsMovedToGhostChannelAndReachesOnlyDeadCrew()
    {
        SpeechRoute route = LocalSpeechRouter.Route(1, ConsciousState.Dead, Vector2.Zero, Crew, (_, _) => true);

        Assert.Equal(SpeechChannel.Ghost, route.Channel);
        Assert.Equal(new[] { 1, 3 }, route.Recipients);
    }

    [Fact]
    public void DeadSpeakerIsNotHeardByAdjacentLivingCrewEvenWithClearLine()
    {
        SpeechRoute route = LocalSpeechRouter.Route(1, ConsciousState.Dead, Vector2.Zero, Crew, (_, _) => true);

        Assert.DoesNotContain(2, route.Recipients);
        Assert.DoesNotContain(4, route.Recipients);
    }

    [Fact]
    public void DeadListenersDoNotReceiveLivingLocalSpeech()
    {
        SpeechRoute route = LocalSpeechRouter.Route(2, ConsciousState.Conscious, new Vector2(1, 0), Crew, (_, _) => true);

        Assert.Equal(SpeechChannel.Local, route.Channel);
        Assert.Equal(new[] { 2, 4 }, route.Recipients);
    }

    [Fact]
    public void UnconsciousSpeakerKeepsLocalRoutingAsUpstreamDoes()
    {
        SpeechRoute route = LocalSpeechRouter.Route(4, ConsciousState.Unconscious, new Vector2(3, 0), Crew, (_, _) => true);

        Assert.Equal(SpeechChannel.Local, route.Channel);
        Assert.Equal(new[] { 2, 4 }, route.Recipients);
    }

    [Fact]
    public void LivingSpeechStillRespectsRangeAndWalls()
    {
        var listeners = new[]
        {
            new LocalChatListener(1, Vector2.Zero),
            new LocalChatListener(2, new Vector2(20, 0)),
            new LocalChatListener(3, new Vector2(2, 0)),
        };

        SpeechRoute route = LocalSpeechRouter.Route(1, ConsciousState.Conscious, Vector2.Zero, listeners,
            (_, to) => to.X < 1.5f);

        Assert.Equal(new[] { 1 }, route.Recipients);
    }
}
