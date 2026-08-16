using System;
using System.Collections.Generic;
using System.Numerics;
using Godotstation.Common.Interactions;
using Xunit;

namespace Godotstation.Tests.Interactions
{
	/// <summary>
	/// The regression suite for FIDELITY_CONTRACT_INTERACTION.md. One region per
	/// contract clause, named FCn_*. These tests pin the *refusals and
	/// permissions* players have muscle memory around — they must survive any
	/// refactor of the implementing code, forever.
	/// </summary>
	public class InteractionFidelityTests
	{
		private static readonly IReachabilityMap Open = UnobstructedMap.Instance;

		/// <summary>Map that blocks exactly the segments the given predicate matches (order-insensitive).</summary>
		private sealed class WallMap : IReachabilityMap
		{
			private readonly Func<Vector2, Vector2, bool> blocked;

			public WallMap(Func<Vector2, Vector2, bool> blocked) => this.blocked = blocked;

			public bool IsPassableForClick(Vector2 from, Vector2 to, bool isServer, WorldEntity? context = null)
				=> blocked(from, to) == false && blocked(to, from) == false;
		}

		private static PlayerEntity PlayerAt(float x, float y)
		{
			var player = new PlayerEntity { Id = 1, Name = "performer", IsLocalPlayer = true };
			player.PlaceAt(new Vector2(x, y));
			return player;
		}

		private static WorldEntity EntityAt(float x, float y, int id = 100)
		{
			var entity = new WorldEntity { Id = id, Name = $"entity{id}" };
			entity.PlaceAt(new Vector2(x, y));
			return entity;
		}

		private static HandApply Hand(PlayerEntity performer, WorldEntity? target, WorldEntity? held = null)
			=> HandApply.ByClient(performer, held, target, BodyPartType.Chest, null, Intent.Help, false);

		// =====================================================================
		#region FC-1: One tile means one tile

		public static IEnumerable<object[]> AllOffsets()
		{
			for (var dx = -3; dx <= 3; dx++)
				for (var dy = -3; dy <= 3; dy++)
					yield return new object[] { dx, dy };
		}

		[Theory]
		[MemberData(nameof(AllOffsets))]
		public void Fc1_HandApply_SucceedsExactlyWithinOneTile_OnBothSides(int dx, int dy)
		{
			var performer = PlayerAt(0, 0);
			var target = EntityAt(dx, dy);
			var interaction = Hand(performer, target);

			var expected = Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1;

			Assert.Equal(expected, DefaultWillInteract.Default(interaction, NetworkSide.Server, Open));
			Assert.Equal(expected, DefaultWillInteract.Default(interaction, NetworkSide.Client, Open));
		}

		[Fact]
		public void Fc1_ReachIsStrictChebyshev_NotEuclidean()
		{
			var performer = PlayerAt(0, 0);

			// 1.4 diagonal: Chebyshev 1.4 < 1.5 passes even though Euclidean is ~1.98.
			Assert.True(DefaultWillInteract.Default(Hand(performer, EntityAt(1.4f, 1.4f)), NetworkSide.Server, Open));

			// Exactly 1.5 is out: the comparison is strict.
			Assert.False(DefaultWillInteract.Default(Hand(performer, EntityAt(1.5f, 0f)), NetworkSide.Server, Open));
			Assert.True(DefaultWillInteract.Default(Hand(performer, EntityAt(1.49f, 0f)), NetworkSide.Server, Open));
		}

		[Fact]
		public void Fc1_RegisteredTileIsAuthoritative_NotVisualPosition()
		{
			var performer = PlayerAt(0, 0);

			// Mid-animation: registered tile adjacent, visual position far away → in reach.
			var sliding = EntityAt(1, 0);
			sliding.TransformPosition = new Vector2(10, 10);
			Assert.True(DefaultWillInteract.Default(Hand(performer, sliding), NetworkSide.Server, Open));

			// Inverse: visual position adjacent, registered tile far → out of reach.
			var visualOnly = EntityAt(10, 10);
			visualOnly.TransformPosition = new Vector2(1, 0);
			Assert.False(DefaultWillInteract.Default(Hand(performer, visualOnly), NetworkSide.Server, Open));
		}

		[Fact]
		public void Fc1_BlockedClickLine_RefusesEvenAdjacent()
		{
			var performer = PlayerAt(0, 0);
			var target = EntityAt(1, 0);
			var wall = new WallMap((a, b) => a == new Vector2(0, 0) && b == new Vector2(1, 0));

			Assert.False(DefaultWillInteract.Default(Hand(performer, target), NetworkSide.Server, wall));
			Assert.True(DefaultWillInteract.Default(Hand(performer, target), NetworkSide.Server, Open));
		}

