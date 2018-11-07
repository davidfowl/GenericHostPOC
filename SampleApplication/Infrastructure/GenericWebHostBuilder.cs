using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Builder;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ObjectPool;

namespace Microsoft.AspNetCore.Hosting
{
    public class GenericWebHostBuilder : IWebHostBuilder
    {
        private readonly IHostBuilder _builder;
        private readonly IConfiguration _config;

        public GenericWebHostBuilder(IHostBuilder builder)
        {
            _builder = builder;
            _config = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .Build();

            _builder.ConfigureHostConfiguration(config =>
            {
                config.AddConfiguration(_config);
            });

            _builder.ConfigureServices((context, services) =>
            {
                var webhostContext = GetWebHostBuilderContext(context);

                // Add the IHostingEnvironment and IApplicationLifetime from Microsoft.AspNetCore.Hosting
                services.AddSingleton(webhostContext.HostingEnvironment);
                services.AddSingleton<IApplicationLifetime, WebApplicationLifetime>();

                services.Configure<WebHostServiceOptions>(options =>
                {
                    // Set the options
                    options.Options = (WebHostOptions)context.Properties[typeof(WebHostOptions)];
                });

                services.AddHostedService<WebHostService>();

                // REVIEW: This is bad since we don't own this type. Anybody could add one of these and it would mess things up
                // We need to flow this differently
                var listener = new DiagnosticListener("Microsoft.AspNetCore");
                services.TryAddSingleton<DiagnosticListener>(listener);
                services.TryAddSingleton<DiagnosticSource>(listener);

                services.TryAddSingleton<IHttpContextFactory, HttpContextFactory>();
                services.TryAddScoped<IMiddlewareFactory, MiddlewareFactory>();
                services.TryAddSingleton<IApplicationBuilderFactory, ApplicationBuilderFactory>();

                // Conjure up a RequestServices
                services.TryAddTransient<IStartupFilter, AutoRequestServicesStartupFilter>();
                services.TryAddTransient<IServiceProviderFactory<IServiceCollection>, DefaultServiceProviderFactory>();

                // Ensure object pooling is available everywhere.
                services.TryAddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
            });
        }

        public IWebHost Build()
        {
            return null;
        }

        public IWebHostBuilder ConfigureAppConfiguration(Action<WebHostBuilderContext, IConfigurationBuilder> configureDelegate)
        {
            _builder.ConfigureAppConfiguration((context, builder) =>
            {
                var webhostBuilderContext = GetWebHostBuilderContext(context);
                configureDelegate(webhostBuilderContext, builder);
            });

            return this;
        }

        public IWebHostBuilder ConfigureServices(Action<IServiceCollection> configureServices)
        {
            _builder.ConfigureServices(configureServices);
            return this;
        }

        public IWebHostBuilder ConfigureServices(Action<WebHostBuilderContext, IServiceCollection> configureServices)
        {
            _builder.ConfigureServices((context, builder) =>
            {
                var webhostBuilderContext = GetWebHostBuilderContext(context);
                configureServices(webhostBuilderContext, builder);
            });

            return this;
        }

        // TODO: The extension method can detect this type and do some magic to call this method instead of the regular one
        public IWebHostBuilder UseStartup<TStartup>()
        {
            // Get the startyp type
            var startupType = typeof(TStartup);

            var configureMethod = startupType.GetMethod("Configure", BindingFlags.Public | BindingFlags.Instance);
            var configureServicesMethod = startupType.GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance);
            var configureContainerMethod = startupType.GetMethod("ConfigureContainer", BindingFlags.Public | BindingFlags.Instance);

            // TODO: Figure out how to port IStartupConfigureServicesFilter (this no longer works)

            // TODO: The usual error checking around the shape of the Startup class

            var configureServicesBuilder = new ConfigureServicesBuilder(configureServicesMethod)
            {
                StartupServiceFilters = (f) => f
            };

            var configureBuilder = new ConfigureBuilder(configureMethod);

            _config[HostDefaults.ApplicationKey] = startupType.GetTypeInfo().Assembly.GetName().Name;

            _builder.ConfigureServices((context, services) =>
            {
                var webHostBuilderContext = GetWebHostBuilderContext(context);

                var instance = ActivatorUtilities.CreateInstance(new ServiceProvider(webHostBuilderContext), startupType);

                var configureServices = configureServicesBuilder.Build(instance);
                configureServices(services);

                services.Configure<WebHostServiceOptions>(options =>
                {
                    options.ConfigureApplication = configureBuilder.Build(instance);
                });
            });

            // TODO: Make ConfigureContainer work
            //if (configureContainerMethod != null)
            //{
            //    var ihostBuilderConfigureContainerMethod = typeof(IHostBuilder).GetMethods().First(m => m.Name == "ConfigureContainer");

            //    // Get the parameter type from the ConfigureContainer method on 
            //    var containerType = configureContainerMethod.GetParameters()[0].ParameterType;

            //    // _builder.ConfigureContainer<T>(container => 
            //    // {
            //    //    configureServicesMethod.Invoke(container);
            //    // });

            //    ihostBuilderConfigureContainerMethod.MakeGenericMethod(containerType).Invoke(_builder, new object[] { });
            //}

            return this;
        }

        // TODO: The extension method can detect this type and do some magic to call this method instead of the regular one
        public IWebHostBuilder Configure(Action<IApplicationBuilder> configure)
        {
            _builder.ConfigureServices((context, services) =>
            {
                services.Configure<WebHostServiceOptions>(options =>
                {
                    options.ConfigureApplication = configure;
                });
            });

            return this;
        }

        private WebHostBuilderContext GetWebHostBuilderContext(HostBuilderContext context)
        {
            if (!context.Properties.TryGetValue(typeof(WebHostBuilderContext), out var contextVal))
            {
                var options = new WebHostOptions(_config, Assembly.GetEntryAssembly()?.GetName().Name);
                var hostingEnvironment = new HostingEnvironment();
                hostingEnvironment.Initialize(context.HostingEnvironment.ContentRootPath, options);

                var webHostBuilderContext = new WebHostBuilderContext
                {
                    Configuration = context.Configuration,
                    HostingEnvironment = hostingEnvironment
                };
                context.Properties[typeof(WebHostBuilderContext)] = webHostBuilderContext;
                context.Properties[typeof(WebHostOptions)] = options;
                return webHostBuilderContext;
            }

            return (WebHostBuilderContext)contextVal;
        }

        public string GetSetting(string key)
        {
            return _config[key];
        }

        public IWebHostBuilder UseSetting(string key, string value)
        {
            _config[key] = value;
            return this;
        }

        // This exists just so that we can use ActivatorUtilities.CreateInstance on the Startup class
        private class ServiceProvider : IServiceProvider
        {
            private readonly WebHostBuilderContext _context;

            public ServiceProvider(WebHostBuilderContext context)
            {
                _context = context;
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(Microsoft.AspNetCore.Hosting.IHostingEnvironment))
                {
                    return _context.HostingEnvironment;
                }

                if (serviceType == typeof(IConfiguration))
                {
                    return _context.Configuration;
                }

                if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return Array.CreateInstance(serviceType.GetGenericArguments()[0], 0);
                }

                return null;
            }
        }
    }
}