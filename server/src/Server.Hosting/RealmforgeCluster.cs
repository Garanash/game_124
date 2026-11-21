using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Serialization;
using Realmforge.Server.Core;
using Realmforge.Server.Grains;
using StackExchange.Redis;

namespace Realmforge.Server.Hosting;

public sealed class ClusterPortOptions
{
    public const string SectionName = "Cluster";

    public int SiloPort { get; set; } = EndpointOptions.DEFAULT_SILO_PORT;
    public int GatewayPort { get; set; } = EndpointOptions.DEFAULT_GATEWAY_PORT;
}

/// <summary>
/// One place that decides how processes find the Orleans cluster: Redis membership when a Redis connection
/// string is configured, a single localhost silo otherwise (local runs, CI).
/// </summary>
public static class RealmforgeCluster
{
    public const string ClusterId = "realmforge";
    public const string ServiceId = "realmforge";

    public static ISiloBuilder UseRealmforgeCluster(this ISiloBuilder silo, IConfiguration configuration)
    {
        silo.Configure<ClusterOptions>(ConfigureIds);
        ClusterPortOptions ports = Ports(configuration);
        string? redis = configuration.GetConnectionString(ConnectionNames.Redis);
        if (string.IsNullOrWhiteSpace(redis))
        {
            silo.UseLocalhostClustering(ports.SiloPort, ports.GatewayPort);
            silo.UseInMemoryReminderService();
            return silo;
        }
        silo.UseRedisClustering(options => options.ConfigurationOptions = ConfigurationOptions.Parse(redis));
        silo.UseRedisReminderService(options => options.ConfigurationOptions = ConfigurationOptions.Parse(redis));
        silo.ConfigureEndpoints(siloPort: ports.SiloPort, gatewayPort: ports.GatewayPort, listenOnAnyHostAddress: true);
        return silo;
    }

    /// <summary>Gateway, zone and store processes are cluster clients; they never host grains.</summary>
    public static IHostApplicationBuilder AddRealmforgeClusterClient(this IHostApplicationBuilder builder)
    {
        string? redis = builder.Configuration.GetConnectionString(ConnectionNames.Redis);
        ClusterPortOptions ports = Ports(builder.Configuration);
        builder.UseOrleansClient(client =>
        {
            client.Configure<ClusterOptions>(ConfigureIds);
            client.Services.AddSerializer(serializer => serializer.AddRealmforgeSerializers());
            if (string.IsNullOrWhiteSpace(redis))
            {
                client.UseLocalhostClustering(ports.GatewayPort);
            }
            else
            {
                client.UseRedisClustering(options => options.ConfigurationOptions = ConfigurationOptions.Parse(redis));
            }
        });
        return builder;
    }

    private static ClusterPortOptions Ports(IConfiguration configuration) =>
        configuration.GetSection(ClusterPortOptions.SectionName).Get<ClusterPortOptions>() ?? new ClusterPortOptions();

    private static void ConfigureIds(ClusterOptions options)
    {
        options.ClusterId = ClusterId;
        options.ServiceId = ServiceId;
    }
}
