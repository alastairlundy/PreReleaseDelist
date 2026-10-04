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

namespace PreReleaseDelistLib.Internal;

/// <summary>
/// The requested versions of a package, bucketed by the listing state they were found in on the server.
/// </summary>
/// <param name="ToDelist">Versions that exist, are listed, and therefore need a delete request.</param>
/// <param name="AlreadyDelisted">Versions that exist on the server but are already unlisted.</param>
/// <param name="NotOnServer">Versions the server does not know about at all.</param>
internal sealed record DelistPlan(
    IReadOnlyList<NuGetVersion> ToDelist,
    IReadOnlyList<NuGetVersion> AlreadyDelisted,
    IReadOnlyList<NuGetVersion> NotOnServer);

/// <summary>
/// Turns requested package versions plus the server's listing metadata into a <see cref="DelistPlan"/>.
/// </summary>
internal static class DelistPlanning
{
    /// <summary>
    /// Partitions the requested versions by their current listing state.
    /// </summary>
    /// <remarks>
    /// A version that the server does not report is <em>not</em> treated as already delisted. The two states are
    /// reported separately so that a mistyped or never-published version surfaces as a failure instead of a
    /// misleading success.
    /// </remarks>
    /// <param name="requestedVersions">The versions the caller asked to delist. May contain duplicates.</param>
    /// <param name="listingInfoByVersion">Listing metadata keyed by version, as returned by
    /// <see cref="Abstractions.IPackageVersionService.CheckPackageVersionsListedAsync"/>.</param>
    internal static DelistPlan Partition(IEnumerable<NuGetVersion> requestedVersions,
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfoByVersion)
    {
        ArgumentNullException.ThrowIfNull(requestedVersions);
        ArgumentNullException.ThrowIfNull(listingInfoByVersion);

        List<NuGetVersion> toDelist = [];
        List<NuGetVersion> alreadyDelisted = [];
        List<NuGetVersion> notOnServer = [];

        foreach (NuGetVersion version in requestedVersions.Distinct())
        {
            if (!listingInfoByVersion.TryGetValue(version, out PackageVersionListingInfo? listingInfo)
                || !listingInfo.PackageVersionExists)
            {
                notOnServer.Add(version);
            }
            else if (!listingInfo.IsListed)
            {
                alreadyDelisted.Add(version);
            }
            else
            {
                toDelist.Add(version);
            }
        }

        return new DelistPlan(toDelist, alreadyDelisted, notOnServer);
    }
}