using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.addons.mega_text;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

internal static class MirrorClock
{
    internal static MegaLabel? Label
    {
        get
        {
            var timer = NRun.Instance?.GlobalUi?.TopBar?.Timer;
            return timer != null && GodotObject.IsInstanceValid(timer) && timer.IsInsideTree()
                ? timer.GetNodeOrNull<MegaLabel>("TimerLabel") : null;
        }
    }
}
