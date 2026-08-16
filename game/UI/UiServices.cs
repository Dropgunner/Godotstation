using Godotstation.Common.Interactions;

namespace Godotstation.Game.UI
{
	/// <summary>
	/// Injection seam between the Godot scene tree and the headless backend.
	/// The bootstrap autoload assigns these once at startup (real services in
	/// game, fakes in UI test scenes). UI scripts read, never write.
	///
	/// Deliberately a dumb static locator, not a DI container: Godot
	/// instantiates nodes from scenes so constructor injection doesn't exist,
	/// and two lines of locator beat a framework.
	/// </summary>
	public static class UiServices
	{
		public static ILobbyService Lobby { get; set; } = null!;
		public static IInteractionService Interactions { get; set; } = null!;
		public static IInventoryProvider Inventory { get; set; } = null!;

		/// <summary>NetId of the local player, set on spawn.</summary>
		public static int LocalPlayerId { get; set; }

		/// <summary>SlotId of the currently active hand, kept current by InventoryUI.</summary>
		public static int ActiveHandSlotId { get; set; } = -1;
	}
}
