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

using NuGet.Packaging.Core;

namespace PreReleaseDelistLib;

/// <summary>
/// This class provides functionality to retrieve package versions from a NuGet repository.
/// </summary>
public class PackageVersionService : IPackageVersionService
{
    private readonly IPackageAvailabilityDetector _packageAvailabilityDetector;

    public PackageVersionService(IPackageAvailabilityDetector packageAvailabilityDetector)
    {
        _packageAvailabilityDetector = packageAvailabilityDetector;
    }

    /// <summary>
    /// Enumerates prerelease package versions from a NuGet repository.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are included alongside prerelease versions.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of prerelease <see cref="NuGetVersion"/> objects matching the specified criteria.</returns>
    public async IAsyncEnumerable<NuGetVersion> EnumeratePrereleasePackageVersionsAsync(string nugetApiUrl,
        string nugetApiKey, string packageId,
        bool includeZeroMajorVersions = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);

        SourceRepository repoInfo = GetRepoInfo(nugetApiUrl);

        using var cacheContext = new SourceCacheContext();

        FindPackageByIdResource? resource = await repoInfo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

        if (resource is null)
            throw new InvalidOperationException($"Package metadata resource is not available for this source: {nugetApiUrl}");

        IEnumerable<NuGetVersion>? allPackageVersions =
            await resource.GetAllVersionsAsync(packageId, cacheContext, NullLogger.Instance,
                cancellationToken);

        if (allPackageVersions is null)
        {
            yield break;
        }

