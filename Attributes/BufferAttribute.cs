using Miko.HLSLBuffers;

namespace Miko.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class BufferAttribute(uint binding, BufferType type) : Attribute
{
    public uint Binding { get; } = binding;
    public BufferType Type { get; } = type;

    public override bool Equals(object? obj)
    {
        if (obj is BufferAttribute other)
        {
            return Binding == other.Binding;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return Binding.GetHashCode();
    }
}