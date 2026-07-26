using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace DiscordActivity;

internal sealed class DiscordRpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readerCts;
    private string _clientId = "";
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private TaskCompletionSource<bool>? _ready;

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(string clientId, CancellationToken cancellationToken)
    {
        if (IsConnected && _clientId == clientId) return;
        DisposePipe();
        _clientId = clientId;

        Exception? lastError = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(250, cancellationToken);
                _pipe = pipe;
                _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = ReaderLoopAsync(_readerCts.Token);
                await WriteFrameAsync(0, new { v = 1, client_id = clientId }, cancellationToken);
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                lastError = ex;
                DisposePipe();
            }
        }

        throw new IOException(
            "Could not connect to Discord RPC. Confirm the desktop app is running and the Application ID is valid.",
            lastError);
    }

    public Task SetActivityAsync(DetectedActivity detected, CancellationToken cancellationToken)
    {
        var mapping = detected.Mapping;
        var activity = new Dictionary<string, object?>
        {
            ["type"] = 0,
            ["details"] = EmptyToNull(mapping.Details),
            ["state"] = EmptyToNull(mapping.State),
            ["timestamps"] = new { start = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        };

        var artwork = !string.IsNullOrWhiteSpace(mapping.LargeImageKey)
            ? mapping.LargeImageKey.Trim()
            : mapping.ArtworkUrl.Trim();
        if (!string.IsNullOrWhiteSpace(artwork))
        {
            activity["assets"] = new
            {
                large_image = artwork,
                large_text = EmptyToNull(mapping.LargeImageText)
            };
        }

        if (Uri.TryCreate(mapping.ButtonUrl, UriKind.Absolute, out var buttonUri)
            && buttonUri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(mapping.ButtonLabel))
        {
            activity["buttons"] = new[]
            {
                new { label = mapping.ButtonLabel.Trim(), url = buttonUri.ToString() }
            };
        }

        return SendCommandAsync(activity, cancellationToken);
    }

    public Task ClearActivityAsync(CancellationToken cancellationToken) =>
        SendCommandAsync(null, cancellationToken);

    private async Task SendCommandAsync(object? activity, CancellationToken cancellationToken)
    {
        var nonce = Guid.NewGuid().ToString();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[nonce] = completion;
        try
        {
            await WriteFrameAsync(1, new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity },
                nonce
            }, cancellationToken);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Discord did not acknowledge the activity update within five seconds.");
        }
        finally
        {
            _pending.TryRemove(nonce, out _);
        }
    }

    private async Task ReaderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipe?.IsConnected == true)
            {
                var header = new byte[8];
                await ReadExactlyAsync(_pipe, header, cancellationToken);
                var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
                var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
                if (length is < 0 or > 4_000_000)
                    throw new IOException("Discord returned an invalid RPC frame.");
                var payload = new byte[length];
                await ReadExactlyAsync(_pipe, payload, cancellationToken);

                if (opcode == 3)
                {
                    await WriteRawFrameAsync(4, payload, cancellationToken);
                    continue;
                }
                if (opcode == 2)
                    throw new IOException("Discord closed the RPC connection.");
                if (opcode != 1) continue;

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var evt = GetString(root, "evt");
                var nonce = GetString(root, "nonce");
                if (string.Equals(evt, "READY", StringComparison.OrdinalIgnoreCase))
                {
                    _ready?.TrySetResult(true);
                    continue;
                }

                if (string.Equals(evt, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    var message = ExtractError(root);
                    if (!string.IsNullOrWhiteSpace(nonce) && _pending.TryRemove(nonce, out var failed))
                        failed.TrySetException(new InvalidOperationException(message));
                    else
                        _ready?.TrySetException(new InvalidOperationException(message));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(nonce) && _pending.TryRemove(nonce, out var completed))
                    completed.TrySetResult(root.Clone());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _ready?.TrySetException(ex);
            foreach (var pending in _pending.Values)
                pending.TrySetException(ex);
            DisposePipeFromReader();
        }
    }

    private async Task WriteFrameAsync(int opcode, object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await WriteRawFrameAsync(opcode, bytes, cancellationToken);
    }

    private async Task WriteRawFrameAsync(int opcode, byte[] payloadBytes,
        CancellationToken cancellationToken)
    {
        if (_pipe is null || !_pipe.IsConnected)
            throw new IOException("Discord RPC is not connected.");

        var frame = new byte[8 + payloadBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payloadBytes.Length);
        payloadBytes.CopyTo(frame, 8);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _pipe.WriteAsync(frame, cancellationToken);
            await _pipe.FlushAsync(cancellationToken);
        }
        catch
        {
            DisposePipe();
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("Discord RPC disconnected.");
            offset += read;
        }
    }

    private static string ExtractError(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data))
        {
            var message = GetString(data, "message");
            var code = data.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "";
            if (!string.IsNullOrWhiteSpace(message))
                return string.IsNullOrWhiteSpace(code)
                    ? $"Discord rejected the request: {message}"
                    : $"Discord error {code}: {message}";
        }
        return "Discord rejected the Rich Presence request.";
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void DisposePipeFromReader()
    {
        try { _pipe?.Dispose(); }
        catch { }
        _pipe = null;
    }

    private void DisposePipe()
    {
        _readerCts?.Cancel();
        _readerCts?.Dispose();
        _readerCts = null;
        try { _pipe?.Dispose(); }
        catch { }
        _pipe = null;
        foreach (var pending in _pending.Values)
            pending.TrySetCanceled();
        _pending.Clear();
    }

    public void Dispose()
    {
        DisposePipe();
        _writeLock.Dispose();
    }
}
