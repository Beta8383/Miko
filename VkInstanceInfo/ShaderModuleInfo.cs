namespace Miko.VkInstanceInfo;

class ShaderModuleInfo(string functionName)
{
    public readonly Guid Guid = Guid.NewGuid();
    public readonly string FunctionName = functionName;
}