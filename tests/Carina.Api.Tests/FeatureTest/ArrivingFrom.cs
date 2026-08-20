using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ArrivingFrom(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return builder =>
        {
            builder.Use((context, following) =>
            {
                context.Connection.RemoteIpAddress = address;

                return following(context);
            });

            next(builder);
        };
    }
}
