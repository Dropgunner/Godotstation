using Godotstation.Engine.Atmos;
using Xunit;

namespace Godotstation.Tests.Atmos;

public sealed class PlasmaFireReactionTests
{
	[Fact]
	public void SatisfiesRequiresBothFuelAndOxidizer()
	{
		var reaction = new PlasmaFireReaction();
		var mix = new GasMix(GasMix.TileVolume, 500f);
		mix.SetMoles(Gas.Plasma, 1f);
		Assert.False(reaction.Satisfies(mix));
		mix.SetMoles(Gas.Oxygen, 1f);
		Assert.True(reaction.Satisfies(mix));
	}

	[Fact]
	public void ReactBelowIgnitionLeavesMixUnchanged()
	{
		var reaction = new PlasmaFireReaction();
		var mix = new GasMix(GasMix.TileVolume, 300f);
		mix.SetMoles(Gas.Plasma, 10f);
		mix.SetMoles(Gas.Oxygen, 100f);
		var initialEnergy = mix.InternalEnergy;

		Assert.False(reaction.React(ref mix));
		Assert.Equal(10f, mix.GetMoles(Gas.Plasma));
		Assert.Equal(100f, mix.GetMoles(Gas.Oxygen));
		Assert.Equal(0f, mix.GetMoles(Gas.CarbonDioxide));
		Assert.Equal(initialEnergy, mix.InternalEnergy);
	}

	[Fact]
	public void ReactConsumesReactantsProducesCarbonDioxideAndAddsEnergy()
	{
		var reaction = new PlasmaFireReaction();
		var mix = new GasMix(GasMix.TileVolume, 500f);
		mix.SetMoles(Gas.Plasma, 10f);
		mix.SetMoles(Gas.Oxygen, 100f);
		var initialEnergy = mix.InternalEnergy;

		Assert.True(reaction.React(ref mix));
		Assert.True(mix.GetMoles(Gas.Plasma) < 10f);
		Assert.True(mix.GetMoles(Gas.Oxygen) < 100f);
		Assert.True(mix.GetMoles(Gas.CarbonDioxide) > 0f);
		Assert.True(mix.InternalEnergy > initialEnergy);
	}
}
