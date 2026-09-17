/*
    PreReleaseDelistLib
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

using CliInvoke.Core;
using EnhancedLinq.Deferred;

namespace PreReleaseDelistLib;

/// <summary>
/// This class is an alternative implementation for the <see cref="PackageDelistService"/> that uses .NET SDK CLI's nuget subcommand.
/// </summary>
/// <remarks>
/// <para>
/// This backend passes the API key on the process command line, which is visible in process listings.
/// This is inherent to the <c>dotnet nuget delete</c> command and cannot be avoided.
/// Consider using the HTTP-based <see cref="PackageDelistService"/> where possible for improved security.
/// </para>
/// </remarks>
public class NetSdkPackageDelistService : IPackageDelistService
{
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;
    private readonly IProcessInvoker _processInvoker;

    public NetSdkPackageDelistService(IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        IProcessInvoker processInvoker)
    {
        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
        _processInvoker = processInvoker;
    }

    /// <summary>
    /// Asynchronously delists all prerelease versions of a NuGet package to be delisted based on the provided API credentials and package ID.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint.</param>
    /// <param name="nugetApiKey">The API key for authentication with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package(s) to be delisted.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are also delisted alongside prerelease versions.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A sequence of tuples containing the version, whether the delisting was successful, and any response message from the API.</returns>
    public async IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)>
        RequestPackageDelistingAsync(string nugetApiUrl, string nugetApiKey,
            string packageId, bool includeZeroMajorVersions = false,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NuGetVersion[] versionsToDelist = await _packageVersionService.GetPrereleasePackageVersionsAsync
            (nugetApiUrl, nugetApiKey, packageId, includeZeroMajorVersions, cancellationToken);
        
        IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)> delistResults =
            RequestPackageDelistingAsync(nugetApiUrl, nugetApiKey, packageId, versionsToDelist, cancellationToken);

        await foreach ((NuGetVersion version, bool delistSuccess, string responseMessage) result in delistResults)
        {
            yield return (result.version, result.delistSuccess, result.responseMessage);
        }
    }

    /// <summary>
    /// Asynchronously delists a list of versions of a NuGet package to be delisted based on the provided API credentials and package ID.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint.</param>
    /// <param name="nugetApiKey">The API key for authentication with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package(s) to be delisted.</param>
    /// <param name="versions">The package versions to delist.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A sequence of tuples containing the version, whether the delisting was successful, and any response message from the API.</returns>
    public async IAsyncEnumerable<(NuGetVersion version, bool delistSuccess, string responseMessage)>
        RequestPackageDelistingAsync(string nugetApiUrl, string nugetApiKey, string packageId, IList<NuGetVersion> versions,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(versions);
        
        bool doesPackageExists = await _packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken);

        if(!doesPackageExists)
            throw new ArgumentException(string.Format(Resources.Exceptions_Package_NotFoundOnServer, packageId, nugetApiUrl), nameof(packageId));
        
        IDictionary<NuGetVersion, bool> checkVersionsForDelist = await _packageVersionService.CheckPackageVersionsListedAsync(nugetApiUrl, nugetApiKey, packageId,
            true, versions, cancellationToken);

        NuGetVersion[] alreadyDelistedVersions =
            [.. checkVersionsForDelist.Where(kvp => !kvp.Value).Select(kvp => kvp.Key)];
        
        NuGetVersion[] versionsToDelist = [.. versions.Exclude(alreadyDelistedVersions)];
        
        foreach (NuGetVersion version in alreadyDelistedVersions)
        {
            yield return new ValueTuple<NuGetVersion, bool, string>(version, true, Resources.Info_Package_AlreadyDelisted);
        }
        
        foreach (NuGetVersion version in versionsToDelist)
        {
            ProcessConfiguration configuration = new()
            {
                TargetFilePath = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet",
                OutputRedirection = true,
                ArgumentList =
                [
                    "nuget", "delete",
                    packageId.ToLowerInvariant(),
                    version.ToNormalizedString(),
                    "--api-key", nugetApiKey,
                    "--source", nugetApiUrl,
                    "--non-interactive"
                ]
            };
            
            BufferedProcessResult result = await _processInvoker.ExecuteBufferedAsync(configuration,
                new ProcessExitConfiguration(),
                cancellationToken);

            if (result.ExitCode == 0)
            {
                yield return new ValueTuple<NuGetVersion, bool, string>(version, true, "");
            }
            else
            {
                string failureMessage = string.IsNullOrEmpty(result.StandardError)
                    ? result.StandardOutput
                    : $"{result.StandardOutput} {result.StandardError}".Trim();
                yield return new ValueTuple<NuGetVersion, bool, string>(version, false, failureMessage);
            }
        }
    }
}
