using Microsoft.Extensions.Hosting;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Grains;
using Realmforge.Server.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ContentCatalog catalog = ContentSource.Load(builder.Configuration["Content:Path"]);

builder.UseOrleans(silo => silo
    .UseRealmforgeCluster(builder.Configuration)
    .AddRealmforgeGrains(builder.Configuration, catalog));

await builder.Build().RunAsync();
