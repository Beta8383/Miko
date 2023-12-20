using System.Runtime.InteropServices;
using System.Text;

namespace Miko.Util;

unsafe static class MarshalUtil
{
    public static nint StringToHGlobalUtf8(string str)
    {
        int length = Encoding.UTF8.GetByteCount(str) + 1;
        nint pointer = Marshal.AllocHGlobal(length);

        Span<byte> utf8Bytes = new((void*)pointer, length);
        Encoding.UTF8.GetBytes(str, utf8Bytes);
        utf8Bytes[length - 1] = 0;  // null terminator
        
        return pointer;
    }

    public static nint StringArrayToHGlobalUtf8(IList<string> array)
    {
        nint pointerArrayPtr = Marshal.AllocHGlobal(sizeof(nint) * array.Count);
        Span<nint> pointerArray = new((void*)pointerArrayPtr, array.Count);
    
        for (int i = 0; i < array.Count; i++)
            pointerArray[i] = StringToHGlobalUtf8(array[i]);
        return pointerArrayPtr;
    }

    public static void FreeHGlobalArray(nint pointer, int length)
    {
        Span<nint> pointers = new((void*)pointer, length);
        foreach (nint ptr in pointers)
            Marshal.FreeHGlobal(ptr);
        Marshal.FreeHGlobal(pointer);
    }
}