using BotSharp.Abstraction.Plugins;
using BotSharp.Plugin.XiaoZhi.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace BotSharp.Plugin.XiaoZhi;

/// <summary>
/// XiaoZhi server plugin for BotSharp.
/// Implements the XiaoZhi WebSocket protocol to provide realtime voice conversation capabilities.
/// Compatible with xiaozhi-esp32 and other XiaoZhi clients.
/// </summary>
public class XiaoZhiPlugin : IBotSharpAppPlugin
{
    public string Id => "e8c1d737-6c21-49de-b241-cd5c8d9bf979";
    public string Name => "XiaoZhi Server";
    public string? IconUrl => "https://avatars.githubusercontent.com/u/162138609";
    public string Description => "XiaoZhi WebSocket server plugin for realtime voice conversations with ESP32 and other XiaoZhi clients";

    public void RegisterDI(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped(provider =>
        {
            var settingService = provider.GetRequiredService<BotSharp.Abstraction.Settings.ISettingService>();
            return settingService.Bind<XiaoZhiSettings>("XiaoZhi");
        });
    }

    public void Configure(IApplicationBuilder app)
    {
        // Register XiaoZhi WebSocket middleware
        app.UseXiaoZhiStream();
    }
}
