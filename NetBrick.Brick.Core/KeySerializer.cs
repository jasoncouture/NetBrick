using System.Text;
using FASTER.core;

namespace NetBrick.Brick.Core;

internal class KeySerializer : BinaryObjectSerializer<string>
{
    public override void Deserialize(out string obj)
    {
        var lengthToRead = reader.ReadInt32();
        if (lengthToRead > 0)
        {
            var bytes = reader.ReadBytes(lengthToRead);
            obj = Encoding.UTF8.GetString(bytes);
        }
        else if (lengthToRead == 0)
        {
            obj = string.Empty;
        }
        else
        {
            obj = null;
        }
    }

    public override void Serialize(ref string obj)
    {
        writer.Write(obj?.Length ?? -1);
        if (obj is {Length: > 0})
        {
            var bytes = Encoding.UTF8.GetBytes(obj);
            writer.Write(bytes);
        }
    }
}