using System.Runtime.CompilerServices;

namespace Miko.HLSLBuffer;

public class StructuredBuffer<T>(ulong length) : BufferBase((ulong)Unsafe.SizeOf<T>() * length)
    where T : unmanaged
{
}

public class RWStructuredBuffer<T>(ulong length) : StructuredBuffer<T>(length)
where T : unmanaged
{
}