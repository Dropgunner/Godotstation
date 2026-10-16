using System.Collections.Generic;

namespace Godotstation.Engine.Inventory;

/// <summary>Behavior that receives an EMP strength value for a pickupable item.</summary>
public interface IEmpAble
{
    void OnEmp(int empStrength);
}

/// <summary>Provides component-like EMP behaviors attached to an item.</summary>
public interface IEmpAbleProvider
{
    IEnumerable<IEmpAble> EmpBehaviors { get; }
}

/// <summary>World/content policy for determining whether an item resists an EMP.</summary>
public interface IEmpResistancePolicy
{
    bool IsEmpResistant(IPickupable item);
}
