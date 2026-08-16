using System.Numerics;

namespace Godotstation.Common.Interactions
{
	/// <summary>
	/// The world-geometry question the interaction system asks and nothing more:
	/// "is the click line between two world positions passable?". Mirrors
	/// MatrixManager.IsPassableAtAllMatrices(CollisionType.Click, isReach: true,
	/// excludeLayers: Walls/Windows/Grills, onlyExcludeLayerOnDestination: true).
	/// Implemented by the Engine's tile world; by stubs in tests.
	/// </summary>
	public interface IReachabilityMap
	{
		/// <param name="context">If not null, collisions caused by this entity are ignored.</param>
		bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer, WorldEntity? context = null);
	}

	/// <summary>Open-space map: every click line is passable. Default for tests.</summary>
	public sealed class UnobstructedMap : IReachabilityMap
	{
		public static readonly UnobstructedMap Instance = new UnobstructedMap();

		public bool IsPassableForClick(Vector2 fromWorldPos, Vector2 toWorldPos, bool isServer, WorldEntity? context = null)
			=> true;
	}

	/// <summary>
	/// Headless stand-in for the GameObject + RegisterTile + UniversalObjectPhysics
	/// bundle the original validations read. Every position the Unity code could
	/// observe is an explicit field here, because WHICH position source a check
	/// reads is contract-bearing (FC-1 mid-animation, FC-3 server positions).
	/// </summary>
	public class WorldEntity
	{
		public int Id { get; init; }

		public string Name { get; init; } = "";

		/// <summary>False when the entity has no RegisterTile — forces the transform-position fallback path.</summary>
		public bool HasRegisterTile { get; set; } = true;

		/// <summary>RegisterTile.WorldPosition — the registered tile as the client sees it.</summary>
		public Vector2 WorldPositionClient { get; set; }

		/// <summary>RegisterTile.WorldPositionServer — the registered tile as the server knows it.</summary>
		public Vector2 WorldPositionServer { get; set; }

		/// <summary>transform.position — the visual/interpolated position. Almost never authoritative (FC-1).</summary>
		public Vector2 TransformPosition { get; set; }

		/// <summary>
		/// UniversalObjectPhysics.AssumedWorldPosServer() — the predicted server
		/// position, readable on both sides. Telekinesis and MouseDrop read this
		/// regardless of which side is validating.
		/// </summary>
		public Vector2 AssumedWorldPosServer { get; set; }

		/// <summary>True when the entity is a Pickupable sitting in an item slot that belongs to some storage.</summary>
		public bool IsInItemSlot { get; set; }

		/// <summary>ItemSlot.RootPlayer() — the player whose inventory tree contains this entity, if any.</summary>
		public PlayerEntity? InventoryRootPlayer { get; set; }

		/// <summary>True when the entity has UniversalObjectPhysics (enables the ExtendedServer reach path).</summary>
		public bool HasPhysics { get; set; }

		/// <summary>UniversalObjectPhysics.OfficialPosition.</summary>
		public Vector2 OfficialPosition { get; set; }

		/// <summary>UniversalObjectPhysics.NewtonianMovement — current flight velocity.</summary>
		public Vector2 NewtonianMovement { get; set; }

		public Vector2 RegisteredPosition(NetworkSide side)
			=> side == NetworkSide.Server ? WorldPositionServer : WorldPositionClient;

		/// <summary>Test/setup convenience: put the entity at one position in every position source.</summary>
		public void PlaceAt(Vector2 worldPos)
		{
			WorldPositionClient = worldPos;
			WorldPositionServer = worldPos;
			TransformPosition = worldPos;
			AssumedWorldPosServer = worldPos;
			OfficialPosition = worldPos;
		}
	}

	/// <summary>
	/// Headless stand-in for PlayerScript + PlayerMove + PlayerHealthV2 + brain,
	/// reduced to exactly the fields DefaultWillInteract's validations read.
	/// </summary>
	public sealed class PlayerEntity : WorldEntity
	{
		public PlayerTypes PlayerType { get; set; } = PlayerTypes.Normal;

		/// <summary>playerMove.IsCuffed.</summary>
		public bool IsCuffed { get; set; }

		/// <summary>playerMove.AllowInput.</summary>
		public bool AllowInput { get; set; } = true;

		public ConsciousState ConsciousState { get; set; } = ConsciousState.Conscious;

		/// <summary>
		/// Whether this player is the local player on the validating client. The
		/// client only knows its OWN conscious state; for anyone else it assumes
		/// Conscious and defers to the server (sanctioned asymmetry, FC-8a).
		/// </summary>
		public bool IsLocalPlayer { get; set; }

		/// <summary>True while hidden inside a container (closet, crate...).</summary>
		public bool IsHidden { get; set; }

		/// <summary>
		/// ObjectPhysics.ContainedInObjectContainer — what the player is hidden
		/// inside. Known only server-side in the original; a hidden client
		/// validates permissively and lets the server decide (FC-8a).
		/// </summary>
		public WorldEntity? ContainedIn { get; set; }

		/// <summary>playerHealth.brain != null &amp;&amp; brain.HasTelekinesis (FC-2).</summary>
		public bool HasTelekinesis { get; set; }
	}

	/// <summary>
	/// Headless stand-in for Systems.Inventory.ItemSlot — just enough identity
	/// for interactions to freeze a reference to "the active hand at click time".
	/// </summary>
	public sealed class ItemSlotRef
	{
		public NamedSlot Named { get; init; }

		/// <summary>The player whose inventory tree owns this slot, if any.</summary>
		public PlayerEntity? RootPlayer { get; init; }

		/// <summary>Mutable on purpose: the LIVE slot content. Interactions must snapshot it (FC-7), never re-read it.</summary>
		public WorldEntity? Item { get; set; }
	}

	/// <summary>
	/// Snapshot source for client-side interaction factories. Replaces the
	/// PlayerManager / UIManager / KeyboardInputManager singletons the original
	/// factories read. The input layer keeps ONE of these current; factories
	/// read it exactly once at click time and freeze the values (FC-7) — this
	/// object stays mutable precisely so tests can prove the freeze.
	/// </summary>
	public sealed class LocalPlayerContext
	{
		public required PlayerEntity Player { get; init; }

		/// <summary>PlayerManager.LocalMindScript.IsGhosting (FC-5).</summary>
		public bool IsGhosting { get; set; }

		/// <summary>DynamicItemStorage.GetActiveHandSlot(). Null when the player has no hands at all.</summary>
		public ItemSlotRef? ActiveHandSlot { get; set; }

		/// <summary>False models DynamicItemStorage == null (PositionalHandApply.ByLocalPlayer guards on it).</summary>
		public bool HasItemStorage { get; set; } = true;

		/// <summary>UIManager.CurrentIntent.</summary>
		public Intent CurrentIntent { get; set; } = Intent.Help;

		/// <summary>UIManager.DamageZone — the body-zone selector state.</summary>
		public BodyPartType DamageZone { get; set; } = BodyPartType.Chest;

		/// <summary>KeyboardInputManager.IsAltActionKeyPressed().</summary>
		public bool IsAltActionKeyPressed { get; set; }
	}
}
