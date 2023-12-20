using Miko;
using Miko.HLSLBuffer;

internal class Program
{
    struct UniformBuffer
    {
        public uint a;
    }

    struct Data
    {
        [Binding(0)]
        public StructuredBuffer<float> data1;

        [Binding(1)]
        public ConstantBuffer<UniformBuffer> constData;

        [Binding(2)]
        public StructuredBuffer<float> result;
    }

    private static void Main(string[] args)
    {
        const int size = 100;
        Span<float> data1 = new float[size];
        for (int i = 0; i < size; i++)
            data1[i] = i;
        Span<uint> constData = [10];
        Span<float> result = new float[size];

        ComputeKernel kernel = new(1, true);

        var bufferInfo1 = kernel.AllocateBuffer((ulong)(data1.Length * sizeof(float)), false);
        kernel.WriteDataIntoBuffer(bufferInfo1, data1);
        var constBufferInfo = kernel.AllocateBuffer((ulong)(constData.Length * sizeof(float)), true);
        kernel.WriteDataIntoBuffer(constBufferInfo, constData);

        var descriptorSetLayout = kernel.CreateDescriptorSetLayout(1, 2);
        var descriptorSetInfo = kernel.AllocateDescriptorSet(descriptorSetLayout);
        kernel.UpdateDescriptorSet(descriptorSetInfo, [constBufferInfo, bufferInfo1]);

        const string path = @"./TestShader/multiply.spv";
        var shaderCode = File.ReadAllBytes(path);
        var shaderModule = kernel.CreateShaderModule(shaderCode);

        var pipelineLayout = kernel.CreateComputePineLineLayout(descriptorSetLayout);
        var pipelineInfo = kernel.CreateComputePipeLine(shaderModule, pipelineLayout);

        kernel.Execute(pipelineInfo, descriptorSetInfo);
        kernel.ReadDataFromBuffer(bufferInfo1, result);
        foreach (var item in result)
            Console.Write(item + " ");

        Console.WriteLine();

        kernel.FreeBuffer(bufferInfo1);
        kernel.FreeBuffer(constBufferInfo);
        kernel.FreeDescriptorSetLayout(descriptorSetLayout);
        kernel.FreeShaderModule(shaderModule);
        kernel.FreeComputePipelineLayout(pipelineLayout);
        kernel.FreeComputePipeline(pipelineInfo);
        kernel.Dispose();

        Task.Delay(2000).Wait();
    }
}