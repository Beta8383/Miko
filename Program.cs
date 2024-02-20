using System.Runtime.CompilerServices;
using Miko;
using Miko.HLSLBuffers;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

internal class Program
{
    static void Main(string[] args)
    {
        float[] data = new float[10];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }

        ulong size = (ulong)(data.Length * sizeof(float));
        ComputeKernel kernel = new(1, true);
        Buffer buffer = kernel.CreateBuffer(BufferUsageFlags.StorageBufferBit, size);
        DeviceMemory memory = kernel.AllocateMemoryForBuffer(buffer, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        unsafe
        {
            kernel.WriteBuffer(memory, size, 0, Unsafe.AsPointer(ref data[0]));
        }

        // read back
        float[] data2 = new float[10];
        unsafe
        {
            kernel.ReadBuffer(memory, size, 0, Unsafe.AsPointer(ref data2[0]));
        }
        // check
        for (int i = 0; i < data.Length; i++)
        {
            System.Console.WriteLine(data2[i]);
        }
        kernel.Dispose();
    }

    public void CreateComputeShader(Span<byte> shaderCode, (uint binding, BufferType bufferTypes)[] bufferLayout, string functionName = "main")
    {
        ComputeKernel kernel = new(1, true);
        var shader = kernel.CreateShaderModule(shaderCode);

        var layoutBindings = new DescriptorSetLayoutBinding[bufferLayout.Length];
        for (int i = 0; i < bufferLayout.Length; i++)
        {
            layoutBindings[i].Binding = bufferLayout[i].binding;
            layoutBindings[i].DescriptorCount = 1;
            layoutBindings[i].StageFlags = ShaderStageFlags.ComputeBit;
            layoutBindings[i].DescriptorType = bufferLayout[i].bufferTypes.ToVkDescriptorType();
        }

        var descriptorSetLayout = kernel.CreateDescriptorSetLayout(layoutBindings);
        var pipelineLayout = kernel.CreatePipelineLayout(descriptorSetLayout);
        var pipeline = kernel.CreatePipeline(shader, functionName, pipelineLayout);
    }
}