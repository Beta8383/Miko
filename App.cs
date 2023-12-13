using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Buffer = Silk.NET.Vulkan.Buffer;
using static Global;
using Miko.Vulkan.Extensions;
using Miko.Util;

unsafe class App(bool enableValidationLayers = false) : IDisposable
{
    const int Width = 800;
    const int Height = 600;

    IWindow? window;
    KhrSurface? khrSurface;
    SurfaceKHR surface;

    Instance instance;
    PhysicalDevice physicalDevice;
    uint queueFamilyIndex;
    Device device;
    Queue queue;

    Buffer buffer;
    DeviceMemory memory;

    DescriptorSetLayout descriptorSetLayout;
    DescriptorPool descriptorPool;
    DescriptorSet descriptorSet;

    ShaderModule computeShaderModule;
    PipelineLayout pipelineLayout;
    Pipeline pipeline;

    CommandPool commandPool;

    public readonly bool EnableValidationLayers = enableValidationLayers;
    readonly string[] ValidationLayers = ["VK_LAYER_KHRONOS_validation"];

    ExtDebugUtils? debugUtils;
    DebugUtilsMessengerEXT debugMessenger;

    const string ComputeShaderPath = @"/Users/beta/Desktop/Project/Miko/multiply.spv";

    public void Run()
    {
        CreateWindow();
        InitVulkan();

        (physicalDevice, queueFamilyIndex) = VkApi.GetPhysicalDeviceWithQueueFamilies(instance, QueueFlags.ComputeBit).First();

        CreateLogicalDevice();
        VkApi.GetDeviceQueue(device, queueFamilyIndex, 0, out queue);

        InitBuffer();
        CreateDescriptorSetLayout();
        CreateDescriptorPool();
        CreateDescriptorSet(descriptorSetLayout);
        UpdateDescriptorSets();

        CreateShaderModule(ComputeShaderPath);
        CreateComputePipeLine(descriptorSetLayout);
        CreateCommandPool();

        Execute();

        window!.Run();
    }

    private void InitVulkan()
    {
        CreateInstance();
        SetupDebugMessenger();
    }

