namespace Godotstation.Engine.Inventory;

/// <summary>Network-neutral request to transfer from one registered slot to another.</summary>
public readonly record struct InventoryTransferRequest(int FromSlotId, int ToSlotId);

/// <summary>Resolves transport slot identifiers to this server's authoritative slots.</summary>
public interface IInventoryTransferSlotResolver
{
    bool TryResolve(int slotId, out ItemSlot? slot);
}

/// <summary>Transport seam for sending a client transfer request to the server.</summary>
public interface IInventoryTransferRequestTransport
{
    void Send(InventoryTransferRequest request);
}

/// <summary>Server-side check that binds a transfer request to its authenticated actor.</summary>
public interface IInventoryTransferRequestAuthorization
{
    bool CanTransfer(string actorId, ItemSlot fromSlot, ItemSlot toSlot);
}
