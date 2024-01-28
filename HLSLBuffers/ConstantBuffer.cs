using System.Runtime.CompilerServices;

namespace Miko.HLSLBuffers;

public class ConstantBuffer<T> : HLSLBuffer
    where T : unmanaged
{
    internal ConstantBuffer() : base((ulong)Unsafe.SizeOf<T>(), BufferType.ConstantBuffer)
    {
    }
}