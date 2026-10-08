using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Deliberately contains no Godot objects or live game models. The local prototype
// is handed to the local renderer as a read-only value graph. RoundTrip remains
// available to integration tests to exercise the future transport boundary.
internal sealed class SpectatorSnapshot
{
	public int MapDrawingMode { get; set; }
	public int Schema { get; set; } = 1;
	public SpectatorRevisions Revisions { get; set; } = new();
	public string Session { get; set; } = "";
	public string SourceId { get; set; } = "";
	public List<ParticipantSnapshot> Sources { get; set; } = new();
	public PointerSnapshot Pointer { get; set; } = new();
	public ControlSnapshot Control { get; set; } = new();
	public string Room { get; set; } = "";
	public string Page { get; set; } = "";
	public string Character { get; set; } = "";
	public string Summary { get; set; } = "";
	public string Detail { get; set; } = "";
	public float Width { get; set; } = 1920;
	public float Height { get; set; } = 1080;
	public List<ArtSnapshot> Background { get; set; } = new();
	public List<TextSnapshot> BackgroundLabels { get; set; } = new();
	public List<ArtSnapshot> ForegroundArt { get; set; } = new();
	public List<TextSnapshot> ForegroundLabels { get; set; } = new();
	public List<CreatureSnapshot> Creatures { get; set; } = new();
	public List<CardSnapshot> Cards { get; set; } = new();
	public List<CardSnapshot> Deck { get; set; } = new();
	public List<ItemSnapshot> Items { get; set; } = new();
	public List<ItemSnapshot> Inventory { get; set; } = new();
	public List<string> Choices { get; set; } = new();
	public List<ArtSnapshot> HudArt { get; set; } = new();
	public List<TextSnapshot> HudLabels { get; set; } = new();
	public List<HoverSnapshot> HudHovers { get; set; } = new();
	public List<RelicSnapshot> Relics { get; set; } = new();
	public List<PileSnapshot> Piles { get; set; } = new();
	public List<ArtSnapshot> ModalArt { get; set; } = new();
	public List<TextSnapshot> ModalLabels { get; set; } = new();
	public List<CardSnapshot> ModalCards { get; set; } = new();
	public float[]? DeckButtonRect { get; set; }
	public List<CardSnapshot> UnderlayCards { get; set; } = new();
	public List<ItemSnapshot> UnderlayItems { get; set; } = new();
	public List<ArtSnapshot> RewardArt { get; set; } = new();
	public List<TextSnapshot> RewardLabels { get; set; } = new();
	public List<ArtSnapshot> PageArt { get; set; } = new();
	public List<TextSnapshot> PageLabels { get; set; } = new();
	public List<DrawingSnapshot> Drawings { get; set; } = new();
	public List<HoverSnapshot> Hovers { get; set; } = new();
	public string UnderlayPage { get; set; } = "";
	public List<ArtSnapshot> CombatHudArt { get; set; } = new();
	public List<TextSnapshot> CombatHudLabels { get; set; } = new();
	public string Culture { get; set; } = "en-US";

	internal static SpectatorSnapshot RoundTrip(SpectatorSnapshot source) =>
		JsonSerializer.Deserialize<SpectatorSnapshot>(JsonSerializer.SerializeToUtf8Bytes(source))
		?? throw new InvalidOperationException("Empty spectator snapshot");
}

internal sealed class PointerSnapshot
{
	public string Session { get; set; } = "";
	public string SourceId { get; set; } = "";
	public long Sequence { get; set; }
	public bool Visible { get; set; }
	public float X { get; set; }
	public float Y { get; set; }
	public string Texture { get; set; } = "";
	public float HotspotX { get; set; }
	public float HotspotY { get; set; }
	public float ScaleX { get; set; } = 1;
	public float ScaleY { get; set; } = 1;
}

// Session-local domain revisions, not native instance IDs or network entity IDs.
internal sealed class SpectatorRevisions
{
	public long Hud { get; set; }
	public long Background { get; set; }
	public long Creatures { get; set; }
	public long Screen { get; set; }
	public long Inventory { get; set; }
	public long Offers { get; set; }
	public long Cards { get; set; }
	public long Deck { get; set; }
	public long Sources { get; set; }
}

