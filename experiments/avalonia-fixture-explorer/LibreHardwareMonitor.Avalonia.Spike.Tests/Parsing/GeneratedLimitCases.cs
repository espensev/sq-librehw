using System.Text;
using System.Text.Json;

namespace LibreHardwareMonitor.Avalonia.Spike.Tests.Parsing;

internal static class GeneratedLimitCases
{
    private const string EmptyDocument = "{\"Children\":[]}";

    public static byte[] Utf8(string json)
    {
        return Encoding.UTF8.GetBytes(json);
    }

    public static string CreateExactByteDocument(int byteCount)
    {
        int minimumBytes = Encoding.UTF8.GetByteCount(EmptyDocument);
        if (byteCount < minimumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        return EmptyDocument + new string(' ', byteCount - minimumBytes);
    }

    public static string CreateDepthDocument(int containerDepth)
    {
        if (containerDepth < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(containerDepth));
        }

        StringBuilder json = new();
        json.Append("{\"Children\":[],\"Unknown\":");
        json.Append('[', containerDepth - 1);
        json.Append('0');
        json.Append(']', containerDepth - 1);
        json.Append('}');
        return json.ToString();
    }

    public static string CreateNodeDocument(int nodeCount)
    {
        if (nodeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount));
        }

        StringBuilder json = new("{\"Children\":[");
        for (int index = 0; index < nodeCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("{\"id\":");
            json.Append(index);
            json.Append(",\"Text\":\"Node ");
            json.Append(index);
            json.Append("\",\"Children\":[]}");
        }

        json.Append("]}");
        return json.ToString();
    }

    public static string CreateNestedChildDocument(int childCount)
    {
        if (childCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(childCount));
        }

        StringBuilder json = new("{\"Children\":[{\"Text\":\"Parent\",\"Children\":[");
        for (int index = 0; index < childCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("{\"Text\":\"Child ");
            json.Append(index);
            json.Append("\",\"Children\":[]}");
        }

        json.Append("]}]}");
        return json.ToString();
    }

    public static string CreateStringValueDocument(int characterCount)
    {
        if (characterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount));
        }

        return $"{{\"Unknown\":\"{new string('s', characterCount)}\",\"Children\":[]}}";
    }

    public static string CreatePropertyNameDocument(int characterCount)
    {
        if (characterCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount));
        }

        return $"{{\"{new string('p', characterCount)}\":0,\"Children\":[]}}";
    }

    public static string CreateSensorDocument(params string[] sensorIds)
    {
        ArgumentNullException.ThrowIfNull(sensorIds);

        StringBuilder json = new("{\"Version\":\"fixture\",\"Children\":[");
        for (int index = 0; index < sensorIds.Length; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("{\"id\":7,\"Text\":\"Sensor ");
            json.Append(index);
            json.Append("\",\"SensorId\":");
            json.Append(JsonSerializer.Serialize(sensorIds[index]));
            json.Append(
                ",\"Type\":\"Temperature\",\"Min\":\"1 C\",\"Value\":\"2 C\",\"Max\":\"3 C\"," +
                "\"RawMin\":1,\"RawValue\":2,\"RawMax\":3,\"Children\":[]}");
        }

        json.Append("]}");
        return json.ToString();
    }

    public sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableReadStream(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            _inner = new MemoryStream(payload, writable: false);
        }

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _inner.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int bytesRead = await _inner
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public sealed class BlockingReadStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Sensitive implementation detail.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<int>(
                new IOException("Sensitive implementation detail."));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
