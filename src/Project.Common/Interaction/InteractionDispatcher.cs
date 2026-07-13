using Godotstation.Common.Interaction.Types;
using Godotstation.Common.Interaction.Validation;

namespace Godotstation.Common.Interaction;

/// <summary>
/// Dispatches interactions to registered handlers in priority order.
/// FIDELITY CONTRACT FC-4: Resolution order is PositionalHandApply > HandApply > TileApply.
/// </summary>
public class InteractionDispatcher
{
    private readonly List<IInteractionHandler<PositionalHandApply>> _positionalHandlers = new();
    private readonly List<IInteractionHandler<HandApply>> _handApplyHandlers = new();
    private readonly List<IInteractionHandler<TileApply>> _tileApplyHandlers = new();
    private readonly List<IInteractionHandler<MouseDrop>> _mouseDropHandlers = new();
    private readonly List<IInteractionHandler<HandActivate>> _handActivateHandlers = new();
    private readonly List<IInteractionHandler<InventoryApply>> _inventoryApplyHandlers = new();

    public void RegisterHandler(IInteractionHandler<PositionalHandApply> handler) =>
        InsertSorted(_positionalHandlers, handler);

    public void RegisterHandler(IInteractionHandler<HandApply> handler) =>
        InsertSorted(_handApplyHandlers, handler);

    public void RegisterHandler(IInteractionHandler<TileApply> handler) =>
        InsertSorted(_tileApplyHandlers, handler);

    public void RegisterHandler(IInteractionHandler<MouseDrop> handler) =>
        InsertSorted(_mouseDropHandlers, handler);

    public void RegisterHandler(IInteractionHandler<HandActivate> handler) =>
        InsertSorted(_handActivateHandlers, handler);

    public void RegisterHandler(IInteractionHandler<InventoryApply> handler) =>
        InsertSorted(_inventoryApplyHandlers, handler);

    /// <summary>
    /// Attempts to resolve a click interaction.
    /// FIDELITY CONTRACT FC-4: Tries PositionalHandApply first, then HandApply, then TileApply.
    /// Returns true if any handler consumed the interaction.
    /// </summary>
    public bool TryResolveClick(
        IPerformer performer,
        IInteractable? handObject,
        IInteractable? targetObject,
        System.Numerics.Vector2 targetPosition,
        Intent intent,
        bool isAltClick,
        NetworkSide side)
    {
        // FIDELITY CONTRACT FC-5: Ghost state blocks all interactions
        if (performer.IsGhosting) return false;

        // Priority 1: PositionalHandApply
        var positional = new PositionalHandApply(performer, handObject, targetObject, targetPosition, intent, isAltClick);
        if (InteractionValidator.CanInteract(positional, side))
        {
            foreach (var handler in ResolveOrder(_positionalHandlers, handObject, targetObject))
            {
                if (handler.WillInteract(positional, side))
                {
                    handler.ServerPerformInteraction(positional);
                    return true;
                }
            }
        }

        // Priority 2: HandApply
        var handApply = new HandApply(performer, handObject, targetObject, intent, isAltClick);
        if (InteractionValidator.CanInteract(handApply, side))
        {
            foreach (var handler in ResolveOrder(_handApplyHandlers, handObject, targetObject))
            {
                if (handler.WillInteract(handApply, side))
                {
                    handler.ServerPerformInteraction(handApply);
                    return true;
                }
            }
        }

        // Priority 3: TileApply
        var tileApply = new TileApply(performer, targetPosition, handObject);
        if (targetObject != null) // Only try TileApply if there's a tile target
        {
            foreach (var handler in _tileApplyHandlers)
            {
                if (handler.WillInteract(tileApply, side))
                {
                    handler.ServerPerformInteraction(tileApply);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to resolve a MouseDrop interaction.
    /// </summary>
    public bool TryResolveMouseDrop(
        IPerformer performer,
        IInteractable droppedObject,
        IInteractable targetObject,
        NetworkSide side)
    {
        var mouseDrop = new MouseDrop(performer, droppedObject, targetObject);
        if (!InteractionValidator.CanInteract(mouseDrop, side)) return false;

        foreach (var handler in _mouseDropHandlers)
        {
            if (handler.WillInteract(mouseDrop, side))
            {
                handler.ServerPerformInteraction(mouseDrop);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to resolve a HandActivate (Z key) interaction.
    /// </summary>
    public bool TryResolveHandActivate(IPerformer performer, IInteractable? handObject, NetworkSide side)
    {
        if (!InteractionValidator.CanInteract(performer, side)) return false;

        var handActivate = new HandActivate(performer, handObject);
        foreach (var handler in _handActivateHandlers)
        {
            if (handler.WillInteract(handActivate, side))
            {
                handler.ServerPerformInteraction(handActivate);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Attempts to resolve an InventoryApply interaction.
    /// </summary>
    public bool TryResolveInventoryApply(
        IPerformer performer,
        IInteractable? fromSlotItem,
        IInteractable? toSlotItem,
        NetworkSide side)
    {
        if (!InteractionValidator.CanInteract(performer, side)) return false;

        var inventoryApply = new InventoryApply(performer, fromSlotItem, toSlotItem);
        foreach (var handler in _inventoryApplyHandlers)
        {
            if (handler.WillInteract(inventoryApply, side))
            {
                handler.ServerPerformInteraction(inventoryApply);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Event raised when an interaction is resolved (for UI/audio feedback).
    /// </summary>
    public event Action<Types.Interaction>? OnInteractionResolved;

    private static void InsertSorted<T>(List<IInteractionHandler<T>> list, IInteractionHandler<T> handler)
        where T : Types.Interaction
    {
        int index = list.FindIndex(h => h.Priority > handler.Priority);
        if (index < 0) list.Add(handler);
        else list.Insert(index, handler);
    }

    /// <summary>
    /// Orders handlers by ownership rather than the flat Priority list:
    /// FIDELITY CONTRACT FC-9: the target's first-interactable handler claims the
    /// click unconditionally, before anything else.
    /// FIDELITY CONTRACT FC-8: the held item's handler gets first refusal over the
    /// target's remaining handlers.
    /// Handlers with no declared owner (the Wave 1-3 default) are checked last, in
    /// their existing Priority order, preserving prior behavior for FC-1..FC-7.
    /// </summary>
    private static IEnumerable<IInteractionHandler<T>> ResolveOrder<T>(
        List<IInteractionHandler<T>> handlers,
        IInteractable? handObject,
        IInteractable? targetObject) where T : Types.Interaction
    {
        var owned = handlers.OfType<IOwnedInteractionHandler<T>>().ToList();
        var unowned = handlers.Except(owned);

        foreach (var h in owned.Where(h => Equals(h.Owner, targetObject) && h.IsFirstInteractable))
            yield return h;

        foreach (var h in owned.Where(h => Equals(h.Owner, handObject)))
            yield return h;

        foreach (var h in owned.Where(h => Equals(h.Owner, targetObject) && !h.IsFirstInteractable))
            yield return h;

        foreach (var h in unowned)
            yield return h;
    }
}
