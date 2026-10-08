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
/// The per-version delete seam shared by every delete backend and composing service.
/// </summary>
/// <remarks>
/// One version goes in, one <see cref="PackageVersionOutcome"/> comes out. The server URL is a
/// per-call parameter, so a single implementation may serve multiple sources; per-call
/// service-index resolution stays inside implementing backends, and per-run state belongs to the
/// composing service rather than to this contract.
/// </remarks>
public interface IPackageVersionDeleter
{
    /// <summary>
    /// Deletes (delists) a single package version from the specified NuGet server.
    /// </summary>
    /// <param name="nugetApiUrl">The URL of the NuGet API endpoint serving the package.</param>
    /// <param name="packageId">The identifier of the package the version belongs to.</param>
    /// <param name="version">The specific version of the package to delete.</param>
    /// <param name="apiKey">The API key required for authentication with the NuGet service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The closed outcome of the delete attempt for this version.</returns>
    ValueTask<PackageVersionOutcome> DeleteAsync(
        string nugetApiUrl, string packageId, NuGetVersion version,
        string apiKey, CancellationToken cancellationToken = default);
}
