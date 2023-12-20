using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;

namespace Miko.Extension.Vulkan;

unsafe static class VkSurfaceExtensions
{
    public static List<string> GetExtensions(this IVkSurface surface)
    {
        var extensions = surface.GetRequiredExtensions(out var extensionsCount);
        List<string> extensionsName = new(SilkMarshal.PtrToStringArray((nint)extensions, (int)extensionsCount));
        SilkMarshal.Free((nint)extensions);
        return extensionsName;
    }
}