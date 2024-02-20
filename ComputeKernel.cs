using System.Diagnostics;
using System.Runtime.InteropServices;
using Miko.Util;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Runtime.CompilerServices;
using Miko.Extension.Vulkan;
using static Miko.Global;

namespace Miko;

unsafe class ComputeKernel : IDisposable
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
        (_physicalDevice, _queueFamilyIndex) = VkApi.GetPhysicalDeviceWithQueueFamilies(_instance, QueueFlags.ComputeBit).FirstOrDefault()
                                               ?? throw new Exception("Failed to find suitable device!");

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
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = (byte**)&extensionName
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

    internal Buffer CreateBuffer(BufferUsageFlags bufferUsage, ulong size, SharingMode sharingMode = SharingMode.Exclusive)
    {
        BufferCreateInfo bufferCreateInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = bufferUsage,
            SharingMode = sharingMode
        };

        if (VkApi.CreateBuffer(_device, in bufferCreateInfo, null, out var buffer) != Result.Success)
            throw new Exception("Failed to create buffer!");

        return buffer;
    }

    internal void FreeBuffer(Buffer buffer)
    {
        VkApi.DestroyBuffer(_device, buffer, null);
    }

    internal void WriteBuffer(DeviceMemory memory, ulong size, ulong offset, void* source)
    {
        void* mappedMemory = MapMemory(memory, offset, size);
        Unsafe.CopyBlock(mappedMemory, source, (uint)size);
        UnmapMemory(memory);
    }

    internal void ReadBuffer(DeviceMemory memory, ulong size, ulong offset, void* destination)
    {
        void* mappedMemory = MapMemory(memory, offset, size);
        Unsafe.CopyBlock(destination, mappedMemory, (uint)size);
        UnmapMemory(memory);
    }

    #region Memory
    internal DeviceMemory AllocateMemoryForBuffer(Buffer buffer, MemoryPropertyFlags flags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
    {
        var memoryRequirements = VkApi.GetBufferMemoryRequirements(_device, buffer);
        var memoryTypeIndex = FindMemoryType(memoryRequirements.MemoryTypeBits, flags);
        DeviceMemory memory = AllocateMemory(memoryTypeIndex, memoryRequirements.Size);
        VkApi.BindBufferMemory(_device, buffer, memory, 0);
        return memory;
    }

    internal uint FindMemoryType(uint memoryTypeBits, MemoryPropertyFlags flags)
    {
        var memProperties = VkApi.GetPhysicalDeviceMemoryProperties(_physicalDevice);

        for (uint i = 0; i < memProperties.MemoryTypeCount; i++)
            if ((memoryTypeBits & (1 << (int)i)) != 0 && memProperties.MemoryTypes[(int)i].PropertyFlags.HasFlag(flags))
                return i;

        throw new Exception("Failed to find suitable memory type!");
    }

    /*
    public MemoryInfo[] GetMemoryRequirements(Buffer[] buffers)
    {
        // Get memory requirements for each buffer
        MemoryRequirements[] bufferMemoryRequirements = new MemoryRequirements[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
            bufferMemoryRequirements[i] = VkApi.GetBufferMemoryRequirements(_device, buffers[i]);

        // Group by alignment
        // IGrouping<ulong alignment, (HLSLBuffer, MemoryRequirements)>[]
        var groupByAlignment = buffers.Zip(bufferMemoryRequirements, (first, second) => new { Buffer = first, MemoryRequirement = second })
                                      .GroupBy(x => x.MemoryRequirement.Alignment)
                                      .ToArray();

        // Combine buffer with same alignment into one DeviceMemory
        var combinedBufferMemoryRequirements = groupByAlignment.Select(group => new MemoryRequirements()
        {
            Alignment = group.Key,
            Size = group.Aggregate(0ul, (acc, y) => acc + y.MemoryRequirement.Size),
            MemoryTypeBits = group.Aggregate(0u, (acc, y) => acc | y.MemoryRequirement.MemoryTypeBits)
        }).ToArray();

        var memoryInfos = AllocateMemory(combinedBufferMemoryRequirements);

        // Assign DeviceMemoryId and Offset to HLSLBuffer
        for (int memoryIndex = 0; memoryIndex < groupByAlignment.Length; memoryIndex++)
        {
            ulong offset = 0;
            foreach (var group in groupByAlignment[memoryIndex])
            {
                var buffer = group.Buffer;
                var bufferMemoryRequirement = group.MemoryRequirement;

                buffer.DeviceMemoryId = memoryInfos[memoryIndex].Id;
                buffer.Offset = offset;

                memoryInfos[memoryIndex].BuffersId.Add(buffer.Id);
                offset += bufferMemoryRequirement.Size;     //use the size after alignment rather than the original size
            }
        }
        return memoryInfos;
    }
    */

    internal DeviceMemory AllocateMemory(uint memoryTypeIndex, ulong size)
    {
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex
        };

        if (VkApi.AllocateMemory(_device, in allocateInfo, null, out var memory) != Result.Success)
            throw new Exception("Failed to allocate buffer memory!");

        return memory;
    }

    internal void FreeMemory(DeviceMemory memory)
    {
        VkApi.FreeMemory(_device, memory, null);
    }

    internal void* MapMemory(DeviceMemory memory, ulong offset, ulong size)
    {
        void* mappedMemory = null;
        if (VkApi.MapMemory(_device, memory, offset, size, 0, ref mappedMemory) != Result.Success)
            throw new Exception("Failed to map memory!");

        return mappedMemory;
    }

    internal void UnmapMemory(DeviceMemory memory)
    {
        VkApi.UnmapMemory(_device, memory);
    }
    #endregion

    #region Descriptor Set
    internal DescriptorSetLayout CreateDescriptorSetLayout(DescriptorSetLayoutBinding[] layoutBindings)
    {
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

    internal void FreeDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout)
    {
        VkApi.DestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
    }

    internal DescriptorSet AllocateDescriptorSet(DescriptorSetLayout descriptorSetLayout)
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

    internal void FreeDescriptorSet(DescriptorSet descriptorSet)
    {
        VkApi.FreeDescriptorSets(_device, _descriptorPool, 1, in descriptorSet);
    }

    internal void UpdateDescriptorSet(DescriptorSet descriptorSet, (uint binding, DescriptorType type, DescriptorBufferInfo descriptorBufferInfo)[] buffers)
    {
        var writeDescriptorSets = new WriteDescriptorSet[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            writeDescriptorSets[i] = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = descriptorSet,
                DstBinding = buffers[i].binding,
                DstArrayElement = 0,
                DescriptorCount = 1,
                DescriptorType = buffers[i].type,
                PBufferInfo = (DescriptorBufferInfo*)Unsafe.AsPointer(ref buffers[i].descriptorBufferInfo),
            };
        }

        VkApi.UpdateDescriptorSets(_device, writeDescriptorSets, null);
    }

    /*
        internal void UpdateDescriptorSet<T>(DescriptorSet descriptorSet, T shaderBuffer)
        {
            var bindings = ShaderBufferMapper<T>.Bindings;
            var descriptorBufferInfo = new DescriptorBufferInfo[bindings.Count];
            var writeDescriptorSets = new WriteDescriptorSet[bindings.Count];

            for (int i = 0; i < bindings.Count; i++)
            {
                HLSLBuffer HLSLbuffer = ShaderBufferMapper<T>.GetBuffer(shaderBuffer, bindings[i]);
                if (!_buffers.TryGetValue(HLSLbuffer.Id, out var buffer))
                    throw new Exception("Buffer not found!");

                descriptorBufferInfo[i] = new()
                {
                    Buffer = buffer,
                    Offset = 0,
                    Range = HLSLbuffer.Size
                };

                writeDescriptorSets[i] = new()
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = descriptorSet,
                    DstBinding = bindings[i],
                    DstArrayElement = 0,
                    DescriptorCount = 1,
                    DescriptorType = HLSLbuffer.GetDescriptorType(),
                    PBufferInfo = (DescriptorBufferInfo*)Unsafe.AsPointer(ref descriptorBufferInfo[i]),
                };
            }

            VkApi.UpdateDescriptorSets(_device, writeDescriptorSets, null);
        }
    */
    #endregion

    #region Shader Module
    internal ShaderModule CreateShaderModule(Span<byte> shaderCode)
    {
        var shaderModuleCreateInfo = new ShaderModuleCreateInfo
        {
            SType = StructureType.ShaderModuleCreateInfo,
            CodeSize = (nuint)shaderCode.Length,
            PCode = (uint*)Unsafe.AsPointer(ref shaderCode[0])
        };

        if (VkApi.CreateShaderModule(_device, in shaderModuleCreateInfo, null, out var shaderModule) != Result.Success)
            throw new Exception("Failed to create compute shader module!");

        return shaderModule;
    }

    public void FreeShaderModule(ShaderModule shaderModule)
    {
        VkApi.DestroyShaderModule(_device, shaderModule, null);
    }
    #endregion

    #region Pipeline
    internal PipelineLayout CreatePipelineLayout(DescriptorSetLayout descriptorSetLayout)
    {
        var pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref descriptorSetLayout)
        };

        if (VkApi.CreatePipelineLayout(_device, in pipelineLayoutCreateInfo, null, out var pipelineLayout) != Result.Success)
            throw new Exception("Failed to create pipeline layout!");

        return pipelineLayout;
    }

    internal Pipeline CreatePipeline(ShaderModule shaderModule, string functionName, PipelineLayout pipelineLayout)
    {
        var functionNameUtf8 = MarshalUtil.StringToHGlobalUtf8(functionName);
        var pipelineShaderStageCreateInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = shaderModule,
            PName = (byte*)functionNameUtf8
        };

        var computePipelineCreateInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = pipelineShaderStageCreateInfo,
            Layout = pipelineLayout,
        };

        if (VkApi.CreateComputePipelines(_device, new PipelineCache(), 1, in computePipelineCreateInfo, null, out var pipeline) != Result.Success)
            throw new Exception("Failed to create compute pipeline!");

        Marshal.FreeHGlobal(functionNameUtf8);
        return pipeline;
    }

    internal void FreePipelineLayout(PipelineLayout pipelineLayout)
    {
        VkApi.DestroyPipelineLayout(_device, pipelineLayout, null);
    }

    internal void FreePipeline(Pipeline pipeline)
    {
        VkApi.DestroyPipeline(_device, pipeline, null);
    }
    #endregion

    internal void Execute(Pipeline pipeline, PipelineLayout pipelineLayout, DescriptorSet descriptorSet)
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

        VkApi.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
        VkApi.CmdBindDescriptorSets(commandBuffer, PipelineBindPoint.Compute, pipelineLayout, 0, 1, in descriptorSet, 0, null);
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

    #region Dispose

    public void Dispose()
    {
        VkApi.DestroyCommandPool(_device, _commandPool, null);
        VkApi.DestroyDescriptorPool(_device, _descriptorPool, null);
        VkApi.DestroyDevice(_device, null);
        VkApi.DestroyInstance(_instance, null);
    }
    #endregion

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