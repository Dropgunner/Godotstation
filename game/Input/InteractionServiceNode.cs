using Godot;
using Godotstation.Common.Interactions;

namespace Godotstation.Game.Input;

/// <summary>The scene tree's one shared dispatch service (autoload name: Interaction).</summary>
public partial class InteractionServiceNode : Node
{
	public InteractionDispatcher Dispatcher { get; } = new InteractionDispatcher();
}
