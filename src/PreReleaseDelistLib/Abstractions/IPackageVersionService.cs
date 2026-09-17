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

namespace PreReleaseDelistLib.Abstractions;

/// <summary>
/// Provides methods for retrieving and managing NuGet package versions.
/// </summary>
public interface IPackageVersionService
{
    /// <summary>
    /// Enumerates prerelease package versions from a NuGet repository.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are included alongside prerelease versions.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of prerelease <see cref="NuGetVersion"/> objects matching the specified criteria.</returns>
    IAsyncEnumerable<NuGetVersion> EnumeratePrereleasePackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId,
        bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an array of prerelease package versions from a NuGet repository.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="includeZeroMajorVersions">When true, stable versions with Major == 0 are included alongside prerelease versions.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An array of prerelease <see cref="NuGetVersion"/> objects matching the specified criteria.</returns>
    Task<NuGetVersion[]> GetPrereleasePackageVersionsAsync(string nugetApiUrl, string nugetApiKey, string packageId,
        bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all package versions from a NuGet repository, including listed and unlisted versions.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An asynchronous sequence of <see cref="PackageVersionListingInfo"/> objects with version and listing status.</returns>
    IAsyncEnumerable<PackageVersionListingInfo> EnumerateAllPackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all available versions of a NuGet package from the specified repository. Unlisted versions are included.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API.</param>
    /// <param name="nugetApiKey">The API key for authentication against the NuGet API.</param>
    /// <param name="packageId">The identifier of the package to retrieve versions for.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An array of <see cref="NuGetVersion"/> objects matching the specified criteria.</returns>
    Task<NuGetVersion[]> GetAllPackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, CancellationToken cancellationToken);
    
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
    Task<bool> IsPackageVersionDelistedAsync(string nugetApiUrl, string nugetApiKey, string packageId, bool includePreReleaseVersions,
        NuGetVersion packageVersion, CancellationToken cancellationToken);

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
    Task<IDictionary<NuGetVersion, bool>> CheckPackageVersionsListedAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, bool includePreReleaseVersions, IList<NuGetVersion> packageVersions,
        CancellationToken cancellationToken);
}
