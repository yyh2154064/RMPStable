using System.Collections.Generic;
namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;
// Map and ink share a single high-frequency transform sample.
internal sealed class MapMotionFrame
{
    public string Session { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Key { get; set; } = "";
    public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
    public List<DrawingSnapshot> Drawings { get; set; } = new();
}
