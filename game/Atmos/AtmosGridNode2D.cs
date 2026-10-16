using System;
using System.Globalization;
using System.Text;
using Godot;
using Godotstation.Engine.Atmos;
using Godotstation.Engine.Physics;

namespace Godotstation.Game.Atmos;

/// <summary>Runs the deterministic atmos backend and maps its updates to Godot tile overlays.</summary>
public partial class AtmosGridNode2D : Node2D, IAtmosVisualService
{
    private static readonly Vector3Int[] NeighborOffsets =
    [
        new Vector3Int(0, -1, 0),
        Vector3Int.Right,
        Vector3Int.Down,
        Vector3Int.Left
    ];

    [Export] public Vector2I GridSize { get; set; } = new(8, 4);
    [Export] public float CellSpacing { get; set; } = 1.5f;
    [Export] public double StepInterval { get; set; } = 0.2;
    [Export] public bool SeedPlasmaFire { get; set; } = true;

    private AtmosCellNode2D[,] cells = new AtmosCellNode2D[0, 0];
    private AtmosSimulation simulation = null!;
    private double stepElapsed;

    public override void _Ready()
    {
        if (GridSize.X <= 0 || GridSize.Y <= 0)
        {
            GD.PushError("Atmos grid requires positive dimensions.");
            return;
        }

        simulation = new AtmosSimulation(this);
        cells = new AtmosCellNode2D[GridSize.X, GridSize.Y];
        BuildCells();
        LinkNeighbors();

        foreach (var cell in cells)
            simulation.RequestUpdate(cell);
    }

    public override void _Process(double delta)
    {
        if (simulation == null || StepInterval <= 0d) return;

        stepElapsed += delta;
        while (stepElapsed >= StepInterval)
        {
            stepElapsed -= StepInterval;
            simulation.Run();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        var local = ToLocal(GetGlobalMousePosition());
        var x = Mathf.RoundToInt(local.X / CellSpacing);
        var y = Mathf.RoundToInt(local.Y / CellSpacing);
        if (x < 0 || y < 0 || x >= GridSize.X || y >= GridSize.Y) return;

        var center = new Vector2(x * CellSpacing, y * CellSpacing);
        if (local.DistanceSquaredTo(center) > AtmosCellNode2D.HalfExtent * AtmosCellNode2D.HalfExtent) return;

        GD.Print(DescribeMix(cells[x, y]));
    }

    public void UpdateGasOverlays(IAtmosNode node, in GasMix mix)
    {
        if (node is not AtmosCellNode2D cell) return;

        var oxygenAlpha = Mathf.Clamp(mix.GetMoles(Gas.Oxygen) / 120f, 0f, 0.48f);
        var plasmaAlpha = Mathf.Clamp(mix.GetMoles(Gas.Plasma) / 12f, 0f, 0.7f);
        cell.SetGasOverlays(
            new Color(0.68f, 0.18f, 0.9f, plasmaAlpha),
            new Color(0.16f, 0.38f, 0.95f, oxygenAlpha));
    }

    public void SetFire(IAtmosNode node, bool burning)
    {
        if (node is AtmosCellNode2D cell) cell.SetBurning(burning);
    }

    public void SetIce(IAtmosNode node, bool frozen)
    {
        if (node is AtmosCellNode2D cell) cell.SetIced(frozen);
    }

    private void BuildCells()
    {
        var spaceX = GridSize.X - 1;
        var spaceY = GridSize.Y / 2;
        for (var x = 0; x < GridSize.X; x++)
        for (var y = 0; y < GridSize.Y; y++)
        {
            var cell = new AtmosCellNode2D
            {
                Name = $"AtmosCell_{x}_{y}",
                Position = new Vector2(x * CellSpacing, y * CellSpacing)
            };
            var isSpace = x == spaceX && y == spaceY;
            cell.IsSpace = isSpace;

            var mix = isSpace ? GasMix.Space() : GasMix.StandardAtmosphere();
            if (SeedPlasmaFire && x == GridSize.X / 2 && y == GridSize.Y / 2)
            {
                mix.AddGas(Gas.Plasma, 12f);
                mix.Temperature = 500f;
            }

            cell.Configure(new Vector3Int(x, y, 0), in mix);
            AddChild(cell);
            cells[x, y] = cell;
        }
    }

    private void LinkNeighbors()
    {
        for (var x = 0; x < GridSize.X; x++)
        for (var y = 0; y < GridSize.Y; y++)
        {
            var neighbors = new System.Collections.Generic.List<IAtmosNode>(4);
            foreach (var offset in NeighborOffsets)
            {
                var neighborX = x + offset.X;
                var neighborY = y + offset.Y;
                if (neighborX >= 0 && neighborX < GridSize.X && neighborY >= 0 && neighborY < GridSize.Y)
                    neighbors.Add(cells[neighborX, neighborY]);
            }
            cells[x, y].SetNeighbors(neighbors.ToArray());
        }
    }

    private static string DescribeMix(AtmosCellNode2D cell)
    {
        ref var mix = ref cell.GasMix;
        var result = new StringBuilder()
            .Append("Atmos ").Append(cell.TilePosition).Append(": ")
            .Append(mix.Pressure.ToString("R", CultureInfo.InvariantCulture)).Append(" kPa, ")
            .Append(mix.Temperature.ToString("R", CultureInfo.InvariantCulture)).Append(" K, ")
            .Append(mix.TotalMoles.ToString("R", CultureInfo.InvariantCulture)).Append(" mol [");

        for (var i = 0; i < GasMix.GasCount; i++)
        {
            if (i > 0) result.Append(", ");
            var gas = (Gas)i;
            result.Append(gas).Append('=').Append(mix.GetMoles(gas).ToString("R", CultureInfo.InvariantCulture));
        }

        return result.Append(']').ToString();
    }
}