		#endregion

		// =====================================================================
		#region FC-2: Telekinesis works only with an empty hand

		[Theory]
		[InlineData(true, true, true, true)]   // the single passing cell
		[InlineData(true, true, false, false)]
		[InlineData(true, false, true, false)]
		[InlineData(true, false, false, false)]
		[InlineData(false, true, true, false)]
		[InlineData(false, true, false, false)]
		[InlineData(false, false, true, false)]
		[InlineData(false, false, false, false)]
		public void Fc2_TelekinesisMatrix_OnlyTkEmptyHandAllowedSucceedsAtRange(
			bool hasTk, bool emptyHand, bool allowTk, bool expected)
		{
			var performer = PlayerAt(0, 0);
			performer.HasTelekinesis = hasTk;
			var target = EntityAt(5, 0);
			var held = emptyHand ? null : EntityAt(0, 0, id: 55);

			var interaction = Hand(performer, target, held);

			Assert.Equal(expected, DefaultWillInteract.Default(interaction, NetworkSide.Server, Open, allowTelekinesis: allowTk));
			Assert.Equal(expected, DefaultWillInteract.Default(interaction, NetworkSide.Client, Open, allowTelekinesis: allowTk));
		}

		[Fact]
		public void Fc2_TelekinesisRange_IsEuclideanFifteen_InclusiveBoundary()
		{
			var performer = PlayerAt(0, 0);
			performer.HasTelekinesis = true;

			// Exactly 15 passes ("> 15" refuses), 15.01 does not.
			Assert.True(DefaultWillInteract.Default(Hand(performer, EntityAt(15f, 0f)), NetworkSide.Server, Open));
			Assert.False(DefaultWillInteract.Default(Hand(performer, EntityAt(15.01f, 0f)), NetworkSide.Server, Open));

			// Euclidean, not Chebyshev: (11, 11) has Chebyshev 11 but Euclidean ~15.56.
			Assert.False(DefaultWillInteract.Default(Hand(performer, EntityAt(11f, 11f)), NetworkSide.Server, Open));
		}

		[Fact]
		public void Fc2_Telekinesis_ReachesThroughWalls()
		{
			// Faithful to the original: the TK branch performs no blocking
			// linecast at all. FOV gating is a different system's job.
			var performer = PlayerAt(0, 0);
			performer.HasTelekinesis = true;
			var target = EntityAt(5, 0);
			var everythingBlocked = new WallMap((a, b) => true);

			Assert.True(DefaultWillInteract.Default(Hand(performer, target), NetworkSide.Server, everythingBlocked));
		}

		#endregion

		// =====================================================================
		#region FC-3: Drag-and-drop needs a clear line, not adjacency

		[Fact]
		public void Fc3_MouseDrop_RefusesWhenLineBetweenObjectsBlocked_EvenIfBothAdjacent()
		{
			// Performer at origin, A and B each one tile away around a wall corner.
			var performer = PlayerAt(0, 0);
			var dragged = EntityAt(1, 0, id: 10);
			var dropTarget = EntityAt(0, 1, id: 11);

			// Wall blocks only the A→B segment; performer can reach both individually.
			var cornerWall = new WallMap((a, b) => a == new Vector2(1, 0) && b == new Vector2(0, 1));

			var drop = MouseDrop.ByClient(performer, dragged, dropTarget, Intent.Help);

			Assert.False(DefaultWillInteract.Default(drop, NetworkSide.Server, cornerWall));
			Assert.True(DefaultWillInteract.Default(drop, NetworkSide.Server, Open));
		}

		[Fact]
		public void Fc3_MouseDrop_UsesAssumedServerPositions_NotClientPositions()
		{
			var performer = PlayerAt(0, 0);
			var dragged = EntityAt(1, 0, id: 10);
			var dropTarget = EntityAt(0, 1, id: 11);

			// Client-side registered positions claim adjacency, but the assumed
			// server position of the drop target is far away: must refuse.
			dropTarget.AssumedWorldPosServer = new Vector2(20, 20);

			var drop = MouseDrop.ByClient(performer, dragged, dropTarget, Intent.Help);

			Assert.False(DefaultWillInteract.Default(drop, NetworkSide.Server, Open));
			// Both sides read assumed server positions for this check (amendment A3).
			Assert.False(DefaultWillInteract.Default(drop, NetworkSide.Client, Open));
		}

