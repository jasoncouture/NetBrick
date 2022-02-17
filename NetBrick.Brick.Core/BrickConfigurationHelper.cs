using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace NetBrick.Brick.Core;

public class BrickConfigurationHelper : IConfigureNamedOptions<BrickOptions>
{
    static BrickConfigurationHelper()
    {
        foreach (var name in typeof(BrickOptions).GetProperties().Select(i => i.Name))
            _ignoredSections.Add(name);
    }

    private readonly IConfiguration _configuration;

    private static HashSet<string> _ignoredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Global",
        string.Empty
    };

    public BrickConfigurationHelper(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(BrickOptions options)
    {
        Configure(string.Empty, options);
    }

    public void Configure(string name, BrickOptions options)
    {
        _configuration.GetSection("Bricks:Global").Bind(options);
        options.IsConfigured = false;
        if (string.IsNullOrEmpty(name))
        {
            foreach (var key in _configuration.GetSection("Bricks").GetChildren().Where(i => !string.IsNullOrEmpty(i.Key))
                         .Where(i => !_ignoredSections.Contains(i.Key))
                         .Select(i => i.Key))
            {
                options.ConfiguredBricks.Add(key);
            }
            return;
        }

        var namedSection = _configuration.GetSection($"Bricks:{name}");
        var defaultPath = options.Path;
        namedSection.Bind(options);
        if (options.Path == defaultPath)
        {
            options.Path = Path.Combine(defaultPath, name);
        }

        options.IsConfigured = options.Path is {Length: > 0};

        options.Name = name;
    }
}