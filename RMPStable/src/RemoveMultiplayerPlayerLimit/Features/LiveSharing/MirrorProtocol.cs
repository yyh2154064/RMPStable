using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RemoveMultiplayerPlayerLimit.Features.LiveSharing;

// Source -> renderer is the only direction in which game state may travel.
// Native engine packets remain version-specific; never accept a cross-build peer.
internal sealed class MirrorMessage
{
    public int Protocol { get; set; } = 2;
    public string Session { get; set; } = "";
    public string Secret { get; set; } = "";
    public ulong Source { get; set; }
    public long Sequence { get; set; }
    public long Generation { get; set; }
    public string Kind { get; set; } = "";
    public int Process { get; set; }
    public long Window { get; set; }
    public long Epoch { get; set; }
    public long Request { get; set; }
    public int Index { get; set; } = -1;
    public int TargetIndex { get; set; } = -1;
    public bool Control { get; set; }
    public bool FastRestore { get; set; }
    public bool Accepted { get; set; }
    public bool DisplayReady { get; set; }
    public bool Attached { get; set; }
    public bool WindowVisible { get; set; }
    public bool TargetArrowVisible { get; set; }
    public string Presentation { get; set; } = "";
    public long DrawFrames { get; set; }
    public string Phase { get; set; } = "";
    public int[] Clip { get; set; } = Array.Empty<int>();
    public int CornerWidth { get; set; }
    public int CornerHeight { get; set; }
    public double Fps { get; set; }
    public double ProcessMs { get; set; }
    public string Build { get; set; } = "";
    public string Hash { get; set; } = "";
    public string DrawingHash { get; set; } = "";
    public int Events { get; set; }
    public bool Idle { get; set; }
    public string Room { get; set; } = "";
    public string Model { get; set; } = "";
    public string NodeKey { get; set; } = "";
    public string Value { get; set; } = "";
    public List<MirrorOperation> Operations { get; set; } = new();
    public bool MapOpen { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public float X { get; set; }
    public float Y { get; set; }
    public float StartX { get; set; }
    public float StartY { get; set; }
    public int Highlight { get; set; }
    public List<MirrorHit> Hits { get; set; } = new();
}

internal sealed class MirrorHit
{
    public string NodeKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public int Index { get; set; } = -1;
    public float[] Rect { get; set; } = Array.Empty<float>();
}

internal sealed class MirrorIdentity
{
    internal readonly string Session, Secret;
    internal readonly ulong Source;
    internal long Generation;
    private long _incoming;
    internal MirrorIdentity(string session, string secret, ulong source)
    { Session = session; Secret = secret; Source = source; }
    internal void Validate(MirrorMessage message)
    {
        if (message.Protocol != 2 || message.Session != Session || message.Source != Source ||
            !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(message.Secret), System.Text.Encoding.UTF8.GetBytes(Secret)))
            throw new InvalidDataException("Mirror identity mismatch");
        if (message.Sequence != _incoming + 1) throw new InvalidDataException("Mirror sequence mismatch");
        _incoming = message.Sequence;
    }
    internal bool Current(MirrorMessage message) => message.Generation == Generation;
}

internal sealed class MirrorWire : IDisposable
{
    internal const int MaxPacket = 32 * 1024 * 1024;
    internal readonly MirrorIdentity Identity;
    private readonly PipeStream _pipe;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1);
    private long _outgoing;
    private int _pending;
    internal volatile bool Connected;
    internal string Error { get; private set; } = "";
    internal readonly ConcurrentQueue<MirrorMessage> Incoming = new();
    internal int Pending => _pending;
    internal MirrorWire(PipeStream pipe, MirrorIdentity identity) { _pipe = pipe; Identity = identity; }
    internal async Task ConnectAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            // Pipe I/O must not consume Godot synchronization-context turns.
            // Only the concurrent incoming queue crosses into the game thread.
            if (_pipe is NamedPipeServerStream server) await server.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            else await ((NamedPipeClientStream)_pipe).ConnectAsync(timeout.Token).ConfigureAwait(false);
            Connected = true;
            await ReadLoop().ConfigureAwait(false);
        }
        catch (Exception e) { if (!_stop.IsCancellationRequested) Error = e.GetType().Name + ": " + e.Message; }
        finally { Connected = false; }
    }
    internal Task Send(MirrorMessage message)
    {
        // Serialize on the caller's thread so ordered state/event publication
        // cannot be reordered by independent Task.Run scheduling.
        message.Session = Identity.Session; message.Secret = Identity.Secret; message.Source = Identity.Source;
        message.Sequence = Interlocked.Increment(ref _outgoing);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(message);
        if (data.Length > MaxPacket) throw new InvalidDataException("Mirror packet too large");
        Interlocked.Increment(ref _pending);
        return Write(data);
    }
    private async Task Write(byte[] data)
    {
        bool locked = false;
        try
        {
            await _sendLock.WaitAsync(_stop.Token).ConfigureAwait(false); locked = true;
            await _pipe.WriteAsync(BitConverter.GetBytes(data.Length), _stop.Token).ConfigureAwait(false);
            await _pipe.WriteAsync(data, _stop.Token).ConfigureAwait(false); await _pipe.FlushAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception e) { if (!_stop.IsCancellationRequested) Error = e.GetType().Name + ": " + e.Message; Connected = false; }
        finally { if (locked) _sendLock.Release(); Interlocked.Decrement(ref _pending); }
    }
    private async Task ReadExactly(byte[] data)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int read = await _pipe.ReadAsync(data.AsMemory(offset), _stop.Token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Mirror peer disconnected");
            offset += read;
        }
    }
    private async Task ReadLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            var length = new byte[4]; await ReadExactly(length).ConfigureAwait(false); int size = BitConverter.ToInt32(length);
            if (size is <= 0 or > MaxPacket) throw new InvalidDataException("Mirror packet length invalid");
            var data = new byte[size]; await ReadExactly(data).ConfigureAwait(false);
            var message = JsonSerializer.Deserialize<MirrorMessage>(data) ?? throw new InvalidDataException("Empty mirror packet");
            Identity.Validate(message);
            if (Incoming.Count >= 128) throw new InvalidDataException("Mirror receive backlog exceeded");
            Incoming.Enqueue(message);
        }
    }
    public void Dispose() { _stop.Cancel(); Connected = false; _pipe.Dispose(); }
}
