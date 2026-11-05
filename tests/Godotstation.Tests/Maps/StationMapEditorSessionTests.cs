using Godotstation.Engine.Entities;
using Godotstation.Engine.Maps;
using Xunit;

namespace Godotstation.Tests.Maps;

public sealed class StationMapEditorSessionTests
{
    private static StationMapEditorSession NewSession() => new(StationMaps.CreateBlank(9, 9));

    [Fact]
    public void Edit_RecordsHistoryOnlyWhenSomethingChanged()
    {
        StationMapEditorSession session = NewSession();
        int changedEvents = 0;
        session.Changed += () => changedEvents++;

        Assert.False(session.Edit(map => map.ClearWall(0, 0)));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        Assert.Equal(0, changedEvents);

        Assert.True(session.Edit(map => map.SetWall(0, 1)));
        Assert.True(session.CanUndo);
        Assert.True(session.IsDirty);
        Assert.Equal(1, changedEvents);
    }

    [Fact]
    public void Stroke_GroupsManyEditsIntoOneUndoStep()
    {
        StationMapEditorSession session = NewSession();
        session.BeginStroke();
        for (int x = -3; x <= 3; x++) session.Edit(map => map.SetWall(x, 2));
        session.EndStroke();

        Assert.Equal(1, session.UndoDepth);
        Assert.True(session.Undo());
        Assert.False(session.Map.IsWall(0, 2));
        Assert.True(session.Redo());
        Assert.True(session.Map.IsWall(3, 2));
    }

    [Fact]
    public void EmptyStroke_LeavesNoHistory()
    {
        StationMapEditorSession session = NewSession();
        session.BeginStroke();
        session.Edit(map => map.ClearWall(0, 0));
        session.EndStroke();
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_Redo_AndNewEditsClearRedo()
    {
        StationMapEditorSession session = NewSession();
        session.Edit(map => map.PlaceEntity(StationEntityKind.Wrench, 1, 1) != null);
        session.Edit(map => map.AddSpawn(2, 2));

        Assert.True(session.Undo());
        Assert.False(session.Map.HasSpawnAt(2, 2));
        Assert.NotNull(session.Map.GetEntityAt(1, 1));
        Assert.True(session.CanRedo);

        session.Edit(map => map.SetWall(3, 3));
        Assert.False(session.CanRedo);
        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.Null(session.Map.GetEntityAt(1, 1));
        Assert.False(session.Undo());
    }

    [Fact]
    public void Load_ReplacesTheMapAndClearsHistory()
    {
        StationMapEditorSession session = NewSession();
        session.Edit(map => map.SetWall(1, 1));
        session.Load(StationMaps.CreateDefault());

        Assert.Equal("Playtest Station", session.Map.Name);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        session.Edit(map => map.SetWall(0, 0));
        session.MarkSaved();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void History_IsCapped()
    {
        StationMapEditorSession session = NewSession();
        for (int i = 0; i < StationMapEditorSession.MaximumHistory + 25; i++)
        {
            int x = (i % 7) - 3, y = ((i / 7) % 7) - 3;
            session.Edit(map => map.IsWall(x, y) ? map.ClearWall(x, y) : map.SetWall(x, y));
        }
        Assert.Equal(StationMapEditorSession.MaximumHistory, session.UndoDepth);
    }
}
