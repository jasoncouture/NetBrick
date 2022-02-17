using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace NetBrick.Brick.Core;

public class BrickFactory : IBrickFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IBrick> _bricks = new(StringComparer.OrdinalIgnoreCase);

    public BrickFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IBrick GetBrick(string name) =>
        _bricks.GetOrAdd(name, s => ActivatorUtilities.CreateInstance<Brick>(_serviceProvider, s));

    public async ValueTask<IBrick> GetInitializedBrickAsync(string name)
    {
        var brick = GetBrick(name);
        await brick.EnsureInitialized();
        return brick;
    }
}