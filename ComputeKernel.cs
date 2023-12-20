using System.Diagnostics;
using System.Runtime.InteropServices;
using Miko.Util;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Buffer = Silk.NET.Vulkan.Buffer;
using static Miko.Global;
using System.Runtime.CompilerServices;
using Miko.Extension.Vulkan;
using Miko.HLSLBuffer;

namespace Miko;

readonly struct BufferInfo(Buffer instance, DeviceMemory memory, ulong size, bool uniform)
{
    public readonly Guid Guid = Guid.NewGuid();
    public readonly Buffer Instance = instance;
    public readonly DeviceMemory Memory = memory;
    public readonly ulong Size = size;
    public readonly bool Uniform = uniform;
}

readonly struct ShaderModuleInfo(ShaderModule instance, string functionName = "main")
{
    public readonly Guid Guid = Guid.NewGuid();
    public readonly ShaderModule Instance = instance;
    public readonly string FunctionName = functionName;
}

readonly struct PipelineInfo(PipelineLayout layout, Pipeline instance)
{
    public readonly Guid Guid = Guid.NewGuid();
    public readonly PipelineLayout Layout = layout;
    public readonly Pipeline Instance = instance;
}

unsafe class ComputeKernel
{
    const uint StorageBufferCount = 20;
    const uint UniformBufferCount = 10;
    const uint MaxDescriptorSetCount = 20;

    readonly bool _enableValidationLayers;
    readonly string[] _validationLayers = ["VK_LAYER_KHRONOS_validation"];

    readonly uint _kernalCount;

    Instance _instance;
    PhysicalDevice _physicalDevice;
    Device _device;
    uint _queueFamilyIndex;
    Queue _queue;
    CommandPool _commandPool;

    DescriptorPool _descriptorPool;

    readonly HashSet<Guid> _bufferInfos = [];
    readonly HashSet<Guid> _shaderModuleInfos = [];
    readonly HashSet<Guid> _pipelineInfos = [];

    readonly HashSet<(DeviceMemory, Buffer)> _memory = [];

    public ComputeKernel(uint kernalCount, bool enableValidationLayers = false)
    {
        _enableValidationLayers = enableValidationLayers;
        _kernalCount = kernalCount;
        Initialize();
    }

    #region Initialization
    private void Initialize()
    {
        CreateInstance();
        CreateDeviceWithQueue();
        CreateDescriptorPool();
        CreateCommandPool();
    }

    private List<string> GetRequiredExtensions()
    {
        List<string> extensions = [];

        if (OperatingSystem.IsMacOS())
            extensions.Add("VK_KHR_portability_enumeration");

        if (_enableValidationLayers)
            extensions.Add(ExtDebugUtils.ExtensionName);

        return extensions;
    }

