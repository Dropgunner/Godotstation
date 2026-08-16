using System.Numerics;

namespace Godotstation.Common.Interactions
{
	// =========================================================================
	// Port of the InteractionV2 interaction hierarchy (US13.Core.Input_System.
	// InteractionV2.Interactions), stripped of GameObject/MonoBehaviour.
	//
	// Structural facts preserved from upstream, because they are load-bearing:
	//  * PositionalHandApply DERIVES from HandApply. The original dispatcher
	//    switches on exact type (typeof(T)); any runtime-type dispatch here must
	//    therefore test the positional case before the plain HandApply case.
	//  * Every field a client factory captures is captured AT CLICK TIME from
	//    the LocalPlayerContext and frozen into get-only properties (FC-7).
	//  * Each concrete type keeps its static Invalid sentinel, returned by
	//    ByLocalPlayer before any validation or network message (FC-5). Callers
	//    compare by reference, exactly like the original.
	//
	// Deliberately dropped (not contract-bearing): Mind/account plumbing,
	// CommonComponents caching, MouseDrop.ShadowWorldLocation, IsHighlight.
	// =========================================================================

	/// <summary>
	/// Base class containing the info on an attempt to perform an interaction —
	/// a performer and the object they are using (null for an empty hand).
	/// </summary>
	public abstract class Interaction
	{
		/// <summary>The player performing the interaction. Null only on Invalid sentinels.</summary>
		public PlayerEntity? Performer { get; }

		/// <summary>
		/// Object being used to perform the interaction. Hand apply: object in
		/// hand (null if empty hand). Inventory apply: object dragged from the
		/// slot. Activate: the object being activated. Mouse drop: the dragged object.
		/// </summary>
		public WorldEntity? UsedObject { get; }

		/// <summary>Intent of the player, frozen at creation (FC-6, FC-7).</summary>
		public Intent Intent { get; }

		protected Interaction(PlayerEntity? performer, WorldEntity? usedObject, Intent intent)
		{
			Performer = performer;
			UsedObject = usedObject;
			Intent = intent;
		}
	}

	/// <summary>An interaction performed upon a target. TargetObject null means open space.</summary>
	public abstract class TargetedInteraction : Interaction
	{
		public WorldEntity? TargetObject { get; }

		protected TargetedInteraction(PlayerEntity? performer, WorldEntity? usedObject, WorldEntity? targetObject, Intent intent)
			: base(performer, usedObject, intent)
		{
			TargetObject = targetObject;
		}
	}

	/// <summary>Targeted interaction which additionally targets a specific body zone.</summary>
	public abstract class BodyPartTargetedInteraction : TargetedInteraction
	{
		public BodyPartType TargetBodyPart { get; }

		protected BodyPartTargetedInteraction(PlayerEntity? performer, WorldEntity? usedObject, WorldEntity? targetObject,
			BodyPartType targetBodyPart, Intent intent)
			: base(performer, usedObject, targetObject, intent)
		{
			TargetBodyPart = targetBodyPart;
		}
	}

	/// <summary>
	/// A player clicked something in the game world: the object in their hand
	/// (or their empty hand) is applied to the target object.
	/// </summary>
	public class HandApply : BodyPartTargetedInteraction
	{
		/// <summary>
		/// The invalid sentinel (FC-5). ByLocalPlayer returns this — by reference —
		/// when the local player is ghosting; the pipeline is never entered.
		/// </summary>
		public static readonly HandApply Invalid =
			new HandApply(null, null, null, BodyPartType.None, null, Intent.Help, false);

		/// <summary>Object in the performer's active hand at click time. Null if the hand was empty.</summary>
		public WorldEntity? HandObject => UsedObject;

		/// <summary>The active hand slot at click time (frozen reference, FC-7).</summary>
		public ItemSlotRef? HandSlot { get; }

		/// <summary>True if the alt action key was held at click time (frozen, FC-7).</summary>
		public bool IsAltClick { get; }

		protected HandApply(PlayerEntity? performer, WorldEntity? handObject, WorldEntity? targetObject,
			BodyPartType targetBodyPart, ItemSlotRef? handSlot, Intent intent, bool isAltClick)
			: base(performer, handObject, targetObject, targetBodyPart, intent)
		{
			HandSlot = handSlot;
			IsAltClick = isAltClick;
		}

		/// <summary>
		/// Creates a HandApply performed by the local player targeting the given
		/// object. Reads the live UI/input state exactly once, now (FC-7).
		/// </summary>
		public static HandApply ByLocalPlayer(LocalPlayerContext local, WorldEntity? targetObject)
		{
			if (local.IsGhosting)
			{
				// Hand apply never works for a ghosting local player (FC-5).
				return Invalid;
			}

			return new HandApply(
				local.Player,
				local.ActiveHandSlot?.Item,
				targetObject,
				local.DamageZone,
				local.ActiveHandSlot,
				local.CurrentIntent,
				local.IsAltActionKeyPressed);
		}

