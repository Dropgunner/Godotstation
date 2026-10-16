using System.Collections.Generic;

namespace Godotstation.Engine.Inventory;

/// <summary>World queries needed to merge a dropped stack with nearby stacks.</summary>
public interface IGroundStackWorld
{
    /// <summary>True when this item's auto-stack setting and world position allow merging.</summary>
    bool CanAutoStack(IPickupable item);

    /// <summary>Returns stacks at the item's current ground position in deterministic order.</summary>
    IEnumerable<IPickupable> GetGroundStacks(IPickupable item);

    /// <summary>True once a candidate stack has finished initialization and can be merged.</summary>
    bool IsStackInitialized(IPickupable item);
}