		[Fact]
		public void Fc3_MouseDrop_StillRequiresPerformerCanTouchDraggedObject()
		{
			var performer = PlayerAt(0, 0);
			var dragged = EntityAt(6, 0, id: 10);      // out of performer's reach
			var dropTarget = EntityAt(6, 1, id: 11);   // adjacent to dragged

			var drop = MouseDrop.ByClient(performer, dragged, dropTarget, Intent.Help);

			Assert.False(DefaultWillInteract.Default(drop, NetworkSide.Server, Open));
		}

		#endregion

		// =====================================================================
		#region FC-4: What's in your hand always works

		[Fact]
		public void Fc4_HandActivateAndInventoryApply_IgnoreReachEntirely()
		{
			// Performer sealed alone: everything blocked, nothing adjacent.
			var performer = PlayerAt(100, 100);
			var flashlight = EntityAt(100, 100, id: 20);
			var everythingBlocked = new WallMap((a, b) => true);

			var slotA = new ItemSlotRef { Named = NamedSlot.LeftHand, RootPlayer = performer, Item = flashlight };
			var slotB = new ItemSlotRef { Named = NamedSlot.Belt, RootPlayer = performer, Item = null };

			var activate = HandActivate.ByClient(performer, flashlight, slotA, Intent.Help, false);
			var transfer = InventoryApply.ByClient(performer, slotB, slotA, Intent.Help, false);

			Assert.True(DefaultWillInteract.Default(activate, NetworkSide.Server, everythingBlocked));
			Assert.True(DefaultWillInteract.Default(transfer, NetworkSide.Server, everythingBlocked));
		}

		[Theory]
		[InlineData(true, false, ConsciousState.Conscious)]        // cuffed
		[InlineData(false, false, ConsciousState.Unconscious)]     // out cold
		[InlineData(false, false, ConsciousState.BarelyConscious)] // soft crit not allowed by default
		[InlineData(false, false, ConsciousState.Dead)]
		[InlineData(false, true, ConsciousState.Conscious)]        // input locked
		public void Fc4_ActorStateGrounds_AreTheOnlyRefusals(bool cuffed, bool inputLocked, ConsciousState state)
		{
			var performer = PlayerAt(0, 0);
			performer.IsCuffed = cuffed;
			performer.AllowInput = inputLocked == false;
			performer.ConsciousState = state;

			var item = EntityAt(0, 0, id: 20);
			var slot = new ItemSlotRef { Named = NamedSlot.LeftHand, RootPlayer = performer, Item = item };
			var activate = HandActivate.ByClient(performer, item, slot, Intent.Help, false);
			var transfer = InventoryApply.ByClient(performer,
				new ItemSlotRef { Named = NamedSlot.Belt, RootPlayer = performer }, slot, Intent.Help, false);

			Assert.False(DefaultWillInteract.Default(activate, NetworkSide.Server, Open));
			Assert.False(DefaultWillInteract.Default(transfer, NetworkSide.Server, Open));
		}

		[Fact]
		public void Fc4_GhostPlayerType_IsRefusedByTheAptMask()
		{
			var performer = PlayerAt(0, 0);
			performer.PlayerType = PlayerTypes.Ghost;

			var item = EntityAt(0, 0, id: 20);
			var slot = new ItemSlotRef { Named = NamedSlot.LeftHand, RootPlayer = performer, Item = item };
			var activate = HandActivate.ByClient(performer, item, slot, Intent.Help, false);

			Assert.False(DefaultWillInteract.Default(activate, NetworkSide.Server, Open));
		}

		#endregion

		// =====================================================================
		#region FC-5: Ghosts don't click

		[Fact]
		public void Fc5_GhostingLocalPlayer_GetsTheInvalidSentinel_BeforeAnyPipeline()
		{
			var context = new LocalPlayerContext { Player = PlayerAt(0, 0), IsGhosting = true };
			var target = EntityAt(1, 0);

			var interaction = HandApply.ByLocalPlayer(context, target);

			// Reference identity, exactly like the original's sentinel comparison.
			Assert.Same(HandApply.Invalid, interaction);

			// And the sentinel refuses validation too — nothing downstream can fire.
			Assert.False(DefaultWillInteract.Default(interaction, NetworkSide.Client, Open));
		}

