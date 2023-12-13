using System.Runtime.InteropServices;
using System.Text;

namespace Miko.Util;

unsafe static class MarshalUtil
{
    public static nint StringArrayToHGlobalAnsi(string[] array)
    {
        int[] lengths = array.Select(s => Encoding.UTF8.GetMaxByteCount(s.Length) + 1).ToArray();
        int totalLength = lengths.Sum();

        nint bytesPtr = Marshal.AllocHGlobal(totalLength);
        var bytes = new Span<byte>((void*)bytesPtr, totalLength);

        int offset = 0;
        for (int i = 0; i < array.Length; i++)
        {
            StringIntoSpan(array[i], bytes.Slice(offset, lengths[i]));
            offset += lengths[i];
        }

        return bytesPtr;
    }

    public static unsafe void StringIntoSpan(string? input, Span<byte> span)
    {
        int convertedBytes = 0;
        fixed (char* firstChar = input)
        {
            fixed (byte* bytes = span)
            {
                convertedBytes = Encoding.UTF8.GetBytes(firstChar, input.Length, bytes, span.Length - 1);
            }
        }

        span[convertedBytes] = 0;
    }
}

unsafe class AutoReleasePointer(nint pointer) : IDisposable
{
    public nint Pointer { get; private set; } = pointer;

    public void Dispose()
    {
        Marshal.FreeHGlobal(Pointer);
        Pointer = nint.Zero;
    }

    public static implicit operator nint(AutoReleasePointer ptr) => ptr.Pointer;
    public static implicit operator void*(AutoReleasePointer ptr) => (void*)ptr.Pointer;
}