    private void CreateInstance()
    {
        nint appName = MarshalUtil.StringToHGlobalUtf8("Miko Sample App");
        nint engineName = MarshalUtil.StringToHGlobalUtf8("Miko");
        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)appName,
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)engineName,
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version13
        };

        //Get required extensions
        List<string> extensions = GetRequiredExtensions();
        if (!VkApi.CheckExtensionSupport(extensions))
            throw new Exception("Extensions requested, but not available!");
        nint extensionNames = MarshalUtil.StringArrayToHGlobalUtf8(extensions);

        //Get required validation layers
        if (_enableValidationLayers && !VkApi.CheckValidationLayerSupport(_validationLayers))
            throw new Exception("Validation layers requested, but not available!");
        nint layerNames = MarshalUtil.StringArrayToHGlobalUtf8(_validationLayers);

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Count,
            PpEnabledExtensionNames = (byte**)extensionNames,
        };

        if (OperatingSystem.IsMacOS())
            createInfo.Flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;

        if (_enableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)_validationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)layerNames;

            var debugCreateInfo = PopulateDebugMessengerCreateInfo();
            createInfo.PNext = &debugCreateInfo;
        }

        var result = VkApi.CreateInstance(in createInfo, null, out _instance);
        if (result != Result.Success)
            throw new Exception("Failed to create instance: " + result.ToString());

        Marshal.FreeHGlobal(appName);
        Marshal.FreeHGlobal(engineName);
        MarshalUtil.FreeHGlobalArray(extensionNames, extensions.Count);
        MarshalUtil.FreeHGlobalArray(layerNames, _validationLayers.Length);
    }

    private void CreateDeviceWithQueue()
    {
        (_physicalDevice, _queueFamilyIndex) = VkApi.GetPhysicalDeviceWithQueueFamilies(_instance, QueueFlags.ComputeBit).First();

        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        var deviceFeatures = new PhysicalDeviceFeatures();

        var extensionName = Marshal.StringToHGlobalAnsi("VK_KHR_portability_subset");
        var deviceCreateInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &deviceFeatures,
#if MACOS
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = (byte**)&extensionName
#endif
        };

        if (VkApi.CreateDevice(_physicalDevice, &deviceCreateInfo, null, out _device) != Result.Success)
            throw new Exception("Failed to create logical device!");

        VkApi.GetDeviceQueue(_device, _queueFamilyIndex, 0, out _queue);

        Marshal.FreeHGlobal(extensionName);
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize storageBufferPoolSize = new()
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = StorageBufferCount
        };

        DescriptorPoolSize uniformBufferPoolSize = new()
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = UniformBufferCount
        };

        DescriptorPoolSize[] poolSizes = [storageBufferPoolSize, uniformBufferPoolSize];

        fixed (DescriptorPoolSize* poolSizesPtr = &poolSizes[0])
        {
            DescriptorPoolCreateInfo poolCreateInfo = new()
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = 2,
                PPoolSizes = poolSizesPtr,
                MaxSets = MaxDescriptorSetCount
            };

            if (VkApi.CreateDescriptorPool(_device, &poolCreateInfo, null, out _descriptorPool) != Result.Success)
                throw new Exception("Failed to create descriptor pool!");
        }
    }

    private void CreateCommandPool()
    {
        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex
        };

        if (VkApi.CreateCommandPool(_device, &createInfo, null, out _commandPool) != Result.Success)
            throw new Exception("Failed to create command pool!");
    }

    #endregion

    internal void AllocateMemory(IList<Buffer> buffers)
    {
        MemoryRequirements combinedMemoryRequirements = new(0, 0, 0);
        MemoryRequirements[] memoryRequirements = new MemoryRequirements[buffers.Count];
        for (int i = 0; i < buffers.Count; i++)
        {
            VkApi.GetBufferMemoryRequirements(_device, buffers[i], out memoryRequirements[i]);
            combinedMemoryRequirements.Size += memoryRequirements[i].Size;
            combinedMemoryRequirements.Alignment = Math.Max(combinedMemoryRequirements.Alignment, memoryRequirements[i].Alignment);
            combinedMemoryRequirements.MemoryTypeBits |= memoryRequirements[i].MemoryTypeBits;
        }
        Debug.WriteLine($"MemoryRequirements: Size: {combinedMemoryRequirements.Size} Alignment: {combinedMemoryRequirements.Alignment}");

        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = combinedMemoryRequirements.Size,
            MemoryTypeIndex = VkApi.FindMemoryType(_physicalDevice, combinedMemoryRequirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        if (VkApi.AllocateMemory(_device, &allocateInfo, null, out DeviceMemory memory) != Result.Success)
            throw new Exception("Failed to allocate buffer memory!");

        //BindBufferMemory
        ulong offset = 0;
        for (int i = 0; i < buffers.Count; i++)
        {
            VkApi.BindBufferMemory(_device, buffers[i], memory, offset);
            offset += memoryRequirements[i].Size;
        }
    }

    #region Buffer

    public BufferInfo AllocateBuffer(ulong size)
    {
        BufferCreateInfo bufferCreateInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.StorageBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        if (VkApi.CreateBuffer(_device, &bufferCreateInfo, null, out var buffer) != Result.Success)
            throw new Exception("Failed to create buffer!");

        
        DeviceMemory memory = VkApi.AllocateMemory(_physicalDevice, _device, buffer, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        var bufferInfo = new BufferInfo(buffer, memory, size, true);
        _bufferInfos.Add(bufferInfo.Guid);
        return bufferInfo;
    }

    public void WriteDataIntoBuffer<T>(BufferInfo info, Span<T> data) where T : unmanaged
    {
        if (!_bufferInfos.Contains(info.Guid))
            throw new Exception("Buffer not found!");

        if (info.Size < (ulong)(sizeof(T) * data.Length))
            throw new Exception("Data size is larger than buffer size!");

        VkApi.WriteMemory(_device, info.Memory, data);
    }

    public void ReadDataFromBuffer<T>(BufferInfo info, Span<T> data) where T : unmanaged
    {
        if (!_bufferInfos.Contains(info.Guid))
            throw new Exception("Buffer not found!");

        if (info.Size > (ulong)(sizeof(T) * data.Length))
            throw new Exception("Buffer size is larger than data size!");

        VkApi.ReadMemory(_device, info.Memory, data);
    }

    public void FreeBuffer(BufferInfo info)
    {
        if (!_bufferInfos.Contains(info.Guid))
            throw new Exception("Buffer not found!");

        VkApi.DestroyBuffer(_device, info.Instance, null);
        VkApi.FreeMemory(_device, info.Memory, null);
        _bufferInfos.Remove(info.Guid);
    }

    #endregion

    #region Descriptor Set
    public DescriptorSetLayout CreateDescriptorSetLayout(int uniformBufferCount, int storageBufferCount)
    {
        var layoutBindings = new DescriptorSetLayoutBinding[uniformBufferCount + storageBufferCount];
        for (int i = 0; i < uniformBufferCount + storageBufferCount; i++)
        {
            layoutBindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)i,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
                DescriptorType = i < uniformBufferCount ? DescriptorType.UniformBuffer : DescriptorType.StorageBuffer
            };
        }

        DescriptorSetLayoutCreateInfo layoutCreateInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)layoutBindings.Length,
            PBindings = (DescriptorSetLayoutBinding*)Unsafe.AsPointer(ref layoutBindings[0])
        };

        if (VkApi.CreateDescriptorSetLayout(_device, in layoutCreateInfo, null, out var descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create descriptor set layout!");
        return descriptorSetLayout;
    }

    public DescriptorSet AllocateDescriptorSet(DescriptorSetLayout descriptorSetLayout)
    {
        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref descriptorSetLayout)
        };

        if (VkApi.AllocateDescriptorSets(_device, in allocateInfo, out var descriptorSet) != Result.Success)
            throw new Exception("Failed to allocate descriptor set!");

        return descriptorSet;
    }

    public void UpdateDescriptorSet(DescriptorSet descriptorSet, BufferInfo[] srcBufferInfos)
    {
        var writeDescriptorSets = new WriteDescriptorSet[srcBufferInfos.Length];
        var descriptorBufferInfo = new DescriptorBufferInfo[srcBufferInfos.Length];
        uint index = 0;
        for (int i = 0; i < srcBufferInfos.Length; i++)
        {
            if (!_bufferInfos.Contains(srcBufferInfos[i].Guid))
                throw new Exception("Buffer not found!");

            descriptorBufferInfo[i] = new()
            {
                Buffer = srcBufferInfos[i].Instance,
                Offset = 0,
                Range = srcBufferInfos[i].Size
            };

            writeDescriptorSets[i] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = index++,
                DstArrayElement = 0,
                DescriptorCount = 1,
                DescriptorType = srcBufferInfos[i].Uniform ? DescriptorType.UniformBuffer : DescriptorType.StorageBuffer,
                PBufferInfo = (DescriptorBufferInfo*)Unsafe.AsPointer(ref descriptorBufferInfo[i]),
            };
        }

        VkApi.UpdateDescriptorSets(_device, writeDescriptorSets, null);
    }

    public void FreeDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout)
    {
        VkApi.DestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
    }

    #endregion

    public ShaderModuleInfo CreateShaderModule(Span<byte> shaderCode, string functionName = "main")
    {
        var shaderModuleCreateInfo = new ShaderModuleCreateInfo
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)shaderCode.Length,
            PCode = (uint*)Unsafe.AsPointer(ref shaderCode[0])
        };

        if (VkApi.CreateShaderModule(_device, in shaderModuleCreateInfo, null, out var shaderModule) != Result.Success)
            throw new Exception("Failed to create compute shader module!");

        var shaderModuleInfo = new ShaderModuleInfo(shaderModule, functionName);
        _shaderModuleInfos.Add(shaderModuleInfo.Guid);
        return shaderModuleInfo;
    }

    public void FreeShaderModule(ShaderModuleInfo shaderModuleInfo)
    {
        if (!_shaderModuleInfos.Contains(shaderModuleInfo.Guid))
            throw new Exception("Shader module not found!");

        VkApi.DestroyShaderModule(_device, shaderModuleInfo.Instance, null);
        _shaderModuleInfos.Remove(shaderModuleInfo.Guid);
    }

    #region Pipeline

    public PipelineLayout CreateComputePineLineLayout(DescriptorSetLayout descriptorSetLayout)
    {
        var pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorSetLayout
        };

        if (VkApi.CreatePipelineLayout(_device, &pipelineLayoutCreateInfo, null, out var pipelineLayout) != Result.Success)
            throw new Exception("Failed to create pipeline layout!");

        return pipelineLayout;
    }

    public PipelineInfo CreateComputePipeLine(ShaderModuleInfo shaderInfo, PipelineLayout pipelineLayout)
    {
        var functionName = MarshalUtil.StringToHGlobalUtf8(shaderInfo.FunctionName);
        var pipelineShaderStageCreateInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = shaderInfo.Instance,
            PName = (byte*)functionName
        };

        var computePipelineCreateInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = pipelineShaderStageCreateInfo,
            Layout = pipelineLayout,
        };

        if (VkApi.CreateComputePipelines(_device, new PipelineCache(), 1, in computePipelineCreateInfo, null, out var pipeline) != Result.Success)
            throw new Exception("Failed to create compute pipeline!");

        Marshal.FreeHGlobal(functionName);
        var pipelineInfo = new PipelineInfo(pipelineLayout, pipeline);
        _pipelineInfos.Add(pipelineInfo.Guid);

        return pipelineInfo;
    }

    public void FreeComputePipelineLayout(PipelineLayout pipelineLayout)
    {
        VkApi.DestroyPipelineLayout(_device, pipelineLayout, null);
    }

    public void FreeComputePipeline(PipelineInfo pipelineInfo)
    {
        if (!_pipelineInfos.Contains(pipelineInfo.Guid))
            throw new Exception("Pipeline not found!");

        VkApi.DestroyPipeline(_device, pipelineInfo.Instance, null);
        _pipelineInfos.Remove(pipelineInfo.Guid);
    }

    #endregion

    public void Execute(PipelineInfo pipelineInfo, DescriptorSet descriptorSet)
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (VkApi.AllocateCommandBuffers(_device, &allocateInfo, out CommandBuffer commandBuffer) != Result.Success)
            throw new Exception("Failed to allocate command buffer!");

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (VkApi.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
            throw new Exception("Failed to begin command buffer!");
        VkApi.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipelineInfo.Instance);
        VkApi.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Compute, pipelineInfo.Layout, 0, 1, in descriptorSet, 0, null);
        VkApi.CmdDispatch(commandBuffer, 1, 1, 1);
        VkApi.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        if (VkApi.QueueSubmit(_queue, 1, in submitInfo, default) != Result.Success)
            throw new Exception("Failed to submit queue!");

        if (VkApi.QueueWaitIdle(_queue) != Result.Success)
            throw new Exception("Failed to wait idle!");
    }

    public void Dispose()
    {
        VkApi.DestroyCommandPool(_device, _commandPool, null);
        VkApi.DestroyDescriptorPool(_device, _descriptorPool, null);
        VkApi.DestroyDevice(_device, null);
        VkApi.DestroyInstance(_instance, null);
    }

    #region Validation Layers
    private DebugUtilsMessengerCreateInfoEXT PopulateDebugMessengerCreateInfo() => new()
    {
        SType = StructureType.DebugUtilsMessengerCreateInfoExt,
        MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                                  DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                                  DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
        MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                              DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                              DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
        PfnUserCallback = new DebugUtilsMessengerCallbackFunctionEXT(DebugCallback)
    };

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageType,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        Debug.WriteLine($"[{messageSeverity}]{messageType} -> {Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage)}");
        return Vk.False;
    }
    #endregion
}