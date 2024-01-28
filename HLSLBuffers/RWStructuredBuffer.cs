using System.Runtime.CompilerServices;

namespace Miko.HLSLBuffers;

public class RWStructuredBuffer<T> : HLSLBuffer
    where T : unmanaged
{
    internal RWStructuredBuffer(ulong size) : base(size * (ulong)Unsafe.SizeOf<T>(), BufferType.RWStructuredBuffer)
    {
    }
}