internal sealed class ParticipantSnapshot
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public bool Simulated { get; set; }
}

internal sealed class CardSnapshot
{
	public string ControlId { get; set; } = "";
	public string ArtKey { get; set; } = "";
	public List<ArtSnapshot> Art { get; set; } = new();
	public List<TextSnapshot> Labels { get; set; } = new();
	public float[]? Transform { get; set; }
	public string Id { get; set; } = "";
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public string Type { get; set; } = "";
	public string Cost { get; set; } = "";
	public string StarCost { get; set; } = "";
	public bool Upgraded { get; set; }
	public bool Ancient { get; set; }
	public bool Sold { get; set; }
	public int? Price { get; set; }
	public string Portrait { get; set; } = "";
	public string Frame { get; set; } = "";
	public string Border { get; set; } = "";
	public string Banner { get; set; } = "";
	public string EnergyIcon { get; set; } = "";
	public string FrameMaterial { get; set; } = "";
	public string BannerMaterial { get; set; } = "";
	public string AncientBorder { get; set; } = "";
	public string AncientText { get; set; } = "";
	public string EnchantmentIcon { get; set; } = "";
	public string EnchantmentAmount { get; set; } = "";
	public int SortCost { get; set; }
	public int SortType { get; set; }
	public float[] CostColor { get; set; } = { 1, 0.965f, 0.886f, 1 };
	public float[] CostOutline { get; set; } = { 0, 0, 0, 1 };
	public float[] StarColor { get; set; } = { 1, 0.965f, 0.886f, 1 };
	public float[] StarOutline { get; set; } = { 0, 0, 0, 1 };
	public List<TipSnapshot> Tips { get; set; } = new();
	public CardSnapshot? Upgrade { get; set; }
	public CardSnapshot? Base { get; set; }
}

internal sealed class ControlSnapshot
{
	public string Context { get; set; } = "";
	public List<ControlActionSnapshot> Actions { get; set; } = new();
	public List<ControlTargetSnapshot> Targets { get; set; } = new();
}

internal sealed class ControlActionSnapshot
{
	public bool MapAttached { get; set; }
	public string Id { get; set; } = "";
	public string Kind { get; set; } = "";
	public string CardId { get; set; } = "";
	public string Label { get; set; } = "";
	public bool Enabled { get; set; }
	public bool RequiresTarget { get; set; }
	public float[] Rect { get; set; } = { 0, 0, 0, 0 };
	public List<string> TargetIds { get; set; } = new();
}

internal sealed class ControlTargetSnapshot
{
	public bool Enemy { get; set; }
	public string Id { get; set; } = "";
	public float[] Rect { get; set; } = { 0, 0, 0, 0 };
}

internal sealed class ItemSnapshot
{
	public List<ArtSnapshot> Art { get; set; } = new();
	public List<TextSnapshot> Labels { get; set; } = new();
	public float[]? Rect { get; set; }
	public string Name { get; set; } = "";
	public string Icon { get; set; } = "";
	public string Text { get; set; } = "";
	public int? Price { get; set; }
	public bool Sold { get; set; }
}

internal sealed class CreatureSnapshot
{
	public string EntityKey { get; set; } = "";
	public List<ArtSnapshot> StateArt { get; set; } = new();
	public List<TextSnapshot> StateLabels { get; set; } = new();
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] HealthRect { get; set; } = { 0, 0, 200, 24 };
	public float[] IntentRect { get; set; } = { 0, 0, 200, 60 };
	public string Name { get; set; } = "";
	public string VisualScene { get; set; } = "";
	public string Skin { get; set; } = "";
	public ArtSnapshot BodyMaterial { get; set; } = new();
	public string Animation { get; set; } = "";
	public bool Player { get; set; }
	public int Hp { get; set; }
	public int MaxHp { get; set; }
	public int Block { get; set; }
	public List<ItemSnapshot> Powers { get; set; } = new();
	public List<ItemSnapshot> Intents { get; set; } = new();
}

