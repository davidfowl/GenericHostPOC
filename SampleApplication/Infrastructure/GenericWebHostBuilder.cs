using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly object _startupKey = new object();

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

            // TODO: Figure out how to port IStartupConfigureServicesFilter (this no longer works)

            _config[HostDefaults.ApplicationKey] = startupType.GetTypeInfo().Assembly.GetName().Name;

            _builder.ConfigureServices((context, services) =>
            {
                var webHostBuilderContext = GetWebHostBuilderContext(context);

                var instance = ActivatorUtilities.CreateInstance(new ServiceProvider(webHostBuilderContext), startupType);
                context.Properties[_startupKey] = instance;

                // Startup.ConfigureServices
                var configureServicesBuilder = StartupReflectionLoader.FindConfigureServicesDelegate(startupType, context.HostingEnvironment.EnvironmentName);
                var configureServices = configureServicesBuilder.Build(instance);
                configureServices(services);

                // We cannot support methods that return IServiceProvider as that is terminal and we need ConfigureServices to compose

                // Startup.Configure
                var configureBuilder = StartupReflectionLoader.FindConfigureDelegate(startupType, context.HostingEnvironment.EnvironmentName);
                services.Configure<WebHostServiceOptions>(options =>
                {
                    options.ConfigureApplication = configureBuilder.Build(instance);
                });

                // Startup.ConfigureContainer
                var configureContainerBuilder = StartupReflectionLoader.FindConfigureContainerDelegate(startupType, context.HostingEnvironment.EnvironmentName);
                if (configureContainerBuilder.MethodInfo != null)
                {
                    var containerType = configureContainerBuilder.GetContainerType();
                    // Store the builder in the property bag
                    _builder.Properties[typeof(ConfigureContainerBuilder)] = configureContainerBuilder;

                    var actionType = typeof(Action<,>).MakeGenericType(typeof(HostBuilderContext), containerType);

                    // Get the private ConfigureContainer method on this type then close over the container type
                    var configureCallback = GetType().GetMethod(nameof(ConfigureContainer), BindingFlags.NonPublic | BindingFlags.Instance)
                                                     .MakeGenericMethod(containerType)
                                                     .CreateDelegate(actionType, this);

                    // _builder.ConfigureContainer<T>(ConfigureContainer);
                    typeof(IHostBuilder).GetMethods().First(m => m.Name == nameof(IHostBuilder.ConfigureContainer))
                        .MakeGenericMethod(containerType)
                        .Invoke(_builder, new object[] { configureCallback });
                }
            });

            return this;
        }

        private void ConfigureContainer<TContainer>(HostBuilderContext context, TContainer container)
        {
            var instance = context.Properties[_startupKey];
            var builder = (ConfigureContainerBuilder)context.Properties[typeof(ConfigureContainerBuilder)];
            builder.Build(instance)(container);
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