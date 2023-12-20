using System.Runtime.CompilerServices;

namespace Miko.HLSLBuffer;

public class ConstantBuffer<T>() : BufferBase((ulong)Unsafe.SizeOf<T>()) 
{ }