using Silk.NET.Vulkan;

namespace Miko;

static class Global
{
    public static Vk VkApi;
    static Global()
    {
        VkApi = Vk.GetApi();
    }
}