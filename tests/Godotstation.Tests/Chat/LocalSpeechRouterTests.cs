using System.Collections.Generic;
using System.Numerics;
using Godotstation.Engine.Chat;
using Xunit;

namespace Godotstation.Tests.Chat;

public sealed class LocalSpeechRouterTests
{
    [Fact]
    public void LocalSpeechUsesListenerRangeAndIncludesSpeaker()
    {
        var listeners = new[]
        {
            new LocalChatListener(1, Vector2.Zero),
            new LocalChatListener(2, new Vector2(14, 0)),
            new LocalChatListener(3, new Vector2(14.01f, 0)),
            new LocalChatListener(4, new Vector2(2, 0), HearingRange: 1),
        };

        IReadOnlyList<int> recipients = LocalSpeechRouter.FindListeners(1, Vector2.Zero, listeners, (_, _) => true);

        Assert.Equal(new[] { 1, 2 }, recipients);
    }

    [Fact]
    public void WallsOccludeSpeechButSpeakerAlwaysReceivesTheirOwnMessage()
    {
        var listeners = new[]
        {
            new LocalChatListener(1, new Vector2(-2, 0)),
            new LocalChatListener(2, new Vector2(2, 0)),
        };
        var walls = new HashSet<(int X, int Y)> { (0, 0) };

        IReadOnlyList<int> recipients = LocalSpeechRouter.FindListeners(
            1, new Vector2(-2, 0), listeners,
            (from, to) => GridLineOfSight.IsClear(from, to, (x, y) => walls.Contains((x, y))));

        Assert.Equal(new[] { 1 }, recipients);
    }

    [Fact]
    public void OpenDoorwayPreservesLineOfSightAndInvalidListenersAreSkipped()
    {
        var listeners = new[]
        {
            new LocalChatListener(1, new Vector2(-2, 0)),
            new LocalChatListener(2, new Vector2(2, 0)),
            new LocalChatListener(0, Vector2.Zero),
            new LocalChatListener(2, Vector2.One),
        };
        var walls = new HashSet<(int X, int Y)> { (0, -1), (0, 1) };

        IReadOnlyList<int> recipients = LocalSpeechRouter.FindListeners(
            1, new Vector2(-2, 0), listeners,
            (from, to) => GridLineOfSight.IsClear(from, to, (x, y) => walls.Contains((x, y))));

        Assert.Equal(new[] { 1, 2 }, recipients);
    }
}
