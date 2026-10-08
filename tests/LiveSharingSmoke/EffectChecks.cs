using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

public static partial class Smoke
{
	private static object EffectCapture() => Field("_source")!.GetType().GetMethod("CaptureEffects", Instance)!.Invoke(Field("_source"), null)!;
	private static void EffectRoundTrip()
	{
		var frame = EffectCapture();
		var json = System.Text.Json.JsonSerializer.Serialize(frame);
		frame = System.Text.Json.JsonSerializer.Deserialize(json, frame.GetType())!;
		ControlView.GetType().GetMethod("UpdateEffects", Instance)!.Invoke(ControlView, new[] { frame });
	}
	private static async Task CheckEffects()
	{
		await RefreshControl();
		var room = NCombatRoom.Instance!;
		var front = (Control)ControlView.GetType().GetField("_effectFront", Instance)!.GetValue(ControlView)!;
		var back = (Control)ControlView.GetType().GetField("_effectBack", Instance)!.GetValue(ControlView)!;
		var impact = NPoisonImpactVfx.Create(new Vector2(1300, 600))!;
		room.CombatVfxContainer.AddChild(impact);
		var missile = NSmallMagicMissileVfx.Create(new Vector2(1400, 620), Colors.Orange)!;
		room.CombatVfxContainer.AddChild(missile);
		var shiv = NShivThrowVfx.Create(new Vector2(500, 600), new Vector2(1300, 600), Colors.Yellow)!;
		room.BackCombatVfxContainer.AddChild(shiv);
		// A CPU emitter exercises the fallback rendering path and pathless
		// gradient/curve resources in addition to the real native GPU scenes.
		var cpu = new CpuParticles2D { Amount = 8, Lifetime = 3, Emitting = true, Position = new Vector2(700, 500), ColorRamp = new Gradient { Colors = new[] { Colors.Yellow, Colors.Red }, Offsets = new[] { 0f, 1f } } };
		room.CombatVfxContainer.AddChild(cpu);
		await Frames(2); EffectRoundTrip();
		Check(Descendants(front).OfType<GpuParticles2D>().Any() && Descendants(back).OfType<GpuParticles2D>().Any(), "native impact and thrown projectile particles reach separate front/back layers");
		Check(!Descendants(front).Concat(Descendants(back)).Any(n => n.GetType().Namespace?.StartsWith("MegaCrit.Sts2.Core.Nodes.Vfx") == true), "spectator effect copies contain no native VFX scripts or gameplay callbacks");
		var renderedCpu = Descendants(front).OfType<CpuParticles2D>().Single();
		Check(renderedCpu.Amount == cpu.Amount && renderedCpu.ColorRamp != cpu.ColorRamp && renderedCpu.ColorRamp.Colors.SequenceEqual(cpu.ColorRamp.Colors), "CPU particles and private pathless gradient survive JSON transport");
		var nativeGpu = Descendants(impact).OfType<GpuParticles2D>().First(p => p.ProcessMaterial is ParticleProcessMaterial);
		var rendererLayer = ControlView.GetType().GetField("_retainedArt", Instance)!.GetValue(ControlView)!;
		// Identify the particle by source instance key in the retained draw index.
		var layer = ((IDictionary)rendererLayer)[front.GetInstanceId()]!;
		var index = (IDictionary)layer.GetType().GetProperty("Index", Instance)!.GetValue(layer)!;
		var renderedGpu = (GpuParticles2D)index[nativeGpu.GetInstanceId().ToString()]!.GetType().GetProperty("Drawing", Instance)!.GetValue(index[nativeGpu.GetInstanceId().ToString()])!;
		ulong retainedId = renderedGpu.GetInstanceId();
		Check(renderedGpu.ProcessMaterial != nativeGpu.ProcessMaterial && renderedGpu.Amount == nativeGpu.Amount && renderedGpu.Lifetime == nativeGpu.Lifetime, "GPU process material is private and emitter configuration matches native scene");
		var material = (ParticleProcessMaterial)nativeGpu.ProcessMaterial;
		Color original = material.Color; material.Color = Colors.Cyan; EffectRoundTrip();
		Check(((ParticleProcessMaterial)renderedGpu.ProcessMaterial).Color == Colors.Cyan && renderedGpu.GetInstanceId() == retainedId, "projectile recolor updates private material without rebuilding emitter"); material.Color = original;
		var timing = Stopwatch.StartNew();
		for (int i = 0; i < 12; i++) EffectRoundTrip();
		timing.Stop(); GD.Print("[LiveSharingSmoke] VFX serialized capture/apply mean ms=" + (timing.Elapsed.TotalMilliseconds / 12).ToString("F3"));
		Check(renderedGpu.GetInstanceId() == retainedId && renderedGpu.IsInsideTree(), "60 Hz effect refresh retains particle simulation rather than restarting every sample");
		cpu.Emitting = false; cpu.SpeedScale = 0.5f; cpu.Position = new Vector2(840, 520); EffectRoundTrip();
		var page = (Control)ControlView.GetType().GetField("_page", Instance)!.GetValue(ControlView)!;
		Check(!renderedCpu.Emitting && renderedCpu.SpeedScale == cpu.SpeedScale && (page.GetGlobalTransform().AffineInverse() * renderedCpu.GlobalPosition).DistanceTo(cpu.GlobalPosition) < 0.01, "emission stop, speed and moving emitter use original viewport coordinates after panel scaling");
		Check((double)_controller.GetField("MotionInterval", Static)!.GetRawConstantValue()! == 1d / 60, "motion channel targets 60 updates per second");
		if (DisplayServer.GetName() == "headless")
		{
			int previous = Engine.MaxFps;
			try
			{
				Engine.MaxFps = 60; long samples = (long)Field("_motionSamples")!; var clock = Stopwatch.StartNew();
				double capture = 0, apply = 0;
				for (int i = 0; i < 120; i++) { await Game.ToSignal(Game.GetTree(), SceneTree.SignalName.ProcessFrame); capture += (double)Field("_lastEffectCpuMs")!; apply += (double)Field("_lastEffectRenderMs")!; }
				clock.Stop(); double rate = ((long)Field("_motionSamples")! - samples) / clock.Elapsed.TotalSeconds;
				GD.Print("[LiveSharingSmoke] 60 FPS headless motion samples/s=" + rate.ToString("F1") + " mean capture ms=" + (capture / 120).ToString("F3") + " mean apply ms=" + (apply / 120).ToString("F3"));
				Check(rate >= 50 && rate <= 65, "isolated 60 FPS engine advances motion channel at about 60 Hz");
			}
			finally { Engine.MaxFps = previous; }
		}
		foreach (var node in new Node[] { impact, missile, shiv, cpu }) if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) { node.GetParent()?.RemoveChild(node); node.QueueFree(); }
		await Frames(2); EffectRoundTrip();
		Check(!Descendants(front).OfType<CpuParticles2D>().Any(), "expired effects remove retained particles and their resource subscriptions");
	}
}
