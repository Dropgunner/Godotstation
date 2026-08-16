using System;
using System.Collections.Generic;

namespace Godotstation.Common.Interactions
{
	// =========================================================================
	// Service interfaces — verbatim from the B2 contract. The game/ layer
	// calls these; Common/Net implement them. UI never implements game rules.
	// =========================================================================

	public interface IInteractionService
	{
		void RequestHandApply(int performerId, int targetId, BodyPart targetPart, bool isAltClick);
		void RequestInventoryTransfer(int fromSlotId, int toSlotId);
		void RequestDrop(int slotId);
		void RequestPickup(int itemId);
	}

	public interface IInventoryProvider
	{
		IReadOnlyList<SlotData> GetPlayerSlots(int playerId);
		SlotData GetSlot(int slotId);

		/// <summary>Fired when a slot's content changes. May fire from the net thread — UI must marshal.</summary>
		event Action<int, SlotData> OnSlotChanged;
	}

	public interface ILobbyService
	{
		IReadOnlyList<JobData> GetAvailableJobs();
		IReadOnlyList<ServerInfo> GetServerHistory();
		void SelectJob(int jobId);
		void RequestSpectate();
		event Action<LobbyState> OnLobbyStateChanged;
	}

	// =========================================================================
	// Data types the interfaces traffic in. Pure data, engine-agnostic:
	// colors are hex strings, icons are resource paths — the frontend decides
	// what a "color" or "texture" is.
	// =========================================================================

	public enum BodyPart
	{
		Chest,
		Head,
		Eyes,
		Mouth,
		LeftArm,
		RightArm,
		LeftHand,
		RightHand,
		Groin,
		LeftLeg,
		RightLeg,
		LeftFoot,
		RightFoot
	}

	/// <summary>Mirrors Unitystation's NamedSlot set (DynamicItemStorage keys).</summary>
	public enum NamedSlot
	{
		LeftHand,
		RightHand,
		Head,
		Eyes,
		Mask,
		Ear,
		Neck,
		UnderSuit,
		Suit,
		SuitStorage,
		Back,
		Belt,
		Gloves,
		Shoes,
		Pocket1,
		Pocket2,
		Id
	}

	public enum LobbyState
	{
		ServerSelect,
		CharacterSelect,
		JobSelect,
		WaitingToSpawn,
		Disconnected
	}

	public sealed record SlotData(
		int SlotId,
		NamedSlot Named,
		bool IsOccupied,
		int ItemId,
		string ItemName,
		string ItemIconPath,
		string ItemDescription);

	public sealed record JobData(
		int JobId,
		string Name,
		string Department,
		string DepartmentColorHex,
		string Description,
		string IconPath,
		int SlotsOpen,
		int SlotsTotal,
		bool IsAvailable,
		string UnavailableReason);

	public sealed record ServerInfo(
		string Name,
		string Address,
		int Port,
		DateTime LastJoinedUtc,
		bool WasDisconnected);
}
