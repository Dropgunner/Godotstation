namespace Godotstation.Engine.Atmos
{
	/// <summary>A deterministic gas reaction operating on the engine's value-type mix.</summary>
	public interface IAtmosReaction
	{
		bool Satisfies(GasMix gasMix);
		bool React(ref GasMix gasMix);
	}
}
