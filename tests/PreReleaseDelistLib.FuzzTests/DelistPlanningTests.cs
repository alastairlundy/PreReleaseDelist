/*
    PreReleaseDelistLib.FuzzTests
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

using NuGet.Versioning;
using PreReleaseDelistLib.Internal;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.FuzzTests;

/// <summary>
/// Covers the partitioning that decides, per version, whether to send a delete, report the version as
/// already delisted, or report that the server has never heard of it.
/// </summary>
/// <remarks>
/// The third bucket is the important one. Treating "not on the server" as "already delisted" makes a
/// mistyped or never-published version report success, which silently defeats the point of running the
/// tool in CI.
/// </remarks>
public class DelistPlanningTests
{
    private static PackageVersionListingInfo Exists(bool isListed) => new()
    {
        PackageVersion = null!, IsListed = isListed, PackageVersionExists = true
    };

    private static PackageVersionListingInfo Missing() => new()
    {
        PackageVersion = null!, IsListed = false, PackageVersionExists = false
    };

    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    [Test]
    [Arguments("1.0.0", true, true, 1, 0, 0)] // listed and present -> needs a delete
    [Arguments("1.0.0", false, true, 0, 1, 0)] // present but unlisted -> already delisted
    [Arguments("1.0.0", true, false, 0, 0, 1)] // not present -> not on server, whatever IsListed says
    [Arguments("1.0.0", false, false, 0, 0, 1)]
    [Arguments("2.5.0-rc.1", true, true, 1, 0, 0)]
    [Arguments("2.5.0-rc.1", false, false, 0, 0, 1)]
    public async Task Partition_RoutesVersionToExactlyOneBucket(string version, bool isListed,
        bool versionExists, int expectedToDelist, int expectedAlreadyDelisted, int expectedNotOnServer)
    {
        NuGetVersion parsed = V(version);

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [parsed] = versionExists ? Exists(isListed) : Missing()
        };

        DelistPlan plan = DelistPlanning.Partition([parsed], listingInfo);

        await Assert.That(plan.ToDelist.Count).IsEqualTo(expectedToDelist);
        await Assert.That(plan.AlreadyDelisted.Count).IsEqualTo(expectedAlreadyDelisted);
        await Assert.That(plan.NotOnServer.Count).IsEqualTo(expectedNotOnServer);
    }

    [Test]
    public async Task Partition_TreatsVersionMissingFromLookupAsNotOnServer()
    {
        NuGetVersion requested = V("99.99.99-nope");

        // The server never reported this version, so the lookup is empty.
        DelistPlan plan = DelistPlanning.Partition([requested], new Dictionary<NuGetVersion, PackageVersionListingInfo>());

        await Assert.That(plan.ToDelist).IsEmpty();
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer.Count).IsEqualTo(1);
        await Assert.That(plan.NotOnServer[0]).IsEqualTo(requested);
    }

    [Test]
    public async Task Partition_NeverReportsAVersionAsAlreadyDelistedWhenItDoesNotExist()
    {
        NuGetVersion ghost = V("1.0.0-alpha");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [ghost] = Missing()
        };

        DelistPlan plan = DelistPlanning.Partition([ghost], listingInfo);

        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Partition_KeepsEachRequestedVersionInOneBucketOnly()
    {
        NuGetVersion[] requested = [V("1.0.0"), V("1.0.1"), V("1.0.2"), V("1.0.3")];

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [requested[0]] = Exists(isListed: true),
            [requested[1]] = Exists(isListed: false),
            [requested[2]] = Missing()
            // requested[3] is deliberately absent from the lookup.
        };

        DelistPlan plan = DelistPlanning.Partition(requested, listingInfo);

        int total = plan.ToDelist.Count + plan.AlreadyDelisted.Count + plan.NotOnServer.Count;

        await Assert.That(total).IsEqualTo(requested.Length);
        await Assert.That(plan.ToDelist[0]).IsEqualTo(requested[0]);
        await Assert.That(plan.AlreadyDelisted[0]).IsEqualTo(requested[1]);
        await Assert.That(plan.NotOnServer).IsEquivalentTo(new[] { requested[2], requested[3] });
    }

    [Test]
    public async Task Partition_DeduplicatesRequestedVersions()
    {
        NuGetVersion version = V("1.0.0-alpha");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [version] = Exists(isListed: true)
        };

        DelistPlan plan = DelistPlanning.Partition([version, version, version], listingInfo);

        await Assert.That(plan.ToDelist.Count).IsEqualTo(1);
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer).IsEmpty();
    }

    [Test]
    public async Task Partition_HandlesAnEmptyRequest()
    {
        DelistPlan plan = DelistPlanning.Partition([], new Dictionary<NuGetVersion, PackageVersionListingInfo>());

        await Assert.That(plan.ToDelist).IsEmpty();
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer).IsEmpty();
    }

    [Test]
    [Arguments("1.0.0", true)]
    [Arguments("1.0.0", false)]
    [Arguments("0.1.0-beta.1", true)]
    [Arguments("2.3.4-rc-1", false)]
    public async Task Partition_IsIndependentOfIsListedWhenVersionIsMissing(string version, bool isListed)
    {
        NuGetVersion parsed = V(version);

        Dictionary<NuGetVersion, PackageVersionListingInfo> listed = new() { [parsed] = Exists(isListed) };
        Dictionary<NuGetVersion, PackageVersionListingInfo> missing = new() { [parsed] = Missing() };

        DelistPlan fromMissing = DelistPlanning.Partition([parsed], missing);
        DelistPlan fromListed = DelistPlanning.Partition([parsed], listed);

        // Whatever the listed flag says, a version that is not on the server must land in NotOnServer.
        await Assert.That(fromMissing.NotOnServer.Count).IsEqualTo(1);
        await Assert.That(fromMissing.ToDelist).IsEmpty();
        await Assert.That(fromMissing.AlreadyDelisted).IsEmpty();

        // Sanity: the listed version is routed by IsListed.
        if (isListed)
            await Assert.That(fromListed.ToDelist.Count).IsEqualTo(1);
        else
            await Assert.That(fromListed.AlreadyDelisted.Count).IsEqualTo(1);
    }
}