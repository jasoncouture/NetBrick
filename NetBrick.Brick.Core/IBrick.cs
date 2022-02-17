using System.Runtime.CompilerServices;

namespace NetBrick.Brick.Core;

public interface IBrick
{
    ValueTask EnsureInitialized();

    ValueTask SetConsumerOffsetAsync(string consumerName, long address,
        CancellationToken cancellationToken);

    ValueTask MakeDurableAsync(CancellationToken cancellationToken);
    ValueTask EnsureDurableAsync(long address, CancellationToken cancellationToken);
    ValueTask<long> WriteBytesAsync(byte[] data, CancellationToken cancellationToken);

    IAsyncEnumerable<(byte[], long)> EnumerateBytesAsync(
        string name,
        long from = long.MinValue,
        long to = long.MaxValue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);
}