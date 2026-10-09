/*
    PreReleaseDelistLib.Tests
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Lesser General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU Lesser General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using CliInvoke.Extensions;
using DotMake.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PreReleaseDelistCli;
using PreReleaseDelistLib;
using PreReleaseDelistLib.Abstractions;
using PreReleaseDelistLib.Detectors;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Drives the real CLI end-to-end, in-process: DotMake parses and binds the argument vector,
/// the composition root builds the production container, and the exit code comes back from
/// <see cref="DelistCommand.RunAsync"/> with stdout and stderr captured (T005, T006, T008).
/// </summary>
/// <remarks>
/// <para>
/// The service wiring mirrors <c>src/PreReleaseDelistCli/Program.cs</c>: that file is a top-level
/// program whose <c>Main</c> is private, so the tests cannot call it and re-declare the same
/// registrations here instead. Everything above the container — argument parsing, option binding,
/// validation, output formats, exit-code mapping — is the real production path, not a stand-in.
/// </para>
/// <para>
/// The command reads <c>NUGET_API_KEY</c> and <c>NUGET_SERVER_URL</c> from the environment at
/// construction, so every run passes <c>--server-url</c> (and <c>--api-key</c> on real runs)
/// explicitly: the option value always wins the precedence chain.
/// </para>
/// </remarks>
internal static class CliIntegrationHarness
{
    /// <summary>The API key every integration run sends; asserted absent from both streams.</summary>
    internal const string TestApiKey = "integration-test-api-key-7d1f0c";

    private static readonly CliSettings Settings = new()
    {
        EnableDefaultExceptionHandler = true,
        EnableSuggestDirective = true,
        EnableEnvironmentVariablesDirective = true
    };

    private static readonly object ConfigureGate = new();
    private static bool _configured;

    /// <summary>
    /// Runs the CLI over <paramref name="args"/> and returns its exit code plus the captured streams.
    /// </summary>
    internal static async Task<CommandRun> RunAsync(params string[] args)
    {
        EnsureConfigured();

        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        try
        {
            // TUnit0055 warns that overwriting the console writer can break TUnit logging. The
            // capture is deliberate and safe here: every test class that runs the CLI is marked
            // [NotInParallel], so no two captured runs can interleave, and the finally block always
            // restores the original writers.
#pragma warning disable TUnit0055
            Console.SetOut(stdout);
            Console.SetError(stderr);
#pragma warning restore TUnit0055

            int exitCode = await Cli.RunAsync<DelistCommand>(args, Settings);

            return new CommandRun(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
            Console.SetError(originalError);
#pragma warning restore TUnit0055
        }
    }

    private static void EnsureConfigured()
    {
        lock (ConfigureGate)
        {
            if (_configured)
            {
                return;
            }

            Cli.Ext.ConfigureServices(ConfigureProductionServices);
            _configured = true;
        }
    }

    /// <summary>
    /// The registration set from <c>Program.cs</c>: HTTP and SDK delete backends keyed to the exact
    /// vocabulary the <c>--backend</c> option accepts, one composing service per key, and the
    /// environment-prefixed configuration the options precedence chain reads.
    /// </summary>
    private static void ConfigureProductionServices(IServiceCollection services)
    {
        services.AddHttpClient()
            .AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>()
            .AddSingleton<IPackageVersionService, PackageVersionService>()
            .AddKeyedSingleton<IPackageVersionDeleter, HttpPackageVersionDeleter>("http")
            .AddKeyedSingleton<IPackageVersionDeleter, SdkPackageVersionDeleter>("sdk")
            .AddKeyedSingleton<IPackageDelistService>("http", static (serviceProvider, _) =>
                new PackageDelistService(
                    serviceProvider.GetRequiredService<IPackageVersionService>(),
                    serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                    serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("http"),
                    isRateLimitedDecorated: true))
            .AddKeyedSingleton<IPackageDelistService>("sdk", static (serviceProvider, _) =>
                new NetSdkPackageDelistService(
                    serviceProvider.GetRequiredService<IPackageVersionService>(),
                    serviceProvider.GetRequiredService<IPackageAvailabilityDetector>(),
                    serviceProvider.GetRequiredKeyedService<IPackageVersionDeleter>("sdk"),
                    isRateLimitedDecorated: true))
            .AddCliInvoke(ServiceLifetime.Singleton);

        ConfigurationBuilder configurationBuilder = new();
        configurationBuilder.AddEnvironmentVariables(prefix: "PRERELEASEDELIST_");
        IConfiguration configuration = configurationBuilder.Build();

        services.AddSingleton(configuration);
    }
}
