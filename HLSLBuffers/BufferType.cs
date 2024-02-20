using Silk.NET.Vulkan;

namespace Miko.HLSLBuffers;

public enum BufferType
{
    ConstantBuffer,
    StructuredBuffer,
    RWStructuredBuffer,
}

public static class BufferTypeExtensions
{
    public static DescriptorType ToVkDescriptorType(this BufferType bufferType)
    {
        return bufferType switch
        {
            BufferType.ConstantBuffer => DescriptorType.UniformBuffer,
            BufferType.StructuredBuffer => DescriptorType.StorageBuffer,
            BufferType.RWStructuredBuffer => DescriptorType.StorageBuffer,
            _ => throw new ArgumentOutOfRangeException(nameof(bufferType), bufferType, null)
        };
    }
}