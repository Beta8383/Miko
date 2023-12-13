using Silk.NET.Vulkan;

static class Global
{
    public static Vk VkApi;
    static Global()
    {
        VkApi = Vk.GetApi();
    }
}