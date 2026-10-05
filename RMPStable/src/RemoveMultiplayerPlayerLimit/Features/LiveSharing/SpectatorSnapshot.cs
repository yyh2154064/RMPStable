using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Deliberately contains no Godot objects or live game models. The local prototype
// passes through JSON before rendering, exercising the future transport boundary.
internal sealed class SpectatorSnapshot
{
	public int Schema { get; set; } = 1;
	public string Session { get; set; } = "";
	public string Room { get; set; } = "";
	public string Page { get; set; } = "";
	public string Character { get; set; } = "";
	public string Summary { get; set; } = "";
	public string Detail { get; set; } = "";
	public float Width { get; set; } = 1920;
	public float Height { get; set; } = 1080;
	public List<ArtSnapshot> Background { get; set; } = new();
	public List<CreatureSnapshot> Creatures { get; set; } = new();
	public List<CardSnapshot> Cards { get; set; } = new();
	public List<CardSnapshot> Deck { get; set; } = new();
	public List<ItemSnapshot> Items { get; set; } = new();
	public List<ItemSnapshot> Inventory { get; set; } = new();
	public List<string> Choices { get; set; } = new();
	public List<ArtSnapshot> HudArt { get; set; } = new();
	public List<TextSnapshot> HudLabels { get; set; } = new();
	public float[]? DeckButtonRect { get; set; }
	public List<CardSnapshot> UnderlayCards { get; set; } = new();
	public List<ItemSnapshot> UnderlayItems { get; set; } = new();
	public List<ArtSnapshot> RewardArt { get; set; } = new();
	public List<TextSnapshot> RewardLabels { get; set; } = new();

	internal static SpectatorSnapshot RoundTrip(SpectatorSnapshot source) =>
		JsonSerializer.Deserialize<SpectatorSnapshot>(JsonSerializer.Serialize(source))
		?? throw new InvalidOperationException("Empty spectator snapshot");
}

internal sealed class CardSnapshot
{
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
	public List<ArtSnapshot> StateArt { get; set; } = new();
	public List<TextSnapshot> StateLabels { get; set; } = new();
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] HealthRect { get; set; } = { 0, 0, 200, 24 };
	public float[] IntentRect { get; set; } = { 0, 0, 200, 60 };
	public string Name { get; set; } = "";
	public string VisualScene { get; set; } = "";
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
}

// Static decorative texture layers, not framebuffer captures. Transforms and
// resource paths let another client draw the same background from its own assets.
internal sealed class ArtSnapshot
{
	public bool Solid { get; set; }
	public string Skeleton { get; set; } = "";
	public string Animation { get; set; } = "";
	public string Texture { get; set; } = "";
	public string Material { get; set; } = "";
	public float[] Transform { get; set; } = { 1, 0, 0, 1, 0, 0 };
	public float[] Rect { get; set; } = { 0, 0, 0, 0 };
	public float[] Tint { get; set; } = { 1, 1, 1, 1 };
	public int Z { get; set; }
	public bool FlipH { get; set; }
	public bool FlipV { get; set; }
	public int Stretch { get; set; }
	public float[]? Region { get; set; }
	public int[]? PatchMargins { get; set; }
}
