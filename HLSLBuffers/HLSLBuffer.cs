using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Miko.HLSLBuffers;

public class HLSLBuffer
{
    public readonly BufferType Type;
    public readonly ulong Size;

    internal DeviceMemory? Memory;
    internal Buffer? Buffer;
    internal ulong Offset;

    internal HLSLBuffer(ulong size, BufferType type)
    {
        Type = type;
        Size = size;
    }
}