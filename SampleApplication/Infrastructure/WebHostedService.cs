using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Hosting
{
    public class WebHostService : IHostedService
    {
        public WebHostService(IOptions<WebHostServiceOptions> options,
                              IServiceProvider services,
                              IServer server,
                              ILogger<WebHostService> logger,
                              DiagnosticListener diagnosticListener,
                              IHttpContextFactory httpContextFactory,
                              IApplicationBuilderFactory applicationBuilderFactory,
                              IEnumerable<IStartupFilter> startupFilters,
                              IConfiguration configuration)
        {
            Options = options?.Value ?? throw new System.ArgumentNullException(nameof(options));

            if (Options.ConfigureApplication == null)
            {
                throw new ArgumentException(nameof(Options.ConfigureApplication));
            }

            Services = services ?? throw new ArgumentNullException(nameof(services));
            Server = server ?? throw new ArgumentNullException(nameof(server));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            DiagnosticListener = diagnosticListener ?? throw new ArgumentNullException(nameof(diagnosticListener));
            HttpContextFactory = httpContextFactory ?? throw new ArgumentNullException(nameof(httpContextFactory));
            ApplicationBuilderFactory = applicationBuilderFactory ?? throw new ArgumentNullException(nameof(applicationBuilderFactory));
            StartupFilters = startupFilters ?? throw new ArgumentNullException(nameof(startupFilters));
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public WebHostServiceOptions Options { get; }
        public IServiceProvider Services { get; }
        public HostBuilderContext HostBuilderContext { get; }
        public IServer Server { get; }
        public ILogger<WebHostService> Logger { get; }
        public DiagnosticListener DiagnosticListener { get; }
        public IHttpContextFactory HttpContextFactory { get; }
        public IApplicationBuilderFactory ApplicationBuilderFactory { get; }

        public IEnumerable<IStartupFilter> StartupFilters { get; }
        public IConfiguration Configuration { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // TODO: Add this logic https://github.com/aspnet/Hosting/blob/d7b9fd480765bdc01f06441f308fb288e6001049/src/Microsoft.AspNetCore.Hosting/Internal/WebHost.cs#L199-L302

            var serverAddressesFeature = Server.Features?.Get<IServerAddressesFeature>();
            var addresses = serverAddressesFeature?.Addresses;
            if (addresses != null && !addresses.IsReadOnly && addresses.Count == 0)
            {
                var urls = Configuration[WebHostDefaults.ServerUrlsKey];
                if (!string.IsNullOrEmpty(urls))
                {
                    serverAddressesFeature.PreferHostingUrls = WebHostUtilities.ParseBool(Configuration, WebHostDefaults.PreferHostingUrlsKey);

                    foreach (var value in urls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        addresses.Add(value);
                    }
                }
            }

            var builder = ApplicationBuilderFactory.CreateBuilder(Server.Features);
            Action<IApplicationBuilder> configure = Options.ConfigureApplication;

            foreach (var filter in StartupFilters.Reverse())
            {
                configure = filter.Configure(configure);
            }

            configure(builder);
            var application = builder.Build();

            var httpApplication = new HostingApplication(application, Logger, DiagnosticListener, HttpContextFactory);

            await Server.StartAsync(httpApplication, cancellationToken);

            var serverAddresses = Server.Features.Get<IServerAddressesFeature>()?.Addresses;
            if (serverAddresses != null)
            {
                foreach (var address in serverAddresses)
                {
                    Logger.LogInformation("Now listening on: {address}", address);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Server.StopAsync(cancellationToken);
        }
    }
}