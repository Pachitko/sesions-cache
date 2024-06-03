using System.Threading.Channels;
using Core.Models;

namespace Server.Extensions;

public static class DependencyInjection
{
    public static void AddChannel<T>(this IServiceCollection services)
    {
        var sessionDeletionChannel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });

        services.AddSingleton<ChannelReader<T>>(sessionDeletionChannel);
        services.AddSingleton<ChannelWriter<T>>(sessionDeletionChannel);
    }
}