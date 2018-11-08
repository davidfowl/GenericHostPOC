using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting
{
    // REVIEW: Should this go on HostBuilder itself?
    public class Host
    {
        public static IHostBuilder CreateDefaultBuilder(string[] args)
        {
            return new HostBuilder()
               .ConfigureLogging((hostingContext, logging) =>
               {
                   logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                   logging.AddConsole();
                   logging.AddDebug();
                   logging.AddEventSourceLogger();
               })
               .UseContentRoot(Directory.GetCurrentDirectory())
               .ConfigureHostConfiguration(config =>
               {
                   // REVIEW: Do we need to do this?
                   if (args != null)
                   {
                       config.AddCommandLine(args);
                   }
               })
               .ConfigureAppConfiguration((hostingContext, config) =>
               {
                   var env = hostingContext.HostingEnvironment;

                   config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                         .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                   if (env.IsDevelopment())
                   {
                       var appAssembly = Assembly.Load(new AssemblyName(env.ApplicationName));
                       if (appAssembly != null)
                       {
                           config.AddUserSecrets(appAssembly, optional: true);
                       }
                   }

                   config.AddEnvironmentVariables();

                   if (args != null)
                   {
                       config.AddCommandLine(args);
                   }
               })
               .UseServiceProviderFactory(new DefaultServiceProviderFactory(new ServiceProviderOptions
               {
                   ValidateScopes = true
               }));
        }
    }
}
