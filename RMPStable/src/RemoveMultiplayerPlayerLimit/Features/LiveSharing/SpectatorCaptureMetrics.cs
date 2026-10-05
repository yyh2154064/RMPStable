using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Opt-in development profiling; no logging or sampling during normal play.
internal sealed class SpectatorCaptureMetrics
{
	private long _time, _bytes;
	public Dictionary<string, double> Milliseconds { get; } = new();
	public Dictionary<string, long> AllocatedBytes { get; } = new();
	internal void Begin() { Milliseconds.Clear(); AllocatedBytes.Clear(); _time = Stopwatch.GetTimestamp(); _bytes = GC.GetAllocatedBytesForCurrentThread(); }
	internal void Mark(string stage)
	{
		long time = Stopwatch.GetTimestamp(), bytes = GC.GetAllocatedBytesForCurrentThread();
		Milliseconds[stage] = (time - _time) * 1000d / Stopwatch.Frequency; AllocatedBytes[stage] = bytes - _bytes;
		_time = Stopwatch.GetTimestamp(); _bytes = GC.GetAllocatedBytesForCurrentThread();
	}
}