		[Fact]
		public void Fc5_NonGhostingLocalPlayer_GetsARealInteraction()
		{
			var context = new LocalPlayerContext { Player = PlayerAt(0, 0) };
			var interaction = HandApply.ByLocalPlayer(context, EntityAt(1, 0));

			Assert.NotSame(HandApply.Invalid, interaction);
			Assert.True(DefaultWillInteract.Default(interaction, NetworkSide.Client, Open));
		}

		#endregion

		// =====================================================================
		#region FC-6: Intent decides what the same click does — deferred

		[Fact(Skip = "Wave 2: intent-verb selection lives in the dispatch chain (InteractionUtils / " +
		             "interactable components), sources not yet ported. Clause remains binding; see amendment A7.")]
		public void Fc6_FourIntents_SelectFourVerbs_OnAHumanoidTarget()
		{
		}

		#endregion

		// =====================================================================
		#region FC-7: Click context is frozen at click time

		[Fact]
		public void Fc7_InteractionCarriesClickTimeState_MutatingUiAfterwardsChangesNothing()
		{
			var performer = PlayerAt(0, 0);
			var wrench = EntityAt(0, 0, id: 30);
			var crowbar = EntityAt(0, 0, id: 31);
			var rightHand = new ItemSlotRef { Named = NamedSlot.RightHand, RootPlayer = performer, Item = wrench };
			var leftHand = new ItemSlotRef { Named = NamedSlot.LeftHand, RootPlayer = performer, Item = crowbar };

			var context = new LocalPlayerContext
			{
				Player = performer,
				ActiveHandSlot = rightHand,
				CurrentIntent = Intent.Harm,
				DamageZone = BodyPartType.Head,
				IsAltActionKeyPressed = true,
			};

			var interaction = HandApply.ByLocalPlayer(context, EntityAt(1, 0));

			// The player flails at the UI between click and processing:
			context.CurrentIntent = Intent.Help;       // switches intent
			context.ActiveHandSlot = leftHand;         // swaps hands
			context.IsAltActionKeyPressed = false;     // releases alt
			context.DamageZone = BodyPartType.LeftFoot;
			rightHand.Item = null;                     // even the clicked slot is emptied

			// The interaction is a snapshot of the click, not a view of the UI.
			Assert.Equal(Intent.Harm, interaction.Intent);
			Assert.Same(rightHand, interaction.HandSlot);
			Assert.Same(wrench, interaction.HandObject);
			Assert.True(interaction.IsAltClick);
			Assert.Equal(BodyPartType.Head, interaction.TargetBodyPart);
		}

		[Fact]
		public void Fc7_PositionalHandApply_FreezesTheClickedPosition()
		{
			var context = new LocalPlayerContext { Player = PlayerAt(0, 0) };
			var interaction = PositionalHandApply.ByLocalPlayer(context, null, new Vector2(1.2f, 0.7f));

			Assert.Equal(new Vector2(1.2f, 0.7f), interaction.TargetPosition);
		}

		#endregion

		// =====================================================================
		#region FC-8: The client predicts exactly what the server enforces

		public static IEnumerable<object[]> Seeds()
		{
			yield return new object[] { 1234 };
			yield return new object[] { 5678 };
			yield return new object[] { 424242 };
		}

		[Theory]
		[MemberData(nameof(Seeds))]
		public void Fc8_ClientAndServerVerdictsAgree_OnMirroredState(int seed)
		{
			// Property-style invariant over randomized states. Excludes the three
			// sanctioned asymmetries (hidden performer, ExtendedServer,
			// non-local conscious state) per amendment A4 — those are pinned
			// individually below.
			var rng = new Random(seed);

			for (var i = 0; i < 500; i++)
			{
				var performer = PlayerAt(0, 0);
				performer.IsCuffed = rng.Next(10) == 0;
				performer.AllowInput = rng.Next(10) != 0;
				performer.HasTelekinesis = rng.Next(4) == 0;
				performer.ConsciousState = (ConsciousState)rng.Next(4);
				performer.PlayerType = rng.Next(8) == 0 ? PlayerTypes.Ghost : PlayerTypes.Normal;

				var target = EntityAt(rng.Next(-6, 7), rng.Next(-6, 7), id: 100 + i);
				var held = rng.Next(2) == 0 ? null : EntityAt(0, 0, id: 1000 + i);
				var intent = (Intent)rng.Next(4);
				var map = rng.Next(3) == 0
					? new WallMap((a, b) => ((int)(a.X + a.Y + b.X + b.Y) & 1) == 0)
					: (IReachabilityMap)Open;

				Interaction interaction = rng.Next(3) switch
				{
					0 => HandApply.ByClient(performer, held, target, BodyPartType.Chest, null, intent, false),
					1 => HandActivate.ByClient(performer, held, null, intent, false),
					_ => MouseDrop.ByClient(performer, target, EntityAt(rng.Next(-6, 7), rng.Next(-6, 7), id: 2000 + i), intent),
				};

				var clientVerdict = DefaultWillInteract.Default(interaction, NetworkSide.Client, map);
				var serverVerdict = DefaultWillInteract.Default(interaction, NetworkSide.Server, map);

				Assert.True(clientVerdict == serverVerdict,
					$"seed {seed} case {i}: client said {clientVerdict}, server said {serverVerdict}");
			}
		}

