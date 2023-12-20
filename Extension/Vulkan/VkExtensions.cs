using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Miko.Extension.Vulkan;

unsafe static class VkExtensions
{
    #region Check
    public static bool CheckExtensionSupport(this Vk vkapi, List<string> extensionsName)
    {
        //Get available extensions
        Span<uint> extensionCount = stackalloc uint[1];
        vkapi.EnumerateInstanceExtensionProperties((ReadOnlySpan<byte>)null, extensionCount, (Span<ExtensionProperties>)null);

        ExtensionProperties[] availableExtensions = new ExtensionProperties[extensionCount[0]];
        vkapi.EnumerateInstanceExtensionProperties((ReadOnlySpan<byte>)null, extensionCount, availableExtensions);

        //Check extensions are available
        var availableExtensionNames = availableExtensions.Select(x => Marshal.PtrToStringAnsi((nint)x.ExtensionName)).ToHashSet();
        Debug.WriteLine("Supported Extensions:" + string.Join(", ", extensionsName));
        return extensionsName.All(availableExtensionNames.Contains);
    }

    public unsafe static bool CheckValidationLayerSupport(this Vk vkapi, string[] layersName)
    {
        //Get available layers
        Span<uint> layerCount = stackalloc uint[1];
        vkapi.EnumerateInstanceLayerProperties(layerCount, (Span<LayerProperties>)null);
        LayerProperties[] availableLayers = new LayerProperties[layerCount[0]];
        vkapi.EnumerateInstanceLayerProperties(layerCount, availableLayers);

        var availableLayerNames = availableLayers.Select(x => Marshal.PtrToStringAnsi((nint)x.LayerName)).ToHashSet();
        Debug.WriteLine("Supported Validation Layers:" + string.Join(", ", layersName));

        return layersName.All(availableLayerNames.Contains);
    }
    #endregion

    #region Device
    public static IEnumerable<uint?> PhysicalDeviceFindQueueFamilies(this Vk vkapi, PhysicalDevice device, QueueFlags flags)
    {
        Span<uint> queueFamilyCount = stackalloc uint[1];
        vkapi.GetPhysicalDeviceQueueFamilyProperties(device, queueFamilyCount, (Span<QueueFamilyProperties>)null);

        var queueFamilies = new QueueFamilyProperties[(int)queueFamilyCount[0]];
        vkapi.GetPhysicalDeviceQueueFamilyProperties(device, queueFamilyCount, queueFamilies);

        Debug.WriteLine("Queue Families:");
        foreach (var queueFamily in queueFamilies)
            Debug.WriteLine($"Flags: {queueFamily.QueueFlags}");

        for (uint i = 0; i < queueFamilies.Length; i++)
            if (queueFamilies[i].QueueFlags.HasFlag(flags))
                yield return i;
    }

    private static string GetDeviceName(this Vk vkapi, PhysicalDevice device)
    {
        vkapi.GetPhysicalDeviceProperties(device, out PhysicalDeviceProperties deviceProperties);
        return Marshal.PtrToStringAnsi((nint)deviceProperties.DeviceName) ?? "Unknown Device";
    }

    public static IEnumerable<(PhysicalDevice, uint)> GetPhysicalDeviceWithQueueFamilies(this Vk vkapi, Instance instance, QueueFlags flags)
    {
        Span<uint> deviceCount = stackalloc uint[1];
        vkapi.EnumeratePhysicalDevices(instance, deviceCount, (Span<PhysicalDevice>)null);

        if (deviceCount[0] == 0)
            throw new Exception("Failed to find GPUs with Vulkan support!");

        var devices = new PhysicalDevice[(int)deviceCount[0]];
        vkapi.EnumeratePhysicalDevices(instance, deviceCount, devices);

        foreach (var device in devices)
        {
            Debug.WriteLine($"Device: {vkapi.GetDeviceName(device)}");
            if (vkapi.PhysicalDeviceFindQueueFamilies(device, flags).FirstOrDefault() is uint queueFamilyIndex)
                yield return (device, queueFamilyIndex);
        }
    }
    #endregion

    #region Memory
    public static uint FindMemoryType(this Vk vkapi, PhysicalDevice device, MemoryRequirements requirements, MemoryPropertyFlags flags)
    {
        PhysicalDeviceMemoryProperties memProperties;
        vkapi.GetPhysicalDeviceMemoryProperties(device, &memProperties);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
            if ((requirements.MemoryTypeBits & (1 << (int)i)) != 0 && memProperties.MemoryTypes[(int)i].PropertyFlags.HasFlag(flags))
                return i;

        throw new Exception("Failed to find suitable memory type!");
    }

    public static DeviceMemory AllocateMemory(this Vk vkapi, PhysicalDevice physicalDevice, Device device, Buffer buffer, MemoryPropertyFlags flags)
    {
        MemoryRequirements memoryRequirements = vkapi.GetBufferMemoryRequirements(device, buffer);
        Debug.WriteLine($"MemoryRequirements: Size: {memoryRequirements.Size} Alignment: {memoryRequirements.Alignment}");

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memoryRequirements.Size,
            MemoryTypeIndex = vkapi.FindMemoryType(physicalDevice, memoryRequirements, flags)
        };

        if (vkapi.AllocateMemory(device, &allocateInfo, null, out DeviceMemory memory) != Result.Success)
            throw new Exception("Failed to allocate buffer memory!");

        vkapi.BindBufferMemory(device, buffer, memory, 0);
        return memory;

    }

    public static void WriteMemory<T>(this Vk vkapi, Device device, DeviceMemory memory, Span<T> data) where T : unmanaged
    {
        void* mappedMemory = null;
        if (vkapi.MapMemory(device, memory, 0, (ulong)(sizeof(T) * data.Length), 0, ref mappedMemory) != Result.Success)
            throw new Exception("Failed to map memory!");

        Unsafe.CopyBlock(mappedMemory, Unsafe.AsPointer(ref data[0]), (uint)(sizeof(T) * data.Length));

        vkapi.UnmapMemory(device, memory);
    }

    public static void ReadMemory<T>(this Vk vkapi, Device device, DeviceMemory memory, Span<T> data) where T : unmanaged
    {
        void* mappedMemory = null;
        if (vkapi.MapMemory(device, memory, 0, (ulong)(sizeof(T) * data.Length), 0, ref mappedMemory) != Result.Success)
            throw new Exception("Failed to map memory!");

        Unsafe.CopyBlock(Unsafe.AsPointer(ref data[0]), mappedMemory, (uint)(sizeof(T) * data.Length));

        vkapi.UnmapMemory(device, memory);
    }
    #endregion
}