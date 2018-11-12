using System;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting.Internal;

namespace Microsoft.Extensions.Hosting
{
    public static class GenericHostBuilderExtensions
    {
        public static IHostBuilder ConfigureWebHostDefaults(this IHostBuilder builder, Action<GenericWebHostBuilder> configure)
        {
            return ConfigureWebHost(builder, webHostBuilder =>
            {
                webHostBuilder.UseKestrel((builderContext, options) =>
                {
                    options.Configure(builderContext.Configuration.GetSection("Kestrel"));
                });
                webHostBuilder.UseIIS();
                webHostBuilder.UseIISIntegration();
                
                webHostBuilder.ConfigureServices((hostingContext, services) =>
                {
                    // Fallback
                    services.PostConfigure<HostFilteringOptions>(options =>
                    {
                        if (options.AllowedHosts == null || options.AllowedHosts.Count == 0)
                        {
                            // "AllowedHosts": "localhost;127.0.0.1;[::1]"
                            var hosts = hostingContext.Configuration["AllowedHosts"]?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            // Fall back to "*" to disable.
                            options.AllowedHosts = (hosts?.Length > 0 ? hosts : new[] { "*" });
                        }
                    });
                    // Change notification
                    services.AddSingleton<IOptionsChangeTokenSource<HostFilteringOptions>>(
                        new ConfigurationChangeTokenSource<HostFilteringOptions>(hostingContext.Configuration));

                    services.AddTransient<IStartupFilter, HostFilteringStartupFilter>();
                });

                configure(webHostBuilder);
            });
        }

        public static IHostBuilder ConfigureWebHost(this IHostBuilder builder, Action<GenericWebHostBuilder> configure)
        {
            var webhostBuilder = new GenericWebHostBuilder(builder);
            configure(webhostBuilder);
            return builder;
        }
    }
}
