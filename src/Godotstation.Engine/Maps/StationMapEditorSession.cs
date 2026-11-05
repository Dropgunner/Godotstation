using System;
using System.Collections.Generic;

namespace Godotstation.Engine.Maps;

/// <summary>
/// The editor's model: the map being edited plus undo/redo history and a dirty flag.
/// Every edit goes through <see cref="Edit"/>, and a drag of the mouse is grouped into one
/// history step with <see cref="BeginStroke"/>/<see cref="EndStroke"/>. Frontends draw and
/// route input; they never mutate the map directly.
/// </summary>
public sealed class StationMapEditorSession
{
    public const int MaximumHistory = 200;

    private readonly LinkedList<StationMap> undo = new();
    private readonly Stack<StationMap> redo = new();
    private bool inStroke;
    private bool strokeChanged;

    public StationMapEditorSession(StationMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Map = map;
    }

    public StationMap Map { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int UndoDepth => undo.Count;

    /// <summary>Raised after the map changed through an edit, undo, redo or load.</summary>
    public event Action? Changed;

    /// <summary>Replaces the map and clears history, as after loading or creating a file.</summary>
    public void Load(StationMap map, bool dirty = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        Map = map;
        undo.Clear();
        redo.Clear();
        inStroke = false;
        strokeChanged = false;
        IsDirty = dirty;
        Changed?.Invoke();
    }

    public void MarkSaved() => IsDirty = false;

    /// <summary>Groups the edits until <see cref="EndStroke"/> into one undo step.</summary>
    public void BeginStroke()
    {
        if (inStroke) return;
        inStroke = true;
        strokeChanged = false;
        PushUndo();
    }

    public void EndStroke()
    {
        if (!inStroke) return;
        inStroke = false;
        if (!strokeChanged) undo.RemoveLast();
    }

    /// <summary>
    /// Applies an edit that reports whether it changed anything. A no-op edit leaves history
    /// untouched; a real edit clears the redo stack.
    /// </summary>
    public bool Edit(Func<StationMap, bool> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!inStroke) PushUndo();
        bool changed = edit(Map);
        if (!changed)
        {
            if (!inStroke) undo.RemoveLast();
            return false;
        }
        strokeChanged = true;
        redo.Clear();
        IsDirty = true;
        Changed?.Invoke();
        return true;
    }

    public bool Undo()
    {
        if (inStroke || undo.Count == 0) return false;
        redo.Push(Map);
        Map = undo.Last!.Value;
        undo.RemoveLast();
        IsDirty = true;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (inStroke || redo.Count == 0) return false;
        undo.AddLast(Map);
        Map = redo.Pop();
        IsDirty = true;
        Changed?.Invoke();
        return true;
    }

    private void PushUndo()
    {
        undo.AddLast(Map.Clone());
        while (undo.Count > MaximumHistory) undo.RemoveFirst();
    }
}
