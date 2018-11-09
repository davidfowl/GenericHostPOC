using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Logging
{
    internal static class HostingLoggerExtensions
    {
        public static void HostingStartupAssemblyError(this ILogger logger, Exception exception)
        {
            logger.ApplicationError(
                eventId: 0,
                message: "Hosting startup assembly exception",
                exception: exception);
        }
        public static void ApplicationError(this ILogger logger, Exception exception)
        {
            logger.ApplicationError(
                eventId: 0,
                message: "Application startup exception",
                exception: exception);
        }

        public static void ApplicationError(this ILogger logger, EventId eventId, string message, Exception exception)
        {
            var reflectionTypeLoadException = exception as ReflectionTypeLoadException;
            if (reflectionTypeLoadException != null)
            {
                foreach (var ex in reflectionTypeLoadException.LoaderExceptions)
                {
                    message = message + Environment.NewLine + ex.Message;
                }
            }

            logger.LogCritical(
                eventId: eventId,
                message: message,
                exception: exception);
        }
    }
}
