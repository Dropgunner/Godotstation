using Godotstation.Common.Interactions;

namespace Godotstation.Engine.Chat;

/// <summary>
/// Whether a crew member's conscious state lets them transmit local speech (FC-SC-10).
/// Sources: tgstation f37d36c295c4bf991b72647281352f3bd471b335 code/modules/mob/living/living_say.dm
/// lines 146–153 (DEAD routes to deadchat, IS_UNCONSCIOUS drops the message, SOFT_CRIT speaks) and
/// Unitystation c911581e8354958d664b1e32087053ce43638790 Player/PlayerScript.cs lines 571–573
/// (a dead body may transmit only Ghost | OOC) plus Core/Chat/Chat.cs lines 375–394 (refusal text).
/// </summary>
public static class LocalSpeechRules
{
    /// <summary>Unitystation Chat.CannotSpeakReason text for an unconscious speaker.</summary>
    public const string UnconsciousReason = "because you're not conscious or lack the energy to speak.";

    /// <summary>Unitystation Chat.CannotSpeakReason text for a dead speaker.</summary>
    public const string DeadReason = "because you are dead.";

    /// <summary>Conscious and BarelyConscious (soft crit) crew speak; Unconscious and Dead crew do not.</summary>
    public static bool CanSpeakLocally(ConsciousState state) =>
        state is ConsciousState.Conscious or ConsciousState.BarelyConscious;

    /// <summary>The reason shown to a refused speaker, or null when speech is allowed.</summary>
    public static string? RefusalReason(ConsciousState state) => state switch
    {
        ConsciousState.Dead => DeadReason,
        ConsciousState.Unconscious => UnconsciousReason,
        _ => null,
    };
}
