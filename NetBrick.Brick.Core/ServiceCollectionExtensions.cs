using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NetBrick.Brick.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBricks(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddSingleton<IConfigureOptions<BrickOptions>, BrickConfigurationHelper>();
        services.AddSingleton<IConfigureNamedOptions<BrickOptions>, BrickConfigurationHelper>();
        services.AddSingleton<IBrickFactory, BrickFactory>();
        services.AddHostedService<BrickInitializationJob>();
        return services;
    }
}