namespace Miko.HLSLBuffer;

[AttributeUsage(AttributeTargets.Field)]
public class BindingAttribute(int binding) : Attribute
{
    public int Binding { get; } = binding;
}