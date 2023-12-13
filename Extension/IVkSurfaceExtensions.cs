using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Miko.Vulkan.Extensions;

unsafe static class IVkSurfaceExtensions
{
    public static string[] GetRequiredExtensions(this IVkSurface surface, bool enableValidationLayers)
    {
        var extensions = surface.GetRequiredExtensions(out var extensionsCount);
        List<string> extensionsName = new(SilkMarshal.PtrToStringArray((nint)extensions, (int)extensionsCount));
        SilkMarshal.Free((nint)extensions);

        if (OperatingSystem.IsMacOS())
            extensionsName.Add("VK_KHR_portability_enumeration");

        if (enableValidationLayers)
            extensionsName.Add(ExtDebugUtils.ExtensionName);

        return [.. extensionsName];
    }
}