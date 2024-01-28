namespace Miko.VkInstanceInfo;

class ComputePipelineInfo(Guid pipelineLayoutGuid, ShaderModuleInfo shaderModuleInfo)
{
    public readonly Guid Guid = Guid.NewGuid();
    public readonly Guid PipelineLayoutGuid = pipelineLayoutGuid;
    public readonly ShaderModuleInfo ShaderModuleInfo = shaderModuleInfo;
}