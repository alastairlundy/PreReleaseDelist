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

using PreReleaseDelistLib.Internal;

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
/// <para>
/// Per-version delete requests are dispatched through the injected <see cref="IPackageVersionDeleter"/>
/// backend (<see cref="SdkPackageVersionDeleter"/>); this composing service owns only the per-run state:
/// enumerating the request, bucketing it via <see cref="DelistPlanning.Partition"/>, merging bucket and
/// backend outcomes into one outcome per requested version, and the fail-fast rate-limit rule.
/// </para>
/// </remarks>
public class NetSdkPackageDelistService : IPackageDelistService
{
    private readonly IPackageVersionService _packageVersionService;
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;
    private readonly IPackageVersionDeleter _versionDeleter;

    /// <summary>
    /// Creates a service that dispatches delete requests through <paramref name="versionDeleter"/>.
    /// </summary>
    /// <param name="packageVersionService">Service used to enumerate and check package versions.</param>
    /// <param name="packageAvailabilityDetector">Detector used to verify the package exists on the server.</param>
    /// <param name="versionDeleter">The per-version delete backend to dispatch to-delist versions through.</param>
    public NetSdkPackageDelistService(IPackageVersionService packageVersionService,
        IPackageAvailabilityDetector packageAvailabilityDetector,
        IPackageVersionDeleter versionDeleter)
    {
        ArgumentNullException.ThrowIfNull(packageVersionService);
        ArgumentNullException.ThrowIfNull(packageAvailabilityDetector);
        ArgumentNullException.ThrowIfNull(versionDeleter);

        _packageVersionService = packageVersionService;
        _packageAvailabilityDetector = packageAvailabilityDetector;
        _versionDeleter = versionDeleter;
    }

    /// <summary>
    /// Asynchronously delists all prerelease versions of a NuGet package to be delisted based on the provided API credentials and package ID.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint.</param>
    /// <param name="nugetApiKey">The API key for authentication with the NuGet service.</param>
    /// <param name="packageId">The identifier of the package(s) to be delisted.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are also delisted alongside prerelease versions.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the operation.</param>
    /// <returns>A sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageDelistingAsync(
        string nugetApiUrl, string nugetApiKey,
        string packageId, bool includeZeroMajorVersions = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        NuGetVersion[] versionsToDelist = await _packageVersionService.GetPrereleasePackageVersionsAsync
            (nugetApiUrl, nugetApiKey, packageId, includeZeroMajorVersions, cancellationToken);

        IAsyncEnumerable<PackageVersionOutcome> delistResults =
            RequestPackageDelistingAsync(nugetApiUrl, nugetApiKey, packageId, versionsToDelist, cancellationToken);

        await foreach (PackageVersionOutcome outcome in delistResults)
        {
            yield return outcome;
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
    /// <returns>A sequence containing one <see cref="PackageVersionOutcome"/> per requested version.</returns>
    /// <remarks>
    /// <para>
    /// Already-delisted and not-on-server versions are reported from the plan's buckets; only
    /// <c>plan.ToDelist</c> versions are dispatched to the injected deleter, so every requested version
    /// yields exactly one outcome.
    /// </para>
    /// <para>
    /// Fail-fast: when a dispatch reports <see cref="PackageVersionStatus.RateLimited"/>, no further
    /// dispatches are made and every remaining to-delist version is reported as
    /// <see cref="PackageVersionStatus.NotAttempted"/> rather than <see cref="PackageVersionStatus.Failed"/>.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<PackageVersionOutcome> RequestPackageDelistingAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, IList<NuGetVersion> versions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentNullException.ThrowIfNull(versions);
        
        bool doesPackageExists = await _packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken);

        if(!doesPackageExists)
            throw new ArgumentException(string.Format(Resources.Exceptions_Package_NotFoundOnServer, packageId, nugetApiUrl), nameof(packageId));
        
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo =
            await _packageVersionService.CheckPackageVersionsListedAsync(nugetApiUrl, nugetApiKey, packageId,
                true, versions, cancellationToken);

        DelistPlan plan = DelistPlanning.Partition(versions, listingInfo);

        foreach (NuGetVersion version in plan.AlreadyDelisted)
        {
            yield return new PackageVersionOutcome(packageId, version, PackageVersionStatus.AlreadyDelisted);
        }

        foreach (NuGetVersion version in plan.NotOnServer)
        {
            yield return new PackageVersionOutcome(packageId, version, PackageVersionStatus.NotOnServer);
        }

        if (plan.ToDelist.Count == 0)
            yield break;

        bool rateLimitStop = false;

        foreach (NuGetVersion version in plan.ToDelist)
        {
            if (rateLimitStop)
            {
                yield return new PackageVersionOutcome(packageId, version, PackageVersionStatus.NotAttempted);
                continue;
            }

            PackageVersionOutcome outcome = await _versionDeleter.DeleteAsync(nugetApiUrl, packageId, version,
                nugetApiKey, cancellationToken);

            yield return outcome;

            if (outcome.Status == PackageVersionStatus.RateLimited)
                rateLimitStop = true;
        }
    }
}
