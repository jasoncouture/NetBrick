using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NetBrick.Brick.Core;

namespace NetBrick.Brick.Controllers;

[ApiController]
[Route("[controller]")]
public class DebugController : ControllerBase
{
    private readonly IBrickFactory _brickFactory;

    public DebugController(IBrickFactory brickFactory)
    {
        _brickFactory = brickFactory;
    }

    [HttpGet("WriteRandom/{name}")]
    public async Task<IActionResult> WriteRandomAsync([FromRoute] string name, [FromQuery] int size,
        CancellationToken cancellationToken)
    {
        var random = new Random();
        var bytes = new byte[size];
        random.NextBytes(bytes);

        var brick = await _brickFactory.GetInitializedBrickAsync(name);
        var stopwatch = Stopwatch.StartNew();
        var offset = await brick.WriteBytesAsync(bytes, cancellationToken);
        var writeTime = stopwatch.Elapsed;
        stopwatch.Restart();
        await brick.EnsureDurableAsync(offset, cancellationToken);
        stopwatch.Stop();
        var commitTime = stopwatch.Elapsed;
        return Ok(new
        {
            EntryAddress = offset,
            Timing = new
            {
                WriteDurationMilliseconds = writeTime.TotalMilliseconds,
                CommitDurationMilliseconds = commitTime.TotalMilliseconds,
                TotalDuration = commitTime.TotalMilliseconds
            }
        });
    }
}