    private void CreateInstance()
    {
        if (EnableValidationLayers && !VkApi.CheckValidationLayerSupport(ValidationLayers))
            throw new Exception("Validation layers requested, but not available!");

        using var appName = new AutoReleasePointer(Marshal.StringToHGlobalAnsi("Silk.NET Window"));
        using var engineName = new AutoReleasePointer(Marshal.StringToHGlobalAnsi("Silk.NET"));

        ApplicationInfo appInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)appName.Pointer,
            ApplicationVersion = new Version32(1, 0, 0),
            PEngineName = (byte*)engineName.Pointer,
            EngineVersion = new Version32(1, 0, 0),
            ApiVersion = Vk.Version13
        };

        //Get required extensions
        var extensions = window!.VkSurface!.GetRequiredExtensions(EnableValidationLayers);
        if (!VkApi.CheckExtensionSupport(extensions))
            throw new Exception("Extensions requested, but not available!");
        var extensionNames = SilkMarshal.StringArrayToPtr(extensions);

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = (byte**)extensionNames,
        };

        if (OperatingSystem.IsMacOS())
            createInfo.Flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;

        //var layersName = MarshalUtil.StringArrayToHGlobalAnsi(ValidationLayers);
        var layersName = SilkMarshal.StringArrayToPtr(ValidationLayers);
        if (EnableValidationLayers)
        {
            createInfo.EnabledLayerCount = (uint)ValidationLayers.Length;
            createInfo.PpEnabledLayerNames = (byte**)layersName;

            var debugCreateInfo = PopulateDebugMessengerCreateInfo();
            createInfo.PNext = &debugCreateInfo;
        }

        var result = VkApi.CreateInstance(in createInfo, null, out instance);
        if (result != Result.Success)
            throw new Exception("Failed to create instance: " + result.ToString());

        SilkMarshal.Free(extensionNames);
        //Marshal.FreeHGlobal(extensionNames);
        //Marshal.FreeHGlobal(layersName);
        SilkMarshal.Free(layersName);
    }

    #region Window

    private void CreateWindow()
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(Width, Height),
            Title = "Silk.NET Window",
            IsEventDriven = true,
            UpdatesPerSecond = 60,
            FramesPerSecond = 60,
        };
        window = Window.Create(options);
        window.Initialize();

        if (window.VkSurface is null)
            throw new Exception("Vulkan surface creation failed");
    }

    private void CreateSurface()
    {
        //KhrSurface has automatic created
        if (!VkApi.TryGetInstanceExtension(instance, out khrSurface))
            throw new Exception("Failed to get KhrSurface extension");

        surface = window!.VkSurface!.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
    }

    #endregion

    #region Buffer

    private void InitBuffer()
    {
        //1 to 100
        Span<float> data = new float[16];
        for (int i = 0; i < data.Length; i++)
            data[i] = i + 1;

        CreateBuffer(device, (ulong)(sizeof(float) * data.Length));
        memory = VkApi.AllocateMemory(physicalDevice, device, buffer, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        VkApi.WriteMemory(device, memory, data);
    }

    public void CreateBuffer(Device device, ulong size)
    {
        BufferCreateInfo bufferCreateInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.StorageBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        if (VkApi.CreateBuffer(device, &bufferCreateInfo, null, out buffer) != Result.Success)
            throw new Exception("Failed to create buffer!");
    }

    #endregion

    #region Argument Description
    private void CreateDescriptorSetLayout()
    {
        DescriptorSetLayoutBinding layoutBinding = new()
        {
            Binding = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit
        };

        DescriptorSetLayoutCreateInfo layoutCreateInfo = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &layoutBinding
        };

        if (VkApi.CreateDescriptorSetLayout(device, &layoutCreateInfo, null, out descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create descriptor set layout!");
    }

    private void CreateDescriptorPool()
    {
        DescriptorPoolSize poolSize = new()
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = 1
        };

        DescriptorPoolCreateInfo poolCreateInfo = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 1
        };

        if (VkApi.CreateDescriptorPool(device, &poolCreateInfo, null, out descriptorPool) != Result.Success)
            throw new Exception("Failed to create descriptor pool!");
    }

    private void CreateDescriptorSet(DescriptorSetLayout descriptorSetLayout)
    {
        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &descriptorSetLayout
        };

        if (VkApi.AllocateDescriptorSets(device, in allocateInfo, out descriptorSet) != Result.Success)
            throw new Exception("Failed to allocate descriptor set!");
    }

    private void UpdateDescriptorSets()
    {
        DescriptorBufferInfo bufferInfo = new()
        {
            Buffer = buffer,
            Offset = 0,
            Range = sizeof(float) * 16
        };

        WriteDescriptorSet writeDescriptorSet = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo
        };

        VkApi.UpdateDescriptorSets(device, 1, in writeDescriptorSet, 0, null);
    }

    #endregion

    private void CreateShaderModule(string path)
    {
        var shaderCode = File.ReadAllBytes(path);
        fixed (byte* ptr = shaderCode)
        {
            var shaderModuleCreateInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)shaderCode.Length,
                PCode = (uint*)ptr
            };


            if (VkApi.CreateShaderModule(device, in shaderModuleCreateInfo, null, out computeShaderModule) != Result.Success)
                throw new Exception("Failed to create compute shader module!");
        }
    }

    private void CreateComputePipeLine(DescriptorSetLayout descriptorSetLayout)
    {
        var pipelineShaderStageCreateInfo = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.ComputeBit,
            Module = computeShaderModule,
            PName = (byte*)SilkMarshal.StringToPtr("main")
        };

        var pipelineLayoutCreateInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &descriptorSetLayout
        };

        if (VkApi.CreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out pipelineLayout) != Result.Success)
            throw new Exception("Failed to create pipeline layout!");

        var computePipelineCreateInfo = new ComputePipelineCreateInfo
        {
            SType = StructureType.ComputePipelineCreateInfo,
            Stage = pipelineShaderStageCreateInfo,
            Layout = pipelineLayout,
        };

        if (VkApi.CreateComputePipelines(device, new PipelineCache(), 1, in computePipelineCreateInfo, null, out pipeline) != Result.Success)
            throw new Exception("Failed to create compute pipeline!");
    }

    private void CreateCommandPool()
    {
        CommandPoolCreateInfo createInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamilyIndex
        };

        if (VkApi.CreateCommandPool(device, &createInfo, null, out commandPool) != Result.Success)
            throw new Exception("Failed to create command pool!");
    }

    private void Execute()
    {
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (VkApi.AllocateCommandBuffers(device, &allocateInfo, out CommandBuffer commandBuffer) != Result.Success)
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
        VkApi.CmdDispatch(commandBuffer, 16, 1, 1);
        VkApi.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        if (VkApi.QueueSubmit(queue, 1, in submitInfo, default) != Result.Success)
            throw new Exception("Failed to submit queue!");

        //Wait for the compute shader to finish its job
        if (VkApi.QueueWaitIdle(queue) != Result.Success)
            throw new Exception("Failed to wait idle!");

        Span<float> result = new float[16];
        VkApi.ReadMemory(device, memory, result);

        for (int i = 0; i < result.Length; i++)
            Debug.Write(result[i] + " ");
        Debug.WriteLine("");
    }

    private void CreateLogicalDevice()
    {
        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        var deviceFeatures = new PhysicalDeviceFeatures();

        var extensionName = SilkMarshal.StringToPtr("VK_KHR_portability_subset");
        var deviceCreateInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            EnabledExtensionCount = 1,
            PpEnabledExtensionNames = (byte**)&extensionName,
            PEnabledFeatures = &deviceFeatures
        };

        if (VkApi.CreateDevice(physicalDevice, &deviceCreateInfo, null, out device) != Result.Success)
            throw new Exception("Failed to create logical device!");

        SilkMarshal.Free(extensionName);
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

    private void SetupDebugMessenger()
    {
        if (!EnableValidationLayers)
            return;

        if (!VkApi.TryGetInstanceExtension(instance, out debugUtils))
            throw new Exception("Failed to get DebugUtilsMessengerExt extension");

        var createInfo = PopulateDebugMessengerCreateInfo();
        if (debugUtils!.CreateDebugUtilsMessenger(instance, &createInfo, null, out debugMessenger) != Result.Success)
            throw new Exception("Failed to set up debug messenger");
    }

    private uint DebugCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageType,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
        Debug.WriteLine($"[{messageSeverity}] {Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage)}");
        return Vk.False;
    }
    #endregion

    public void Dispose()
    {
        window?.Dispose();

        VkApi.DestroyBuffer(device, buffer, null);
        VkApi.FreeMemory(device, memory, null);

        VkApi.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);
        VkApi.DestroyDescriptorPool(device, descriptorPool, null);

        VkApi.DestroyShaderModule(device, computeShaderModule, null);
        VkApi.DestroyPipelineLayout(device, pipelineLayout, null);
        VkApi.DestroyPipeline(device, pipeline, null);

        VkApi.DestroyCommandPool(device, commandPool, null);

        khrSurface?.DestroySurface(instance, surface, null);

        VkApi?.DestroyDevice(device, null);

        if (EnableValidationLayers)
            debugUtils?.DestroyDebugUtilsMessenger(instance, debugMessenger, null);

        VkApi?.DestroyInstance(instance, null);
        VkApi?.Dispose();
    }
}