		[Fact]
		public void Fc8a_HiddenPerformer_ClientDefers_ServerAllowsOnlyTheContainer()
		{
			var performer = PlayerAt(0, 0);
			var locker = EntityAt(0, 0, id: 40);
			var bystander = EntityAt(1, 0, id: 41);
			performer.IsHidden = true;
			performer.ContainedIn = locker;

			// Sanctioned asymmetry: the client validates permissively and defers.
			Assert.True(DefaultWillInteract.Default(Hand(performer, locker), NetworkSide.Client, Open));
			Assert.True(DefaultWillInteract.Default(Hand(performer, bystander), NetworkSide.Client, Open));

			// The server permits exactly the container.
			Assert.True(DefaultWillInteract.Default(Hand(performer, locker), NetworkSide.Server, Open));
			Assert.False(DefaultWillInteract.Default(Hand(performer, bystander), NetworkSide.Server, Open));
		}

		#endregion

		// =====================================================================
		#region FC-9: The hand's item gets first refusal — deferred

		[Fact(Skip = "Wave 2: component resolution order lives in the dispatch chain (InteractionUtils), " +
		             "sources not yet ported. Clause remains binding; see amendment A7.")]
		public void Fc9_HeldItemHandlerRunsBeforeTargetHandler_FirstAcceptConsumes()
		{
		}

		#endregion

		// =====================================================================
		#region Amendments: structural pins

		[Fact]
		public void A5_ItemInSomeoneElsesInventory_IsUnreachableAtAnyRange()
		{
			var performer = PlayerAt(0, 0);
			var other = PlayerAt(1, 0);
			var stolen = EntityAt(1, 0, id: 50);
			stolen.IsInItemSlot = true;
			stolen.InventoryRootPlayer = other;

			// Adjacent, unobstructed, even telekinetic — still no.
			performer.HasTelekinesis = true;
			Assert.False(DefaultWillInteract.Default(Hand(performer, stolen), NetworkSide.Server, Open));

			// But the performer's own inventory is always reachable.
			var mine = EntityAt(0, 0, id: 51);
			mine.IsInItemSlot = true;
			mine.InventoryRootPlayer = performer;
			Assert.True(DefaultWillInteract.Default(Hand(performer, mine, held: EntityAt(0, 0, id: 52)), NetworkSide.Server, Open));
		}

		[Fact]
		public void A6_PositionalHandApply_DispatchesAsPositional_NotAsPlainHandApply()
		{
			// Target object adjacent, but the CLICKED POSITION is far away. The
			// positional branch validates the clicked position and must refuse;
			// if dispatch ever falls through to the HandApply branch this
			// wrongly passes — the exact regression amendment A6 warns about.
			var performer = PlayerAt(0, 0);
			var target = EntityAt(1, 0);

			var positional = PositionalHandApply.ByClient(
				performer, null, target, targetPosition: new Vector2(9, 9),
				null, Intent.Help, BodyPartType.Chest, false);

			Assert.False(DefaultWillInteract.Default(positional, NetworkSide.Server, Open));

			// The mirror case: clicked position adjacent, target object far — passes.
			var farTarget = EntityAt(9, 9, id: 60);
			var positionalNear = PositionalHandApply.ByClient(
				performer, null, farTarget, targetPosition: new Vector2(1, 0),
				null, Intent.Help, BodyPartType.Chest, false);

			Assert.True(DefaultWillInteract.Default(positionalNear, NetworkSide.Server, Open));
		}

		private sealed class UnknownInteraction : Interaction
		{
			public UnknownInteraction(PlayerEntity performer) : base(performer, null, Intent.Help) { }
		}

		[Fact]
		public void UnrecognizedInteractionType_Refuses_AsUpstream()
		{
			Assert.False(DefaultWillInteract.Default(new UnknownInteraction(PlayerAt(0, 0)), NetworkSide.Server, Open));
		}

		#endregion
	}
}
