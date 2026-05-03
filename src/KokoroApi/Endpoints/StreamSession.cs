using KokoroApi.Services;
using KokoroApi.Streaming;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace KokoroApi.Endpoints;

internal sealed class StreamSession
{
    private readonly WebSocket _ws;
    private readonly IKokoroSynthesizer _synth;
    private readonly KokoroOptions _opts;
    private readonly ILogger _log;
    private readonly TextSegmenter _segmenter;
    private readonly Channel<OutboundMessage> _outbox = Channel.CreateUnbounded<OutboundMessage>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly Channel<PendingSegment> _segments = Channel.CreateUnbounded<PendingSegment>(
        new UnboundedChannelOptions { SingleReader = true });

    private string _voice;
    private float _speed;
    private int _nextId = 1;
    private CancellationTokenSource _segmentCts = new();

    public StreamSession(WebSocket ws, IKokoroSynthesizer synth, KokoroOptions opts, ILogger log)
    {
        _ws = ws;
        _synth = synth;
        _opts = opts;
        _log = log;
        _segmenter = new TextSegmenter(opts.MinSegmentChars, opts.MaxBufferChars);
        _voice = opts.DefaultVoice;
        _speed = opts.DefaultSpeed;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var ready = await _synth.WaitReadyAsync(ct);
        if (!ready)
        {
            await SendErrorAndClose("model not ready", ct);
            return;
        }

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadLoopAsync(sessionCts.Token);
        var dispatcher = DispatchLoopAsync(sessionCts.Token);
        var writer = WriteLoopAsync(sessionCts.Token);

        try
        {
            await Task.WhenAny(reader, dispatcher, writer);
        }
        finally
        {
            sessionCts.Cancel();
            _outbox.Writer.TryComplete();
            _segments.Writer.TryComplete();
            try
            {
                await Task.WhenAll(reader, dispatcher, writer);
            }
            catch
            {
            }

            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[8 * 1024];
        var mem = new MemoryStream();
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            mem.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(buf, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                mem.Write(buf, 0, result.Count);
                if (mem.Length > 16 * 1024)
                {
                    await SendError("frame too large");
                    return;
                }
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            await HandleClientMessage(mem.ToArray());
        }
    }

    private async Task HandleClientMessage(byte[] payload)
    {
        ClientMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<ClientMessage>(payload, StreamSerialization.Json);
        }
        catch (JsonException ex)
        {
            await SendError("invalid json: " + ex.Message);
            return;
        }

        if (msg is null) return;

        switch (msg.Type)
        {
            case "config":
                if (!string.IsNullOrWhiteSpace(msg.Voice)) _voice = msg.Voice!;
                if (msg.Speed is { } s && s > 0) _speed = s;
                break;
            case "text":
                if (!string.IsNullOrEmpty(msg.Delta))
                {
                    var segs = _segmenter.Append(msg.Delta);
                    foreach (var seg in segs) EnqueueSegment(seg);
                }

                break;
            case "flush":
                foreach (var seg in _segmenter.Flush()) EnqueueSegment(seg);
                break;
            case "cancel":
                _segmenter.Reset();
                Interlocked.Exchange(ref _segmentCts, new CancellationTokenSource()).Cancel();
                while (_segments.Reader.TryRead(out _))
                { /* drain */
                }

                break;
            default:
                await SendError("unknown message type: " + msg.Type);
                break;
        }
    }

    private void EnqueueSegment(string text)
    {
        var id = Interlocked.Increment(ref _nextId) - 1;
        _segments.Writer.TryWrite(new PendingSegment(id, text, _voice, _speed));
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        await foreach (var seg in _segments.Reader.ReadAllAsync(ct))
        {
            using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _segmentCts.Token);
            await SendJson(new ServerMessage { Type = "segment_start", Id = seg.Id, Text = seg.Text }, ct);
            try
            {
                var samples = await _synth.SynthesizeSegmentAsync(seg.Text, seg.Voice, seg.Speed, jobCts.Token);
                if (samples.Length > 0)
                {
                    var pcm = PcmEncoder.FloatToInt16Bytes(samples);
                    await _outbox.Writer.WriteAsync(OutboundMessage.Binary(pcm), ct);
                }

                await SendJson(new ServerMessage { Type = "segment_end", Id = seg.Id }, ct);
            }
            catch (OperationCanceledException)
            {
                await SendJson(new ServerMessage { Type = "segment_end", Id = seg.Id }, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Segment {Id} failed.", seg.Id);
                await SendJson(new ServerMessage { Type = "error", Message = ex.Message }, ct);
            }
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        await foreach (var msg in _outbox.Reader.ReadAllAsync(ct))
        {
            if (_ws.State != WebSocketState.Open) return;
            await _ws.SendAsync(msg.Bytes, msg.IsBinary ? WebSocketMessageType.Binary : WebSocketMessageType.Text, endOfMessage: true, ct);
        }
    }

    private Task SendJson(ServerMessage m, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(m, StreamSerialization.Json);
        return _outbox.Writer.WriteAsync(OutboundMessage.Text(bytes), ct).AsTask();
    }

    private Task SendError(string message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ServerMessage { Type = "error", Message = message }, StreamSerialization.Json);
        return _outbox.Writer.WriteAsync(OutboundMessage.Text(bytes)).AsTask();
    }

    private async Task SendErrorAndClose(string message, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new ServerMessage { Type = "error", Message = message }, StreamSerialization.Json);
            if (_ws.State == WebSocketState.Open)
                await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch
        {
        }

        try
        {
            await _ws.CloseAsync(WebSocketCloseStatus.InternalServerError, message, ct);
        }
        catch
        {
        }
    }
}
