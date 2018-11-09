using System;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Internal;

namespace Microsoft.AspNetCore.Hosting
{
    public class WebHostServiceOptions
    {
        public Action<IApplicationBuilder> ConfigureApplication { get; set; }

        public WebHostOptions Options { get; set; }

        public AggregateException HostingStartupExceptions { get; set; }

        public ExceptionDispatchInfo StartupConfigureServicesError { get; set; }
    }
}
