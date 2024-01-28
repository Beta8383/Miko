using System.Diagnostics;
using System.Runtime.InteropServices;
using Miko.Util;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Runtime.CompilerServices;
using Miko.Extension.Vulkan;
using Miko.HLSLBuffers;
using Miko.VkInstanceInfo;
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

    readonly Dictionary<Guid, DeviceMemory> _memory = [];
    readonly Dictionary<Guid, Buffer> _buffers = [];

    readonly Dictionary<Guid, DescriptorSetLayout> _descriptorSetLayouts = [];
    readonly Dictionary<Guid, DescriptorSet> _descriptorSets = [];

    readonly Dictionary<Guid, ShaderModule> _shaderModules = [];

    readonly Dictionary<Guid, PipelineLayout> _pipelineLayouts = [];
    readonly Dictionary<Guid, Pipeline> _pipelines = [];

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

    internal Guid CreateBuffer(ulong size, BufferUsageFlags bufferUsage)
    {
        BufferCreateInfo bufferCreateInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = bufferUsage,
            SharingMode = SharingMode.Exclusive
        };

        if (VkApi.CreateBuffer(_device, in bufferCreateInfo, null, out var vkbuffer) != Result.Success)
            throw new Exception("Failed to create buffer!");

        Guid guid = Guid.NewGuid();
        _buffers.Add(guid, vkbuffer);
        return guid;
    }

    /*
    internal void WriteBuffer(Guid bufferId, uint count)
    {
        void* source = Unsafe.AsPointer(ref data[0]);
        TemporaryMapMemory(buffer, mappedMemory => Unsafe.CopyBlock(mappedMemory, source, count));
    }

    internal void WriteBuffer<T>(HLSLBuffer buffer, Span<T> data) where T : unmanaged
    {
        void* source = Unsafe.AsPointer(ref data[0]);
        TemporaryMapMemory(buffer, mappedMemory => Unsafe.CopyBlock(mappedMemory, source, (uint)buffer.Size));
    }

    internal void WriteBuffer<T>(HLSLBuffer buffer, T data) where T : unmanaged
    {
        void* source = Unsafe.AsPointer(ref data);
        TemporaryMapMemory(buffer, mappedMemory => Unsafe.CopyBlock(mappedMemory, source, (uint)buffer.Size));
    }

    internal void ReadBuffer<T>(HLSLBuffer buffer, out T[] data) where T : unmanaged
    {
        data = new T[(int)buffer.Size / sizeof(T)];
        void* destination = Unsafe.AsPointer(ref data[0]);
        TemporaryMapMemory(buffer, mappedMemory => Unsafe.CopyBlock(destination, mappedMemory, (uint)buffer.Size));
    }

    internal void ReadBuffer<T>(HLSLBuffer buffer, out T data) where T : unmanaged
    {
        data = default;
        void* destination = Unsafe.AsPointer(ref data);
        TemporaryMapMemory(buffer, mappedMemory => Unsafe.CopyBlock(destination, mappedMemory, (uint)buffer.Size));
    }
    */

    #region Memory
    /*
    public MemoryInfo[] AllocateMemory(HLSLBuffer[] buffers)
    {
        // Get memory requirements for each buffer
        MemoryRequirements[] bufferMemoryRequirements = new MemoryRequirements[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
            VkApi.GetBufferMemoryRequirements(_device, _buffers[buffers[i].Id], out bufferMemoryRequirements[i]);

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

        BindingBufferToMemory(buffers);

        return memoryInfos;
    }
    */

    internal Guid AllocateMemory(ulong size, uint memoryTypeIndex)
    {
        MemoryAllocateInfo allocateInfo = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex
            //MemoryTypeIndex = VkApi.FindMemoryType(_physicalDevice, memoryRequirements[i].MemoryTypeBits, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        if (VkApi.AllocateMemory(_device, in allocateInfo, null, out var memory) != Result.Success)
            throw new Exception("Failed to allocate buffer memory!");

        Guid guid = Guid.NewGuid();
        _memory.Add(guid, memory);

        return guid;
    }

    internal void FreeMemory(Guid memoryId)
    {
        if (!_memory.TryGetValue(memoryId, out DeviceMemory memory))
            throw new Exception("Memory not found!");

        VkApi.FreeMemory(_device, memory, null);
        _memory.Remove(memoryId);
    }

    private delegate void MappedMemoryAction(void* mappedMemory);

    private void TemporaryMapMemory(Guid memoryId, ulong offset, ulong size, MappedMemoryAction action)
    {
        if (!_memory.TryGetValue(memoryId, out DeviceMemory memory))
            throw new Exception("Memory not found!");

        void* mappedMemory = null;
        if (VkApi.MapMemory(_device, memory, offset, size, 0, ref mappedMemory) != Result.Success)
            throw new Exception("Failed to map memory!");

        action(mappedMemory);

        VkApi.UnmapMemory(_device, memory);
    }
    #endregion

    #region Descriptor Set
    internal Guid CreateDescriptorSetLayout(DescriptorSetLayoutBinding[] layoutBindings)
    {
        DescriptorSetLayoutCreateInfo layoutCreateInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)layoutBindings.Length,
            PBindings = (DescriptorSetLayoutBinding*)Unsafe.AsPointer(ref layoutBindings[0])
        };

        if (VkApi.CreateDescriptorSetLayout(_device, in layoutCreateInfo, null, out var descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create descriptor set layout!");

        Guid guid = Guid.NewGuid();
        _descriptorSetLayouts.Add(guid, descriptorSetLayout);
        return guid;
    }

    internal Guid AllocateDescriptorSet(Guid descriptorSetLayoutId)
    {
        if (_descriptorSetLayouts.TryGetValue(descriptorSetLayoutId, out var descriptorSetLayout))
            throw new Exception("Descriptor set layout not found!");

        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref descriptorSetLayout)
        };

        if (VkApi.AllocateDescriptorSets(_device, in allocateInfo, out var descriptorSet) != Result.Success)
            throw new Exception("Failed to allocate descriptor set!");

        Guid guid = Guid.NewGuid();
        _descriptorSets.Add(guid, descriptorSet);
        return guid;
    }


    public DescriptorSet CreateDescriptorSet<T>(T shaderBuffer)
    {
        var descriptorSetLayout = CreateDescriptorSetLayout(shaderBuffer);
        var descriptorSet = AllocateDescriptorSet(descriptorSetLayout);
        UpdateDescriptorSet(descriptorSet, shaderBuffer);
        return descriptorSet;
    }

    private DescriptorSetLayout CreateDescriptorSetLayout<T>(T shaderBuffer)
    {
        if (_descriptorSetLayouts.TryGetValue(typeof(T).GUID, out var descriptorSetLayout))
            return descriptorSetLayout;

        var bindings = ShaderBufferMapper<T>.Bindings;
        var layoutBindings = new DescriptorSetLayoutBinding[bindings.Count];

        for (int i = 0; i < bindings.Count; i++)
        {
            HLSLBuffer HLSLbuffer = ShaderBufferMapper<T>.GetBuffer(shaderBuffer, bindings[i]);
            layoutBindings[i].Binding = bindings[i];
            layoutBindings[i].DescriptorCount = 1;
            layoutBindings[i].StageFlags = ShaderStageFlags.ComputeBit;
            layoutBindings[i].DescriptorType = HLSLbuffer.GetDescriptorType();
        }

        DescriptorSetLayoutCreateInfo layoutCreateInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)layoutBindings.Length,
            PBindings = (DescriptorSetLayoutBinding*)Unsafe.AsPointer(ref layoutBindings[0])
        };

        if (VkApi.CreateDescriptorSetLayout(_device, in layoutCreateInfo, null, out descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create descriptor set layout!");

        _descriptorSetLayouts.Add(typeof(T).GUID, descriptorSetLayout);
        return descriptorSetLayout;
    }

    private DescriptorSet AllocateDescriptorSet(DescriptorSetLayout descriptorSetLayout)
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

    public void UpdateDescriptorSet<T>(DescriptorSet descriptorSet, T shaderBuffer)
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
                Offset = HLSLbuffer.Offset,
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

    public void FreeDescriptorSetLayout(DescriptorSetLayout descriptorSetLayout)
    {
        VkApi.DestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
    }

    #endregion

    #region Shader Module
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

        var shaderModuleInfo = new ShaderModuleInfo(functionName);
        _shaderModules.Add(shaderModuleInfo.Guid, shaderModule);
        return shaderModuleInfo;
    }

    public void FreeShaderModule(ShaderModuleInfo shaderModuleInfo)
    {
        if (!_shaderModules.TryGetValue(shaderModuleInfo.Guid, out var shaderModule))
            throw new Exception("Shader module not found!");

        VkApi.DestroyShaderModule(_device, shaderModule, null);
        _shaderModules.Remove(shaderModuleInfo.Guid);
    }
    #endregion

    #region Pipeline

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout descriptorSetLayout)
    {
        var pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref descriptorSetLayout)
        };

        if (VkApi.CreatePipelineLayout(_device, in pipelineLayoutCreateInfo, null, out var pipelineLayout) != Result.Success)
            throw new Exception("Failed to create pipeline layout!");

        _pipelineLayouts.Add(Guid.NewGuid(), pipelineLayout);

        return pipelineLayout;
    }

    private Pipeline CreateComputePipeline(ShaderModuleInfo shaderInfo, PipelineLayout pipelineLayout)
    {
        if (!_shaderModules.TryGetValue(shaderInfo.Guid, out var shaderModule))
            throw new Exception("Shader module not found!");

        var functionName = MarshalUtil.StringToHGlobalUtf8(shaderInfo.FunctionName);
        var pipelineShaderStageCreateInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = shaderModule,
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

        _pipelines.Add(Guid.NewGuid(), pipeline);

        Marshal.FreeHGlobal(functionName);

        return pipeline;
    }

    private void FreePipelineLayout(PipelineLayout pipelineLayout)
    {
        if (!_pipelineLayouts.ContainsValue(pipelineLayout))
            return;
    }

    private void FreePipeline(ComputePipelineInfo pipelinInfo)
    {
        if (!_pipelines.TryGetValue(pipelinInfo.Guid, out var pipeline))
            return;
        VkApi.DestroyPipeline(_device, pipeline, null);
    }

    #endregion

    public void Execute<T1>(ShaderModuleInfo shaderInfo, T1 arg)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(arg));

        var descriptorSet = CreateDescriptorSet(arg);
        var pipelineLayout = CreatePipelineLayout(_descriptorSetLayouts[typeof(T1).GUID]);
        var pipeline = CreateComputePipeline(shaderInfo, pipelineLayout);

        Execute(pipeline, pipelineLayout, descriptorSet);
    }

    private void Execute(Pipeline pipeline, PipelineLayout pipelineLayout, DescriptorSet descriptorSet)
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

    private void FreeAllMemoryWithBuffer()
    {
        foreach (var buffer in _buffers.Values)
            VkApi.DestroyBuffer(_device, buffer, null);
        _buffers.Clear();

        foreach (var memory in _memory.Values)
            VkApi.FreeMemory(_device, memory, null);
        _memory.Clear();
    }

    private void FreeDescriptorPoolWithLayout()
    {
        foreach (var descriptorSetLayout in _descriptorSetLayouts.Values)
            VkApi.DestroyDescriptorSetLayout(_device, descriptorSetLayout, null);
        _descriptorSetLayouts.Clear();

        VkApi.DestroyDescriptorPool(_device, _descriptorPool, null);
    }

    private void FreeShaderModule()
    {
        foreach (var shaderModule in _shaderModules.Values)
            VkApi.DestroyShaderModule(_device, shaderModule, null);
        _shaderModules.Clear();
    }

    private void FreePipelineWithLayout()
    {
        foreach (var pipeline in _pipelines.Values)
            VkApi.DestroyPipeline(_device, pipeline, null);

        foreach (var pipelineLayout in _pipelineLayouts.Values)
            VkApi.DestroyPipelineLayout(_device, pipelineLayout, null);
        _pipelineLayouts.Clear();

        _pipelines.Clear();
    }

    public void Dispose()
    {
        FreeShaderModule();
        FreeAllMemoryWithBuffer();
        VkApi.DestroyCommandPool(_device, _commandPool, null);

        FreePipelineWithLayout();
        FreeDescriptorPoolWithLayout();
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