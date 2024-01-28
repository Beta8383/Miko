namespace Miko.HLSLBuffers;

public class HLSLBuffer
{
    internal Guid Id;
    internal readonly BufferType Type;
    public readonly ulong Size;

    internal Guid MemoryId;
    internal ulong MemoryOffset;

    internal HLSLBuffer(ulong size, BufferType type)
    {
        Type = type;
        Size = size;
    }
}