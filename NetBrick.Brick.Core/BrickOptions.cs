using System.Text.Json.Nodes;

namespace NetBrick.Brick.Core;

// Terms:
// Brick - A logical object representing low level storage
// Journal - An object that has one or more bricks, and uses a 2nd journal for things like compaction.
// Partition - A named journal that is assigned a number.

public class BrickOptions
{
    public bool IsConfigured { get; set; }
    public string Name { get; set; }
    public long PageSize { get; set; } = 0;
    public string Path { get; set; } = "Bricks";
    public HashSet<string> ConfiguredBricks { get; } = new(StringComparer.OrdinalIgnoreCase);
}