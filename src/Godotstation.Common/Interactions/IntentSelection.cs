using System;

namespace Godotstation.Common.Interactions
{
	/// <summary>
	/// The local player's selected intent. Port of the selection half of
	/// UIManager.CurrentIntent + ControlIntent: hotkeys 1-4 pick Help / Disarm /
	/// Grab / Harm directly, G cycles forward and F cycles back, wrapping at the ends.
	/// Clicks read <see cref="Current"/> once at click time (FC-7), so changing the
	/// selection afterwards never rewrites an interaction already sent.
	/// </summary>
	public sealed class IntentSelection
	{
		private const int IntentCount = 4;

		public Intent Current { get; private set; } = Intent.Help;

		/// <summary>Raised with the new intent whenever the selection actually changes.</summary>
		public event Action<Intent>? Changed;

		/// <summary>Selects one of the four playable intents. None and unknown values are refused.</summary>
		public bool Select(Intent intent)
		{
			if (!IsSelectable(intent)) return false;
			if (intent == Current) return true;
			Current = intent;
			Changed?.Invoke(intent);
			return true;
		}

		/// <summary>Help → Disarm → Grab → Harm → Help (forward), or the reverse.</summary>
		public Intent Cycle(bool forward)
		{
			int next = ((int)Current + (forward ? 1 : IntentCount - 1)) % IntentCount;
			Select((Intent)next);
			return Current;
		}

		/// <summary>
		/// Applies an intent hotkey given as its character ('1'-'4', 'F', 'G', any case).
		/// Returns false for keys that are not intent hotkeys, leaving the selection alone.
		/// </summary>
		public bool TryApplyHotkey(char key)
		{
			switch (char.ToUpperInvariant(key))
			{
				case '1': return Select(Intent.Help);
				case '2': return Select(Intent.Disarm);
				case '3': return Select(Intent.Grab);
				case '4': return Select(Intent.Harm);
				case 'G': Cycle(forward: true); return true;
				case 'F': Cycle(forward: false); return true;
				default: return false;
			}
		}

		public static bool IsSelectable(Intent intent) =>
			intent is Intent.Help or Intent.Disarm or Intent.Grab or Intent.Harm;
	}
}
