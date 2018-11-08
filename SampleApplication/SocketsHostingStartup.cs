using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(SampleApplication.SocketsHostingStartup))]

namespace SampleApplication
{
    public class SocketsHostingStartup : IHostingStartup
    {
        public void Configure(IWebHostBuilder builder)
        {
            builder.UseSockets(o => o.IOQueueCount = 1);
        }
    }
}
