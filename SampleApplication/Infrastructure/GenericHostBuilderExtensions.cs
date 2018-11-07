using System;
using Microsoft.Extensions.Hosting;

namespace Microsoft.AspNetCore.Hosting
{
    public static class GenericHostBuilderExtensions
    {
        public static IHostBuilder ConfigureWebHostBuilderWithDefaults(this IHostBuilder builder, Action<GenericWebHostBuilder> configure)
        {
            return ConfigureWebHostBuilder(builder, webHostBuilder =>
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

        public static IHostBuilder ConfigureWebHostBuilder(this IHostBuilder builder, Action<GenericWebHostBuilder> configure)
        {
            var webhostBuilder = new GenericWebHostBuilder(builder);
            configure(webhostBuilder);
            return builder;
        }
    }
}
