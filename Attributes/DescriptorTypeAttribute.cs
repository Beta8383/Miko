namespace Miko.Attributes;

public enum DescriptorType
{
    Sampler = 0,
    CombinedImageSampler = 1,
    SampledImage = 2,
    StorageImage = 3,
    UniformTexelBuffer = 4,
    StorageTexelBuffer = 5,
    UniformBuffer = 6,
    StorageBuffer = 7,
    UniformBufferDynamic = 8,
    StorageBufferDynamic = 9,
    InputAttachment = 10,
    InlineUniformBlockExt = 1000138000,
    InlineUniformBlock = 1000138000,
    AccelerationStructureKhr = 1000150000,
    AccelerationStructureNV = 1000165000,
    MutableValve = 1000351000,
    MutableExt = 1000351000,
    SampleWeightImageQCom = 1000440000,
    BlockMatchImageQCom = 1000440001
}

[AttributeUsage(AttributeTargets.Class)]
class DescriptorTypeAttribute(DescriptorType flags) : Attribute
{
    public DescriptorType Flags { get; } = flags;

    public override bool Equals(object? obj)
    {
        if (obj is DescriptorTypeAttribute other)
        {
            return Flags == other.Flags;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return Flags.GetHashCode();
    }
}