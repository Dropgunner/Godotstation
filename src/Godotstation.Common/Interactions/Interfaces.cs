using System;
using System.Collections.Generic;
using System.Numerics;

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
		IReadOnlyList<StorageSlotData> GetStorageSlots(int itemId);

		/// <summary>Fired when a slot's content changes. May fire from the net thread — UI must marshal.</summary>
		event Action<int, SlotData> OnSlotChanged;
		/// <summary>Fired when an indexed storage slot changes. UI must marshal before touching nodes.</summary>
		event Action<int, StorageSlotData> OnStorageSlotChanged;
	}

	public interface ILobbyService
	{
		IReadOnlyList<JobData> GetAvailableJobs();
		IReadOnlyList<ServerInfo> GetServerHistory();
		void SelectJob(int jobId);
		void RequestSpectate();
		event Action<LobbyState> OnLobbyStateChanged;
	}

	/// <summary>Receives one engine-agnostic interaction type.</summary>
	public interface IInteractionHandler<in T> where T : Interaction
	{
		/// <summary>Lower values are checked first within a dispatch bucket.</summary>
		int Priority { get; }
		bool WillInteract(T interaction, NetworkSide side);
		void ServerPerformInteraction(T interaction);
	}

	/// <summary>A handler associated with either the held item or clicked entity.</summary>
	public interface IOwnedInteractionHandler<in T> : IInteractionHandler<T> where T : Interaction
	{
		WorldEntity Owner { get; }
		bool IsFirstInteractable { get; }
	}

	/// <summary>A frontend object with a stable backend identity for its lifetime.</summary>
	public interface IInteractable
	{
		string Id { get; }
		Vector2 WorldPosition { get; }
		WorldEntity InteractionEntity { get; }
	}

	/// <summary>A frontend player that supplies the existing headless player model at click time.</summary>
	public interface IPerformer
	{
		Vector2 WorldPosition { get; }
		bool IsGhosting { get; }
		bool HasTelekinesis { get; }
		bool IsAlive { get; }
		IInteractable? ActiveHandItem { get; }
		Intent CurrentIntent { get; }
		PlayerEntity InteractionPlayer { get; }
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
		string ItemDescription)
	{
		public int StackAmount { get; init; } = 1;
	}

	/// <summary>Presentation data for an indexed slot inside an item storage.</summary>
	public sealed record StorageSlotData(
		int SlotId,
		int Index,
		bool IsOccupied,
		int ItemId,
		string ItemName,
		string ItemIconPath,
		string ItemDescription)
	{
		public int StackAmount { get; init; } = 1;
	}

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
