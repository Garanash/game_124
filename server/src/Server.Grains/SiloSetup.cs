using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orleans.Serialization;
using Orleans.Storage;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Grains.Instances;
using Realmforge.Server.Grains.Storage;

namespace Realmforge.Server.Grains;

public static class SiloSetup
{

    /// <summary>
    /// Registers content, rules services, grain storage and the instance allocator. Without a Postgres
    /// connection string grain state lives in memory, which is what local runs and tests want.
    /// </summary>
    public static ISiloBuilder AddRealmforgeGrains(this ISiloBuilder silo, IConfiguration configuration, ContentCatalog catalog)
    {
        silo.Services.AddSingleton(catalog);
        silo.Services.AddSingleton(new CharacterOperations(catalog));
        silo.Services.AddSingleton(TimeProvider.System);
        silo.Services.AddSerializer(serializer => serializer.AddRealmforgeSerializers());

        string? postgres = configuration.GetConnectionString(ConnectionNames.Postgres);
        if (string.IsNullOrWhiteSpace(postgres))
        {
            silo.AddMemoryGrainStorage(GrainStorageNames.Main);
        }
        else
        {
            silo.Services.AddSingleton(NpgsqlDataSource.Create(postgres));
            silo.Services.AddKeyedSingleton<IGrainStorage>(GrainStorageNames.Main, (services, _) =>
                new PostgresGrainStorage(services.GetRequiredService<NpgsqlDataSource>(), RealmforgeSerialization.JsonOptions));
        }

        var agones = configuration.GetSection(AgonesOptions.SectionName).Get<AgonesOptions>() ?? new AgonesOptions();
        if (string.IsNullOrWhiteSpace(agones.AllocatorUrl))
        {
            silo.Services.AddSingleton<IInstanceAllocator, LocalInstanceAllocator>();
        }
        else
        {
            silo.Services.AddSingleton(agones);
            silo.Services.AddHttpClient<IInstanceAllocator, AgonesInstanceAllocator>();
        }
        return silo;
    }
}
