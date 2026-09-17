/*
    prerelease-delist - Delist pre-release library versions from a Nuget Server
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
        .AddKeyedSingleton<IPackageDelistService, PackageDelistService>("http")
        .AddKeyedSingleton<IPackageDelistService, NetSdkPackageDelistService>("sdk")
        .AddCliInvoke(ServiceLifetime.Singleton);

    ConfigurationBuilder configurationBuilder = new();
    foreach (string basePath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        configurationBuilder.AddJsonFile(Path.Combine(basePath, "appsettings.json"), optional: true);
    IConfiguration configuration = configurationBuilder.Build();

    services.AddSingleton(configuration);
});

return await Cli.RunAsync<DelistCommand>(args, new CliSettings
{
    EnableDefaultExceptionHandler = true,
    EnableSuggestDirective = true,
    EnableEnvironmentVariablesDirective = true
});
