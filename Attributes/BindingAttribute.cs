namespace Miko.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class BindingAttribute(uint binding) : Attribute
{
    public uint Binding { get; } = binding;

    public override bool Equals(object? obj)
    {
        if (obj is BindingAttribute other)
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