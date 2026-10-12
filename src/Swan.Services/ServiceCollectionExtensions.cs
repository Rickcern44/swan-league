using Microsoft.Extensions.Hosting;
using Swan.Data;

namespace Swan.Services;

public static class ServiceCollectionExtensions
{
    public static TBuilder AddSwanServices<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddNpgsqlDbContext<SwanDbContext>("swan");
        return builder;
    }
}
