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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

using NuGet.Versioning;
using PreReleaseDelistCli.Helpers;

namespace PreReleaseDelistCli;

[CliCommand(Name = "")]
public class DelistCommand
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public DelistCommand(IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }
    
    [CliOption(Name = "--package-id", Required = true,
        Arity = CliArgumentArity.ExactlyOne)]
    public string PackageId { get; set; }

    [CliOption(Name = "--delist-all-versions")]
    public bool DelistAllVersions { get; set; } = false;

    [CliOption(Name = "--use-strict-parsing")]
    public bool UseStrictParsing { get; set; } = true;
    
    [CliArgument(Name = "versions")]
    public string[] Versions { get; set; }
    
    [CliOption(Name = "--api-key", Required = true)]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [CliOption(Name = "--non-interactive", Required = false)]
    [DefaultValue(false)]
    public bool NonInteractive { get; set; } = false;
    
    [CliOption(Name = "--server-url", Required = false)]
    public string? ServerUrl { get; set; }

    /// <summary>
    /// The backend to use for delisting: "http" (default) or "sdk".
    /// </summary>
    [CliOption(Name = "--backend", Required = false)]
    public string Backend { get; set; } = "http";

    /// <summary>
    /// Includes stable Major==0 versions when using --delist-all-versions.
    /// </summary>
    [CliOption(Name = "--include-zero-major", Required = false)]
    public bool IncludeZeroMajor { get; set; } = false;
    
    public async Task<int> RunAsync()
    {
        if (!string.Equals(Backend, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Backend, "sdk", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync("Invalid backend value. Valid values are: http, sdk");
            return -1;
        }

        IPackageDelistService delistService = string.Equals(Backend, "sdk", StringComparison.OrdinalIgnoreCase)
            ? _serviceProvider.GetRequiredKeyedService<IPackageDelistService>("sdk")
            : _serviceProvider.GetRequiredKeyedService<IPackageDelistService>("http");

        string serverUrl = !string.IsNullOrWhiteSpace(ServerUrl)
            ? ServerUrl
            : (!string.IsNullOrWhiteSpace(_configuration["NuGetServerUrl"])
                ? _configuration["NuGetServerUrl"]!
                : "https://api.nuget.org/v3/index.json");

        if (!DelistAllVersions)
        {
            Versions = Versions.Where(s => !string.IsNullOrWhiteSpace(s) && char.IsDigit(s.Trim()[0])).ToArray();

            if (Versions.Length == 0)
            {
                await Console.Error.WriteLineAsync(Resources.Errors_Input_NoVersionStrings);
                return -1;
            }
        }

        ArgumentException.ThrowIfNullOrEmpty(PackageId);
        
        string? nugetApiKey = !string.IsNullOrEmpty(ApiKey) ? ApiKey : _configuration["NuGetApiKey"];

        if (string.IsNullOrEmpty(nugetApiKey))
        {
            Console.WriteLine(Resources.Exceptions_Configuration_NugetApiKey);
            return -1;
        }

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)> results;
        
        if (DelistAllVersions)
        {
            results = delistService.RequestPackageDelistingAsync(serverUrl, nugetApiKey,
                PackageId, IncludeZeroMajor, cts.Token);
        }
        else
        {
            IList<NuGetVersion> parsedVersions = ParseVersions(Versions, UseStrictParsing);

            results = delistService.RequestPackageDelistingAsync(serverUrl, nugetApiKey,
                PackageId, parsedVersions, cts.Token);
        }

        if (NonInteractive)
        {
            int failureCount = 0;

            await foreach ((NuGetVersion version, bool delistSuccess, string responseMessage) result in results)
            {
                string statusText = result.delistSuccess ? "Success" : "Failure";

                string resultText = $"Version={result.version.ToNormalizedString()} Status={statusText}";

                if (result.delistSuccess)
                {
                    if (!string.IsNullOrEmpty(result.responseMessage))
                    {
                        resultText += $" Info='{result.responseMessage}'";
                    }
                }
                else
                {
                    resultText += $" Error='{result.responseMessage}'";
                    failureCount++;
                }

                await Console.Out.WriteLineAsync(resultText);
            }

            return failureCount > 0 ? 1 : 0;
        }

        ConcurrentBag<(NuGetVersion version, string responseMessage)> delistedVersions = new();
        ConcurrentBag<(NuGetVersion version, bool isDelisted, string responseMessage)> nonDelistedVersions = new();

        await foreach ((NuGetVersion version, bool delistSuccess, string responseMessage) result in results)
        {
            if (result.delistSuccess)
            {
                delistedVersions.Add((result.version, result.responseMessage));
            }
            else
            {
                nonDelistedVersions.Add(result);
            }
        }

        if (delistedVersions.Count > 0)
            await ResultHelper.PrintDelistedVersions(delistedVersions.ToArray(), PackageId);
        
        if (nonDelistedVersions.Count > 0)
            await ResultHelper.PrintNonDelistedVersions(nonDelistedVersions.ToArray(), PackageId);
        
        return nonDelistedVersions.Count > 0 ? 1 : 0;
    }
    
    private static IList<NuGetVersion> ParseVersions(string[] versions, bool throwOnError)
    {
        List<NuGetVersion> output = new(capacity: versions.Length);

        foreach (string versionString in versions)
        {
            bool success = NuGetVersion.TryParse(versionString, out NuGetVersion? version);

            if (success && version is not null)
            {
                output.Add(version);
            }
            else
            {
                if(throwOnError)
                    throw new ArgumentException("Invalid version string: " + versionString);
            }
        }

        return output.Distinct().ToList();
    }
}
