using Godot;
using RemoveMultiplayerPlayerLimit.Core;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Features.QuickSl;

public sealed class QuickSlModule : IRMPModule
{
	public string Name => "QuickSl";

	public void Initialize(ConfigManager config, ReflectionCache cache)
	{
		QuickSlController.Initialize();
	}

	public Node? CreateNode() => null;

	public void Cleanup() => QuickSlController.Cleanup();
}
