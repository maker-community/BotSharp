using BotSharp.Plugin.XiaoZhi;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for XiaoZhi plugin
/// </summary>
public static class XiaoZhiPluginExtensions
{
    /// <summary>
    /// Add XiaoZhi stream middleware to the application pipeline
    /// </summary>
    public static IApplicationBuilder UseXiaoZhiStream(this IApplicationBuilder app)
    {
        return app.UseMiddleware<XiaoZhiStreamMiddleware>();
    }
}
