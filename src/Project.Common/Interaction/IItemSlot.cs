namespace Godotstation.Common.Interaction;

/// <summary>
/// A slot in some item storage (hand, pocket, backpack slot...). Engine-agnostic stand-in for
/// US13's ItemSlot.
/// </summary>
public interface IItemSlot
{
	/// <summary>The item occupying the slot, or null if empty. US13: ItemSlot.ItemObject.</summary>
	IWorldEntity? Item { get; }

	/// <summary>
	/// The player at the root of the storage hierarchy this slot belongs to, or null if the
	/// storage is not carried by a player. US13: ItemSlot.RootPlayer().
	/// </summary>
	IPlayerEntity? RootPlayer { get; }

	/// <summary>
	/// True if this is a left/right hand slot. US13: SlotIdentifier.NamedSlot is leftHand or rightHand.
	/// </summary>
	bool IsHandSlot { get; }
}
