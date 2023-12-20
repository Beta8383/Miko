namespace Miko.HLSLBuffer;

public abstract class BufferBase(ulong size) : IEquatable<BufferBase>
{
    public readonly ulong Size = size;
    internal Guid guid = Guid.NewGuid();

    public bool Equals(BufferBase? other)
    {
        if (other is null)
            return false;
        return guid == other.guid;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as BufferBase);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(guid);
    }
}