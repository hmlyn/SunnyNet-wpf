namespace SunnyNet.Wpf.Models;

public sealed class HexVirtualDataSource
{
    public int TotalLength { get; init; }

    public int HeaderLength { get; init; }

    public byte[] HeaderBytes { get; init; } = Array.Empty<byte>();

    public Func<int, int, CancellationToken, Task<byte[]>>? ReadBodyRangeAsync { get; init; }

    public async Task<byte[]> ReadRangeAsync(int offset, int count, CancellationToken cancellationToken)
    {
        if (count <= 0 || offset < 0 || offset >= TotalLength)
        {
            return Array.Empty<byte>();
        }

        int safeCount = Math.Min(count, TotalLength - offset);
        int headerCount = 0;
        byte[] headerChunk = Array.Empty<byte>();
        if (offset < HeaderLength)
        {
            headerCount = Math.Min(safeCount, HeaderLength - offset);
            headerChunk = new byte[headerCount];
            Buffer.BlockCopy(HeaderBytes, offset, headerChunk, 0, Math.Min(headerCount, HeaderBytes.Length - offset));
        }

        int bodyCount = safeCount - headerCount;
        if (bodyCount <= 0)
        {
            return headerChunk;
        }

        byte[] bodyChunk = ReadBodyRangeAsync is null
            ? Array.Empty<byte>()
            : await ReadBodyRangeAsync(offset + headerCount - HeaderLength, bodyCount, cancellationToken);
        if (headerChunk.Length == 0)
        {
            return bodyChunk;
        }

        byte[] result = new byte[headerChunk.Length + bodyChunk.Length];
        Buffer.BlockCopy(headerChunk, 0, result, 0, headerChunk.Length);
        if (bodyChunk.Length > 0)
        {
            Buffer.BlockCopy(bodyChunk, 0, result, headerChunk.Length, bodyChunk.Length);
        }

        return result;
    }
}
