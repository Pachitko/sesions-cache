using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Server.Extensions;

public static class ChannelExtensions
{
    public static IAsyncEnumerable<T[]> ReadAllBatches<T>(
        this ChannelReader<T> source, int batchSize, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        
        return Implementation();

        async IAsyncEnumerable<T[]> Implementation(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                List<T> buffer = new();
                while (true)
                {
                    var token = buffer.Count == 0 ? cancellationToken : timerCts.Token;
                    (T Value, bool HasValue)? item;

                    try
                    {
                        item = (await source.ReadAsync(token).ConfigureAwait(false), true);
                    }
                    catch (ChannelClosedException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        // Timeout occurred.
                        Debug.Assert(timerCts.IsCancellationRequested);
                        Debug.Assert(buffer.Count > 0);
                        item = default;
                    }

                    if (buffer.Count == 0)
                    {
                        timerCts.CancelAfter(timeout);
                    }

                    if (item is { HasValue: true })
                    {
                        buffer.Add(item.Value.Value);
                        if (buffer.Count < batchSize)
                        {
                            continue;
                        }
                    }

                    yield return buffer.ToArray();

                    buffer.Clear();
                    if (!timerCts.TryReset())
                    {
                        timerCts.Dispose();
                        timerCts = CancellationTokenSource
                            .CreateLinkedTokenSource(cancellationToken);
                    }
                }

                // Emit what's left before throwing exceptions.
                if (buffer.Count > 0)
                {
                    yield return buffer.ToArray();
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Propagate possible failure of the channel.
                if (source.Completion.IsCompleted)
                {
                    await source.Completion.ConfigureAwait(false);
                }
            }
            finally
            {
                timerCts.Dispose();
            }
        }
    }
}