internal sealed class TextSnapshot
{
	public string ArtKey { get; set; } = "";
	public float[] LocalColor { get; set; } = { 1, 1, 1, 1 };
	public bool Rich { get; set; }
	public string Text { get; set; } = "";
	public string Font { get; set; } = "";
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] Size { get; set; } = { 0, 0 };
	public float[] Color { get; set; } = { 1, 1, 1, 1 };
	public float[] OutlineColor { get; set; } = { 0, 0, 0, 1 };
	public int FontSize { get; set; } = 24;
	public int OutlineSize { get; set; }
	public int Alignment { get; set; }
	public int VerticalAlignment { get; set; }
	public int WrapMode { get; set; }
}

// Static decorative texture layers, not framebuffer captures. Transforms and
// resource paths let another client draw the same background from its own assets.
internal sealed class ArtSnapshot
{
	public ParticleSnapshot? Particle { get; set; }
	public string Key { get; set; } = "";
	public string Parent { get; set; } = "";
	public bool Group { get; set; }
	public int ClipChildren { get; set; }
	public bool ClipContents { get; set; }
	public bool ZRelative { get; set; } = true;
	public bool BehindParent { get; set; }
	public float[]? Points { get; set; }
	public bool Polygon { get; set; }
	public float LineWidth { get; set; }
	public int BeginCap { get; set; }
	public int EndCap { get; set; }
	public int Joint { get; set; }
	public bool Antialiased { get; set; }
	public bool Solid { get; set; }
	public string Skeleton { get; set; } = "";
	public string AttachmentClass { get; set; } = "";
	public string AttachmentName { get; set; } = "";
	public string Animation { get; set; } = "";
	public bool AnimationLoop { get; set; } = true;
	public float AnimationTime { get; set; }
	public string Texture { get; set; } = "";
	public string Material { get; set; } = "";
	public string Shader { get; set; } = "";
	public List<ShaderValueSnapshot> ShaderValues { get; set; } = new();
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] Rect { get; set; } = { 0, 0, 0, 0 };
	public float[] Tint { get; set; } = { 1, 1, 1, 1 };
	public float[] SelfTint { get; set; } = { 1, 1, 1, 1 };
	public int Z { get; set; }
	public bool FlipH { get; set; }
	public bool FlipV { get; set; }
	public int Stretch { get; set; }
	public float[]? Region { get; set; }
	public int[]? PatchMargins { get; set; }
}

internal sealed class TipSnapshot
{
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public string Icon { get; set; } = "";
	public bool Debuff { get; set; }
	public CardSnapshot? Card { get; set; }
}

internal sealed class DrawingSnapshot
{
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] Size { get; set; } = { 1, 1 };
	public int[] ViewportSize { get; set; } = { 1, 1 };
	public List<ArtSnapshot> Lines { get; set; } = new();
}

internal sealed class ShaderValueSnapshot
{
	public string Name { get; set; } = "";
	public string Kind { get; set; } = "";
	public float[] Values { get; set; } = Array.Empty<float>();
	public string Texture { get; set; } = "";
}

internal sealed class HoverSnapshot
{
	public float[] Rect { get; set; } = { 0, 0, 1, 1 };
	public List<TipSnapshot> Tips { get; set; } = new();
}

internal sealed class PileSnapshot
{
	public string Kind { get; set; } = "";
	public float[] Rect { get; set; } = { 0, 0, 1, 1 };
	public List<CardSnapshot> Cards { get; set; } = new();
}

internal sealed class RelicSnapshot
{
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";
	public string Flavor { get; set; } = "";
	public string Icon { get; set; } = "";
	public string Rarity { get; set; } = "";
	public float[] FrameHsv { get; set; } = { 1, 1, 1 };
	public float[] RarityColor { get; set; } = { 1, 1, 1, 1 };
	public float[] Rect { get; set; } = { 0, 0, 1, 1 };
	public List<TipSnapshot> Tips { get; set; } = new();
	public List<TipSnapshot> ExtraTips { get; set; } = new();
}
