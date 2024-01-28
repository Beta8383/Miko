namespace Miko.Attributes;

[Flags]
internal enum Usage
{
    None = 0,
    TransferSrcBit = 1,
    TransferDstBit = 2,
    UniformTexelBufferBit = 4,
    StorageTexelBufferBit = 8,
    UniformBufferBit = 16,
    StorageBufferBit = 32,
    IndexBufferBit = 64,
    VertexBufferBit = 128,
    IndirectBufferBit = 256,
    ConditionalRenderingBitExt = 512,
    ShaderBindingTableBitKhr = 1024,
    RayTracingBitNV = 1024,
    TransformFeedbackBufferBitExt = 2048,
    TransformFeedbackCounterBufferBitExt = 4096,
    VideoDecodeSrcBitKhr = 8192,
    VideoDecodeDstBitKhr = 16384,
    VideoEncodeDstBitKhr = 32768,
    VideoEncodeSrcBitKhr = 65536,
    ShaderDeviceAddressBitExt = 131072,
    ShaderDeviceAddressBitKhr = 131072,
    ShaderDeviceAddressBit = 131072,
    AccelerationStructureBuildInputReadOnlyBitKhr = 524288,
    AccelerationStructureStorageBitKhr = 1048576,
    SamplerDescriptorBufferBitExt = 2097152,
    ResourceDescriptorBufferBitExt = 4194304,
    MicromapBuildInputReadOnlyBitExt = 8388608,
    MicromapStorageBitExt = 16777216,
    ExecutionGraphScratchBitAmdx = 33554432,
    PushDescriptorsDescriptorBufferBitExt = 67108864
}

[AttributeUsage(AttributeTargets.Class)]
class BufferUsageAttribute(Usage flags) : Attribute
{
    public Usage Flags{ get; } = flags;

    public override bool Equals(object? obj)
    {
        if (obj is BufferUsageAttribute other)
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