using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FASTER.core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NetBrick.Brick.Core;

public class BrickInitializationJob : BackgroundService
{
    private readonly IBrickFactory _brickFactory;
    private readonly IOptions<BrickOptions> _options;

    public BrickInitializationJob(IBrickFactory brickFactory, IOptions<BrickOptions> options)
    {
        _brickFactory = brickFactory;
        _options = options;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var name in _options.Value.ConfiguredBricks)
        {
            await _brickFactory.GetInitializedBrickAsync(name);
        }
    }
}
public class Brick : IBrick, IAsyncDisposable
{
    private readonly BrickOptions _options;
    private readonly IDevice _device;
    private FasterLog _log;
    private Task _initializationTask;
    private IFasterKV<string, long> _consumerOffsets;
    private string _internalKeyValueStorePath;
    private string _brickPath;
    private readonly CancellationTokenSource _periodicTaskShutdownSignal = new();
    private readonly Task _periodicTask;
    private int _offsetsChanged = 0;

    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    private async Task PeriodicTasks(CancellationToken cancellationToken)
    {
        await _initializationTask;
        int counter = 0;
        const int maxInterval = 10000;
        var periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await periodicTimer.WaitForNextTickAsync(cancellationToken);
                counter++;
                counter %= maxInterval;
                // Commit the data log every 5ms
                var commitTask = _log.CommitAsync(cancellationToken);
                if (counter % 100 == 0) // Every 25ms, take an incremental checkpoint
                {
                    using var session = CreateSession();
                    if (counter == 0 && _offsetsChanged != 0)
                    {
                        var target = _consumerOffsets.Log.HeadAddress;
                        session.Compact(target, false);

                        await _consumerOffsets
                            .TakeFullCheckpointAsync(CheckpointType.Snapshot, cancellationToken)
                            .ConfigureAwait(false);

                        _consumerOffsets.Log
                            .ShiftBeginAddress(target, true);
                    }
                    else if(Interlocked.CompareExchange(ref _offsetsChanged, 0, 1) == 1)
                    {
                        await session.CompletePendingAsync(false);
                        await _consumerOffsets
                            .TakeFullCheckpointAsync(CheckpointType.FoldOver)
                            .ConfigureAwait(false);
                    }
                }

                await commitTask.ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignored.
        }
    }

    public async ValueTask SetConsumerOffsetAsync(string consumerName, long address,
        CancellationToken cancellationToken)
    {
        using var session = CreateSession();
        
        if (address <= _log.BeginAddress)
        {
            await session.DeleteAsync(ref consumerName, token: cancellationToken);
        }
        else
        {
            await session.UpsertAsync(consumerName, address, token: cancellationToken);
        }

        Interlocked.Exchange(ref _offsetsChanged, 1);
        await session.WaitForCommitAsync(cancellationToken);
    }

    ClientSession<string, long, long, long, Empty, IFunctions<string, long, long, long, Empty>> CreateSession() =>
        _consumerOffsets.NewSession(new SimpleFunctions<string, long>(Math.Max));

    private async ValueTask<long> ReadConsumerOffsetAsync(string consumerName, CancellationToken cancellationToken)
    {
        using var session = CreateSession();
        var result = await session.ReadAsync(ref consumerName, token: cancellationToken);
        Status status;
        long output;
        while (true)
        {
            (status, output) = result.Complete();
            if (status == Status.NOTFOUND)
            {
                return _log.BeginAddress;
            }

            if (status == Status.OK)
            {
                return Math.Max(_log.BeginAddress, output);
            }

            if (status == Status.ERROR)
            {
                return _log.BeginAddress;
            }

            if (status != Status.PENDING) throw new InvalidAsynchronousStateException("State is not valid");
        }
    }

    public Brick(string name, IOptionsMonitor<BrickOptions> options)
    {
        _options = options.Get(name);
        _brickPath = Directory.CreateDirectory(_options.Path).FullName;
        _internalKeyValueStorePath = Directory.CreateDirectory(_brickPath).FullName;
        var logDevice = Devices.CreateLogDevice(Path.Combine(_brickPath, $"{name}.segment"), false, false, -1L, true);
        var offsetLog = Devices.CreateLogDevice(Path.Combine(_internalKeyValueStorePath, $"{name}.kvp.log"));
        var objectLog = Devices.CreateLogDevice(Path.Combine(_internalKeyValueStorePath, $"{name}.obj.log"));
        _consumerOffsets = new FasterKV<string, long>(
            1L << 20,
            new LogSettings()
            {
                ObjectLogDevice = objectLog,
                LogDevice = offsetLog,
                MutableFraction = 0.8
            },
            new CheckpointSettings()
            {
                CheckpointDir = Path.Combine(_internalKeyValueStorePath, $"{name}-checkpoints"),
                RemoveOutdated = true
            },
            serializerSettings: new SerializerSettings<string, long>()
            {
                keySerializer = () => new KeySerializer()
            });
        // The task is kicked off here so that initialization is thread safe. The factory will wait for initialization before returning this instance.
        _initializationTask = FasterLog.CreateAsync(new FasterLogSettings() {LogDevice = logDevice, LogCommitDir = Path.Combine(_brickPath, $"{name}-commits")}).AsTask()
            .ContinueWith(async i =>
            {
                var log = await i;
                // This is an attempt to make sure that all threads can see the log immediately.
                Interlocked.Exchange(ref _log, log);
            });
        _periodicTask = PeriodicTasks(_periodicTaskShutdownSignal.Token);
    }


    // Value task is used in the case the initialization is complete.
    public async ValueTask EnsureInitialized()
    {
        if (_log != null) return;
        await _initializationTask;
    }

    public async ValueTask MakeDurableAsync(CancellationToken cancellationToken)
    {
        await _log.CommitAsync(cancellationToken);
    }

    public async ValueTask EnsureDurableAsync(long address, CancellationToken cancellationToken)
    {
        await _log.WaitForCommitAsync(address, cancellationToken);
    }

    public async ValueTask<long> WriteBytesAsync(byte[] data, CancellationToken cancellationToken)
    {
        return await _log.EnqueueAsync(data, cancellationToken);
    }

    public async IAsyncEnumerable<(byte[], long)> EnumerateBytesAsync(
        string name,
        long from = long.MinValue,
        long to = long.MaxValue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            from = await ReadConsumerOffsetAsync(name, cancellationToken);
        }

        if (from < _log.BeginAddress)
        {
            from = _log.BeginAddress;
        }

        if (from > to)
        {
            yield break;
        }

        using var iterator = _log.Scan(from, to, recover: false);
        await foreach (var (result, length, currentAddress, nextAddress) in iterator.GetAsyncEnumerable(
                           cancellationToken))
        {
            yield return (result[..length], currentAddress);
            iterator.CompleteUntil(nextAddress); // This is needed so the iterator can continue at the tail.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _periodicTaskShutdownSignal.Cancel();

        await Task.WhenAll(_initializationTask, _periodicTask).ConfigureAwait(false);

        _log.Dispose(); // Stop the log
        _initializationTask.Dispose();
        _consumerOffsets.Dispose(); // Shutdown the offset storage
    }
}