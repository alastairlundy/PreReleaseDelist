/*
    prerelease-delist - Delist pre-release package versions from a Nuget Server
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using CliInvoke.Extensions;
using Microsoft.Extensions.DependencyInjection;

Cli.Ext.ConfigureServices(services =>
{
    services.AddHttpClient()
        .AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>()
        .AddSingleton<IPackageVersionService, PackageVersionService>()
        // Per-version delete backends keyed to the exact vocabulary the --backend option accepts.
        .AddKeyedSingleton<IPackageVersionDeleter, HttpPackageVersionDeleter>("http")
        .AddKeyedSingleton<IPackageVersionDeleter, SdkPackageVersionDeleter>("sdk")
        // Each composing service receives its keyed backend; both in-box backends detect 429
        // themselves, so the fail-fast state machine lives inside the services.
        .AddKeyedSingleton<IPackageDelistService>("http", static (serviceProvider, _) =>
            new PackageDelistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("http")))
        .AddKeyedSingleton<IPackageDelistService>("sdk", static (serviceProvider, _) =>
            new NetSdkPackageDelistService(
                serviceProvider.GetRequiredService<IPackageVersionService>(),
                serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("sdk")))
        .AddCliInvoke(ServiceLifetime.Singleton);

    ConfigurationBuilder configurationBuilder = new();
    configurationBuilder.AddEnvironmentVariables(prefix: "PRERELEASEDELIST_");
    IConfiguration configuration = configurationBuilder.Build();

    services.AddSingleton(configuration);
});

return await Cli.RunAsync<DelistCommand>(args, new CliSettings
{
    EnableDefaultExceptionHandler = true,
    EnableSuggestDirective = true,
    EnableEnvironmentVariablesDirective = true
});