		/// <summary>
		/// Server only: reconstruct the hand apply a client sent. The hand object
		/// and slot are the ones the message processing already resolved.
		/// </summary>
		public static HandApply ByClient(PlayerEntity clientPlayer, WorldEntity? handObject, WorldEntity? targetObject,
			BodyPartType targetBodyPart, ItemSlotRef? handSlot, Intent intent, bool isAltClick)
		{
			return new HandApply(clientPlayer, handObject, targetObject, targetBodyPart, handSlot, intent, isAltClick);
		}
	}

	/// <summary>
	/// HandApply carrying the exact position the player clicked — used by objects
	/// with position-dependent logic (tiles) and for clicks on open space.
	/// NOTE: derives from HandApply; dispatchers must match it BEFORE HandApply.
	/// In this headless model TargetPosition is a WORLD position — matrix-local
	/// conversion (moving stations) is the map layer's concern, not validation's.
	/// </summary>
	public sealed class PositionalHandApply : HandApply
	{
		public static new readonly PositionalHandApply Invalid =
			new PositionalHandApply(null, null, null, Vector2.Zero, null, Intent.Help, BodyPartType.None, false);

		/// <summary>World position the performer is pointing at, frozen at click time.</summary>
		public Vector2 TargetPosition { get; }

		private PositionalHandApply(PlayerEntity? performer, WorldEntity? handObject, WorldEntity? targetObject,
			Vector2 targetPosition, ItemSlotRef? handSlot, Intent intent, BodyPartType targetBodyPart, bool isAltClick)
			: base(performer, handObject, targetObject, targetBodyPart, handSlot, intent, isAltClick)
		{
			TargetPosition = targetPosition;
		}

		/// <summary>
		/// Creates a PositionalHandApply by the local player. Faithful guard: the
		/// original bails to Invalid when the player has no DynamicItemStorage.
		/// </summary>
		public static PositionalHandApply ByLocalPlayer(LocalPlayerContext local, WorldEntity? targetObject, Vector2 targetPosition)
		{
			if (local.HasItemStorage == false) return Invalid;

			return new PositionalHandApply(
				local.Player,
				local.ActiveHandSlot?.Item,
				targetObject,
				targetPosition,
				local.ActiveHandSlot,
				local.CurrentIntent,
				local.DamageZone,
				local.IsAltActionKeyPressed);
		}

		/// <summary>Server only: reconstruct the positional hand apply a client sent.</summary>
		public static PositionalHandApply ByClient(PlayerEntity clientPlayer, WorldEntity? handObject, WorldEntity? targetObject,
			Vector2 targetPosition, ItemSlotRef? handSlot, Intent intent, BodyPartType targetBodyPart, bool isAltClick)
		{
			return new PositionalHandApply(clientPlayer, handObject, targetObject, targetPosition, handSlot, intent, targetBodyPart, isAltClick);
		}
	}

	/// <summary>
	/// Activating the item in the active hand (Activate key, or clicking the held
	/// item). Validation is actor-state only — no reach check whatsoever (FC-4).
	/// </summary>
	public sealed class HandActivate : Interaction
	{
		public static readonly HandActivate Invalid = new HandActivate(null, null, null, Intent.Help, false);

		/// <summary>Hand slot being activated (frozen at creation).</summary>
		public ItemSlotRef? HandSlot { get; }

		public bool IsAltClick { get; }

		private HandActivate(PlayerEntity? performer, WorldEntity? activatedObject, ItemSlotRef? handSlot, Intent intent, bool isAltClick)
			: base(performer, activatedObject, intent)
		{
			HandSlot = handSlot;
			IsAltClick = isAltClick;
		}

		/// <summary>
		/// Local player activates their active hand item. Faithful guard: the
		/// original bails unless the local player IsNormal.
		/// </summary>
		public static HandActivate ByLocalPlayer(LocalPlayerContext local)
		{
			if (local.Player.PlayerType != PlayerTypes.Normal)
			{
				return Invalid;
			}

			return new HandActivate(
				local.Player,
				local.ActiveHandSlot?.Item,
				local.ActiveHandSlot,
				local.CurrentIntent,
				local.IsAltActionKeyPressed);
		}

		/// <summary>Server only: reconstruct the activate a client sent.</summary>
		public static HandActivate ByClient(PlayerEntity clientPlayer, WorldEntity? activatedObject, ItemSlotRef? handSlot, Intent intent, bool isAltClick)
		{
			return new HandActivate(clientPlayer, activatedObject, handSlot, intent, isAltClick);
		}
	}

	/// <summary>
	/// Clicking an inventory slot or dragging slot-to-slot. Like HandActivate,
	/// validation is actor-state only — no reach or position check (FC-4).
	/// </summary>
	public sealed class InventoryApply : TargetedInteraction
	{
		public static readonly InventoryApply Invalid = new InventoryApply(null, null, null, Intent.Help, false);

		/// <summary>Hand slot used for the apply, or the slot dragged from.</summary>
		public ItemSlotRef? FromSlot { get; }

