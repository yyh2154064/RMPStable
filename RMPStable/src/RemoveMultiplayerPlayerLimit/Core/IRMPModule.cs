using Godot;
using RemoveMultiplayerPlayerLimit.Infrastructure;

namespace RemoveMultiplayerPlayerLimit.Core;

public interface IRMPModule
{
	string Name { get; }

	void Initialize(ConfigManager config, ReflectionCache cache);

	Node? CreateNode();

	void Cleanup();
}
