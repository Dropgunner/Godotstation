using Godotstation.Common.Interaction;
using Xunit;

namespace Godotstation.Common.Tests.Interaction;

/// <summary>
/// FC-1: an interaction is only attempted by a performer who is the right kind of player,
/// uncuffed, input-enabled and conscious - and the client trusts the server for everyone
/// but itself.
/// </summary>
public class FC1_ActorGatingTests
{
	private static (FakePlayer player, FakeWorld world) HealthyPlayer()
	{
		var player = new FakePlayer();
		var world = new FakeWorld { LocalPlayer = player };
		return (player, world);
	}

	[Fact]
	public void ConsciousNormalPlayer_CanInteract()
	{
		var (player, world) = HealthyPlayer();
		Assert.True(Validations.CanInteract(player, world, NetworkSide.Server));
	}

	[Fact]
	public void NullPlayer_CannotInteract()
	{
		var (_, world) = HealthyPlayer();
		Assert.False(Validations.CanInteract(null, world, NetworkSide.Server));
	}

	[Theory]
	[InlineData(PlayerTypes.Ghost)]
	[InlineData(PlayerTypes.Blob)]
	[InlineData(PlayerTypes.Alien)]
	public void DisallowedPlayerType_Rejected(PlayerTypes type)
	{
		var (player, world) = HealthyPlayer();
		player.PlayerType = type;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Server));
	}

	[Fact]
	public void AptMask_AdmitsEveryFlaggedType()
	{
		var (player, world) = HealthyPlayer();
		player.PlayerType = PlayerTypes.Alien;
		Assert.True(Validations.CanInteract(player, world, NetworkSide.Server,
			apt: PlayerTypes.Normal | PlayerTypes.Alien));
	}

	[Fact]
	public void Cuffed_Rejected_UnlessExplicitlyAllowed()
	{
		var (player, world) = HealthyPlayer();
		player.IsCuffed = true;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Server));
		Assert.True(Validations.CanInteract(player, world, NetworkSide.Server, allowCuffed: true));
	}

	[Fact]
	public void InputDisabled_Rejected()
	{
		var (player, world) = HealthyPlayer();
		player.AllowInput = false;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Server));
	}

	[Fact]
	public void SoftCrit_RejectedByDefault_AllowedWhenOptedIn()
	{
		var (player, world) = HealthyPlayer();
		player.ConsciousState = ConsciousState.BARELY_CONSCIOUS;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Server));
		Assert.True(Validations.CanInteract(player, world, NetworkSide.Server, allowSoftCrit: true));
	}

	[Theory]
	[InlineData(ConsciousState.UNCONSCIOUS)]
	[InlineData(ConsciousState.DEAD)]
	public void UnconsciousOrDead_Rejected_EvenWithSoftCrit(ConsciousState state)
	{
		var (player, world) = HealthyPlayer();
		player.ConsciousState = state;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Server, allowSoftCrit: true));
	}

	[Fact]
	public void Client_AssumesRemotePlayersAreConscious()
	{
		// The client only knows its own conscious state; a dead *remote* performer passes the
		// client check and is caught by the server's authoritative re-validation.
		var remote = new FakePlayer { ConsciousState = ConsciousState.DEAD };
		var world = new FakeWorld { LocalPlayer = new FakePlayer() };

		Assert.True(Validations.CanInteract(remote, world, NetworkSide.Client));
		Assert.False(Validations.CanInteract(remote, world, NetworkSide.Server));
	}

	[Fact]
	public void Client_ChecksItsOwnConsciousState()
	{
		var (player, world) = HealthyPlayer();
		player.ConsciousState = ConsciousState.DEAD;
		Assert.False(Validations.CanInteract(player, world, NetworkSide.Client));
	}
}
