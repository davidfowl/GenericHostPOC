using System;
using Microsoft.AspNetCore.Hosting;

namespace Microsoft.Extensions.Hosting
{
    public static class GenericHostBuilderExtensions
    {
        public static IHostBuilder ConfigureWebHostDefaults(this IHostBuilder builder, Action<GenericWebHostBuilder> configure)
        {
            return ConfigureWebHost(builder, webHostBuilder =>
            {
                // TODO: Add this https://github.com/aspnet/MetaPackages/blob/62d9794c633a9a2c502334d525d81c454ac29264/src/Microsoft.AspNetCore/WebHost.cs#L195-L212
                webHostBuilder.UseIISIntegration();
                webHostBuilder.UseKestrel((builderContext, options) =>
                {
                    options.Configure(builderContext.Configuration.GetSection("Kestrel"));
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