        foreach (NuGetVersion version in allPackageVersions.Where(v => v.IsPrerelease || (includeZeroMajorVersions && v.Major == 0)))
        {
            yield return version;
        }
    }

    /// <summary>
    /// Retrieves an array of prerelease package versions from a NuGet repository.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are included alongside prerelease versions.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An array of prerelease NuGet version strings matching the specified criteria.</returns>
    public async Task<NuGetVersion[]> GetPrereleasePackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId,
        bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);

        SourceRepository repoInfo = GetRepoInfo(nugetApiUrl);

        using var cacheContext = new SourceCacheContext();

        FindPackageByIdResource? resource = await repoInfo.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

        if (resource is null)
            throw new InvalidOperationException($"Package metadata resource is not available for this source: {nugetApiUrl}");

        IEnumerable<NuGetVersion>? allPackageVersions =
            await resource.GetAllVersionsAsync(packageId, cacheContext, NullLogger.Instance,
                cancellationToken);

        if (allPackageVersions is null)
            return [];
        
        return allPackageVersions.Where(v => v.IsPrerelease || (includeZeroMajorVersions && v.Major == 0))
            .ToArray();
    }

    /// <summary>
    /// Enumerates all package versions from a NuGet repository, including listed and unlisted versions.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of <see cref="PackageVersionListingInfo"/> objects with version and listing status.</returns>
    public async IAsyncEnumerable<PackageVersionListingInfo> EnumerateAllPackageVersionsAsync(string nugetApiUrl,
        string nugetApiKey, string packageId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);

        bool packageExists = await _packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken);

        if (!packageExists)
            throw new ArgumentException($"Package with Id of '{packageId}' does not exist.");

        List<(NuGetVersion Version, bool IsListed)> metadata = await FetchPackageMetadataAsync(nugetApiUrl, nugetApiKey, packageId, cancellationToken);

        foreach ((NuGetVersion version, bool isListed) in metadata)
        {
            yield return new PackageVersionListingInfo
            {
                IsListed = isListed,
                PackageVersion = version,
                PackageVersionExists = true
            };
        }
    }

    /// <summary>
    /// Retrieves all available versions of a NuGet package from a specified repository. Unlisted versions are included.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An array of NuGet version strings matching the specified criteria.</returns>
    public async Task<NuGetVersion[]> GetAllPackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        ArgumentException.ThrowIfNullOrEmpty(packageId);

        bool packageExists = await _packageAvailabilityDetector.CheckPackageExistsAsync(nugetApiUrl, packageId, cancellationToken);

        if (!packageExists)
            throw new ArgumentException($"Package with Id of '{packageId}' does not exist.");

        List<(NuGetVersion Version, bool IsListed)> metadata = await FetchPackageMetadataAsync(nugetApiUrl, nugetApiKey, packageId, cancellationToken);

        return metadata.Select(m => m.Version).ToArray();
    }

    /// <summary>
    /// Determines whether a specific package version has been delisted from the NuGet repository.
    /// </summary>
    /// <remarks>This method queries the package metadata directly. If the version is not found, it returns false.</remarks>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to check for delisting status.</param>
    /// <param name="includePreReleaseVersions">Whether to include pre-release versions in the search results.</param>
    /// <param name="packageVersion">The specific version of the package to verify against.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A boolean value indicating whether the specified package version is delisted. Returns true if delisted, false otherwise.</returns>
    public async Task<bool> IsPackageVersionDelistedAsync(string nugetApiUrl, string nugetApiKey, string packageId,
        bool includePreReleaseVersions, NuGetVersion packageVersion, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);
        
        SourceRepository repoInfo = GetRepoInfo(nugetApiUrl);

        using var cacheContext = new SourceCacheContext();

        PackageMetadataResource? metadataResource =
            await repoInfo.GetResourceAsync<PackageMetadataResource>(cancellationToken);

        if (metadataResource is null)
            throw new InvalidOperationException($"Package metadata resource is not available for this source: {nugetApiUrl}");

        PackageIdentity identity = new PackageIdentity(packageId, packageVersion);

        IPackageSearchMetadata? result = await metadataResource.GetMetadataAsync(identity, cacheContext, NullLogger.Instance, cancellationToken);

        if (result is null)
            return false;

        return !result.IsListed;
    }

    /// <summary>
    /// Checks whether each of the specified package versions is listed in the repository.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to check.</param>
    /// <param name="includePreReleaseVersions">Whether to include pre-release versions in the search results.</param>
    /// <param name="packageVersions">The specific versions of the package to verify against.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A dictionary mapping each requested version to whether it is currently listed.</returns>
    public async Task<IDictionary<NuGetVersion, bool>> CheckPackageVersionsListedAsync(string nugetApiUrl,
        string nugetApiKey, string packageId,
        bool includePreReleaseVersions,
        IList<NuGetVersion> packageVersions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiUrl);
        ArgumentException.ThrowIfNullOrEmpty(nugetApiKey);

        List<(NuGetVersion Version, bool IsListed)> metadata = await FetchPackageMetadataAsync(nugetApiUrl, nugetApiKey, packageId, cancellationToken);

        Dictionary<NuGetVersion, bool> metadataLookup = metadata.ToDictionary(m => m.Version, m => m.IsListed);

        NuGetVersion[] deduplicatedVersions = packageVersions.Distinct().ToArray();

        Dictionary<NuGetVersion, bool> output = new Dictionary<NuGetVersion, bool>(capacity: deduplicatedVersions.Length);

        foreach (NuGetVersion version in deduplicatedVersions)
        {
            output[version] = metadataLookup.TryGetValue(version, out bool isListed) && isListed;
        }

        return output;
    }

    private SourceRepository GetRepoInfo(string nugetApiUrl) => Repository.Factory.GetCoreV3(nugetApiUrl);

    private async Task<List<(NuGetVersion Version, bool IsListed)>> FetchPackageMetadataAsync(string nugetApiUrl,
        string nugetApiKey, string packageId, CancellationToken cancellationToken)
    {
        SourceRepository repoInfo = GetRepoInfo(nugetApiUrl);

        using var cacheContext = new SourceCacheContext();

        PackageMetadataResource? metadataResource =
            await repoInfo.GetResourceAsync<PackageMetadataResource>(cancellationToken);

        if (metadataResource is null)
            throw new InvalidOperationException($"Package metadata resource is not available for this source: {nugetApiUrl}");

        IEnumerable<IPackageSearchMetadata> results =
            await metadataResource.GetMetadataAsync(packageId, includePrerelease: true, includeUnlisted: true,
                cacheContext, NullLogger.Instance, cancellationToken);

        List<(NuGetVersion Version, bool IsListed)> metadata = results
            .Select(r => (r.Identity.Version, r.IsListed))
            .ToList();

        return metadata;
    }
}
