using System.Collections.Generic;

namespace Project.Common.Atmos
{
    public enum TileType
    {
        Room,
        Space,
        Occupied // Walls, closed doors
    }

    public class AtmosTile
    {
        public int X { get; }
        public int Y { get; }

        public GasMix GasMix { get; } = new GasMix();
        public TileType Type { get; set; } = TileType.Room;

        public bool IsIsolated { get; set; } = false;

        public float ConductivityTemperature { get; set; } = 293.15f;
        public float ThermalConductivity { get; set; } = 0.05f;
        public float HeatCapacity { get; set; } = 10000f;

        public bool StartingSuperConduct { get; set; } = false;
        public bool AllowedToSuperConduct { get; set; } = false;

        public int WindDirectionX { get; set; }
        public int WindDirectionY { get; set; }
        public float WindForce { get; set; }

        public List<AtmosTile> Neighbors { get; } = new List<AtmosTile>();

        public AtmosTile(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool CanPassGas()
        {
            return Type != TileType.Occupied && !IsIsolated;
        }
    }
}
