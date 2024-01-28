using Miko;
using Miko.Attributes;
using Miko.HLSLBuffers;

internal class Program
{
    struct UniformBuffer
    {
        public uint a;
    }

    struct Data
    {
        [Binding(1)]
        public RWStructuredBuffer<float> data1;

        [Binding(0)]
        public ConstantBuffer<UniformBuffer> constData;
    }

    private static void Main(string[] args)
    {
        const int size = 100;
        float[] data1 = new float[size];
        for (int i = 0; i < size; i++)
            data1[i] = i;

        UniformBuffer constData = new() { a = 10 };

        using (ComputeKernel kernel = new(1, true))
        {

            Data data = new()
            {
                data1 = new(size),
                constData = new(),
            };

            var memoryInfos = kernel.AllocateShaderBuffer(data);

            kernel.WriteBuffer(data.constData, constData);
            kernel.WriteBuffer<float>(data.data1, data1);

            var shader = kernel.CreateShaderModule(File.ReadAllBytes("./TestShader/multiply.spv"));
            kernel.Execute(shader, data);

            kernel.ReadBuffer(ShaderBufferMapper<Data>.GetBuffer(data, 1), out data1);

            foreach (var memoryInfo in memoryInfos)
                kernel.FreeMemoryWithBuffers(memoryInfo);
        }
        
        foreach (var d in data1)
            Console.Write(d + " ");
        Console.WriteLine();

        Task.Delay(2000).Wait();
    }
}