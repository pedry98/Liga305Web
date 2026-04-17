using Liga305.Infrastructure.BotWorker;
using Liga305.Infrastructure.Matches;
using Liga305.Infrastructure.OpenDota;
using Liga305.Infrastructure.Persistence;
using Liga305.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Liga305.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLiga305Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<Liga305DbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.Configure<SteamWebApiOptions>(configuration.GetSection("Steam"));
        services.AddHttpClient<SteamWebApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.steampowered.com/");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.Configure<BotWorkerOptions>(configuration.GetSection("BotWorker"));
        services.AddHttpClient<BotWorkerClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<BotWorkerOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.DefaultRequestHeaders.Add("x-worker-secret", opts.SharedSecret);
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<MatchSettlementService>();

        services.AddHttpClient<OpenDotaClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.opendota.com/api/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "Liga305Web/0.1");
        });

        services.AddHostedService<MatchResultPoller>();

        return services;
    }
}
