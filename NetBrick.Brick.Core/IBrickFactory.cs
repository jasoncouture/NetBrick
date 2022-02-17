namespace NetBrick.Brick.Core;

public interface IBrickFactory
{
    IBrick GetBrick(string name);
    ValueTask<IBrick> GetInitializedBrickAsync(string name);
}