		/// <summary>Slot whose content the used object is being applied to.</summary>
		public ItemSlotRef? TargetSlot { get; }

		public bool IsAltClick { get; }

		private InventoryApply(PlayerEntity? performer, ItemSlotRef? targetSlot, ItemSlotRef? fromSlot, Intent intent, bool isAltClick)
			: base(performer, fromSlot?.Item, targetSlot?.Item, intent)
		{
			FromSlot = fromSlot;
			TargetSlot = targetSlot;
			IsAltClick = isAltClick;
		}

		/// <summary>Local player applies their active hand to a slot. Ghosts get the Invalid sentinel.</summary>
		public static InventoryApply ByLocalPlayer(LocalPlayerContext local, ItemSlotRef? targetSlot, ItemSlotRef? fromSlot)
		{
			if (local.Player.PlayerType == PlayerTypes.Ghost)
			{
				return Invalid;
			}

			return new InventoryApply(local.Player, targetSlot, fromSlot, local.CurrentIntent, local.IsAltActionKeyPressed);
		}

		/// <summary>Server only: reconstruct the inventory apply a client sent.</summary>
		public static InventoryApply ByClient(PlayerEntity clientPlayer, ItemSlotRef? targetSlot, ItemSlotRef? fromSlot, Intent intent, bool isAltClick)
		{
			return new InventoryApply(clientPlayer, targetSlot, fromSlot, intent, isAltClick);
		}
	}

	/// <summary>
	/// Dragging an object from the world (or from inventory) and dropping it onto
	/// another world object. Validation needs BOTH performer-can-touch-the-dragged-
	/// object AND a clear line between the two objects' server positions (FC-3).
	/// </summary>
	public sealed class MouseDrop : TargetedInteraction
	{
		public static readonly MouseDrop Invalid = new MouseDrop(null, null, null, null, Intent.Help);

		/// <summary>If dragging from inventory, the slot dragged from. Null otherwise.</summary>
		public ItemSlotRef? FromSlot { get; }

		public bool IsFromInventory => FromSlot != null;

		/// <summary>Object being dropped (same as UsedObject).</summary>
		public WorldEntity? DroppedObject => UsedObject;

		private MouseDrop(PlayerEntity? performer, WorldEntity? droppedObject, WorldEntity? targetObject, ItemSlotRef? fromSlot, Intent intent)
			: base(performer, droppedObject, targetObject, intent)
		{
			FromSlot = fromSlot;
		}

		/// <summary>Local player drops a dragged object onto a target.</summary>
		public static MouseDrop ByLocalPlayer(LocalPlayerContext local, WorldEntity droppedObject, WorldEntity? targetObject, ItemSlotRef? fromSlot = null)
		{
			return new MouseDrop(local.Player, droppedObject, targetObject, fromSlot, local.CurrentIntent);
		}

		/// <summary>Server only: reconstruct the mouse drop a client sent.</summary>
		public static MouseDrop ByClient(PlayerEntity clientPlayer, WorldEntity droppedObject, WorldEntity? targetObject, Intent intent, ItemSlotRef? fromSlot = null)
		{
			return new MouseDrop(clientPlayer, droppedObject, targetObject, fromSlot, intent);
		}
	}

	/// <summary>
	/// Applying the hand/held item to a specific tile position. TargetObject is
	/// the tile-map entity (the original's TargetInteractableTiles.gameObject).
	/// </summary>
	public sealed class TileApply : TargetedInteraction
	{
		/// <summary>World position of the targeted tile.</summary>
		public Vector2 TargetPosition { get; }

		public TileApply(PlayerEntity? performer, WorldEntity? usedObject, WorldEntity? interactableTiles, Vector2 targetPosition, Intent intent)
			: base(performer, usedObject, interactableTiles, intent)
		{
			TargetPosition = targetPosition;
		}
	}

	/// <summary>Clicking a specific cable/pipe connection point on a target object.</summary>
	public sealed class ConnectionApply : TargetedInteraction
	{
		/// <summary>World position of the connection point clicked.</summary>
		public Vector2 TargetPosition { get; }

		public ConnectionApply(PlayerEntity? performer, WorldEntity? usedObject, WorldEntity? targetObject, Vector2 targetPosition, Intent intent)
			: base(performer, usedObject, targetObject, intent)
		{
			TargetPosition = targetPosition;
		}
	}

	/// <summary>An action chosen from the right-click context menu.</summary>
	public sealed class ContextMenuApply : TargetedInteraction
	{
		public ContextMenuApply(PlayerEntity? performer, WorldEntity? usedObject, WorldEntity? targetObject, Intent intent)
			: base(performer, usedObject, targetObject, intent)
		{
		}
	}

	/// <summary>
	/// Continuous aimed fire (guns). Default validation is actor-state plus a
	/// not-hidden check; aim geometry is the weapon system's problem.
	/// </summary>
	public sealed class AimApply : Interaction
	{
		public AimApply(PlayerEntity? performer, WorldEntity? usedObject, Intent intent)
			: base(performer, usedObject, intent)
		{
		}
	}
}
