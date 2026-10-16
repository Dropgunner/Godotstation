using System;
using System.Collections.Generic;
using Godot;
using Godotstation.Engine.Atmos;
using Godotstation.Engine.Physics;

namespace Godotstation.Game.Atmos;

/// <summary>A Godot-rendered tile that exposes its authoritative gas to the headless simulation.</summary>
public partial class AtmosCellNode2D : Node2D, IAtmosNode
{
    public const float HalfExtent = 0.72f;
    private static readonly Color TileColor = new(0.12f, 0.15f, 0.19f);
    private static readonly Color BorderColor = new(0.28f, 0.34f, 0.39f);
    private GasMix gasMix;
    private IAtmosNode[] neighbors = Array.Empty<IAtmosNode>();
    private Vector3Int tilePosition;
    private Color plasmaOverlay;
    private Color oxygenOverlay;
    private bool burning;
    private bool iced;

    public bool Exists => GodotObject.IsInstanceValid(this);
    public bool IsSetUp { get; private set; }
    public bool IsOccupied { get; set; }
    public bool IsIsolatedNode { get; set; }
    public bool IsSpace { get; set; }
    public Vector3Int TilePosition => tilePosition;
    Vector3Int IAtmosNode.Position => tilePosition;
    public ref GasMix GasMix => ref gasMix;
    public float SolidTemperature { get; set; } = Godotstation.Engine.Atmos.GasMix.StandardTemperature;
    public float SolidHeatCapacity { get; set; } = 1000f;
    public float ThermalConductivity { get; set; } = 0.05f;
    public IReadOnlyList<IAtmosNode> Neighbors => neighbors;

    public void Configure(Vector3Int position, in GasMix initialMix)
    {
        tilePosition = position;
        gasMix = initialMix;
        IsSetUp = true;
        QueueRedraw();
    }

    public void SetNeighbors(IAtmosNode[] tileNeighbors) => neighbors = tileNeighbors;

    public void AddNeighborsToList(List<(IAtmosNode node, bool doEqualise)> nodes)
    {
        foreach (var neighbor in neighbors)
            nodes.Add((neighbor, !neighbor.IsOccupied && !neighbor.IsIsolatedNode));
    }

    public void SetGasOverlays(Color plasma, Color oxygen)
    {
        plasmaOverlay = plasma;
        oxygenOverlay = oxygen;
        QueueRedraw();
    }

    public void SetBurning(bool value)
    {
        if (burning == value) return;
        burning = value;
        QueueRedraw();
    }

    public void SetIced(bool value)
    {
        if (iced == value) return;
        iced = value;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(-HalfExtent, -HalfExtent, HalfExtent * 2f, HalfExtent * 2f);
        DrawRect(rect, TileColor);
        if (oxygenOverlay.A > 0f) DrawRect(rect, oxygenOverlay);
        if (plasmaOverlay.A > 0f) DrawRect(rect, plasmaOverlay);
        DrawRect(rect, BorderColor, false, 0.025f);

        if (burning)
        {
            DrawCircle(new Vector2(0f, 0f), 0.2f, new Color(1f, 0.34f, 0.08f, 0.92f));
            DrawCircle(new Vector2(0f, 0.07f), 0.11f, new Color(1f, 0.78f, 0.2f, 0.95f));
        }

        if (iced)
        {
            var ice = new Color(0.55f, 0.92f, 1f, 0.9f);
            DrawLine(new Vector2(-0.35f, -0.3f), new Vector2(0.35f, 0.3f), ice, 0.06f);
            DrawLine(new Vector2(-0.35f, 0.3f), new Vector2(0.35f, -0.3f), ice, 0.06f);
        }
    }
}
