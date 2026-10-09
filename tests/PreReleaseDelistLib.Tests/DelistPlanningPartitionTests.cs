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

using NuGet.Versioning;
using PreReleaseDelistLib.Internal;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Unit coverage for <see cref="DelistPlanning.Partition"/>: which bucket each requested version
/// lands in, de-duplication, and the missing-key rule that keeps an unknown version out of the
/// success buckets.
/// </summary>
/// <remarks>
/// The missing-key rule is the important one: a version the server never reported must surface as
/// not-on-server (a failure), never as already-delisted (a success).
/// </remarks>
public class DelistPlanningPartitionTests
{
    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    private static PackageVersionListingInfo Listed() => new()
    {
        PackageVersion = null!, IsListed = true, PackageVersionExists = true
    };

    private static PackageVersionListingInfo Unlisted() => new()
    {
        PackageVersion = null!, IsListed = false, PackageVersionExists = true
    };

    private static PackageVersionListingInfo Absent() => new()
    {
        PackageVersion = null!, IsListed = false, PackageVersionExists = false
    };

    [Test]
    public async Task Partition_RoutesEveryRequestedVersionToExactlyOneBucket()
    {
        NuGetVersion listedVersion = V("1.0.0");
        NuGetVersion unlistedVersion = V("1.0.1-beta.2");
        NuGetVersion absentVersion = V("2.0.0-rc.1");
        NuGetVersion missingKeyVersion = V("9.9.9");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [listedVersion] = Listed(),
            [unlistedVersion] = Unlisted(),
            [absentVersion] = Absent()
            // missingKeyVersion is deliberately absent from the lookup entirely.
        };

        DelistPlan plan = DelistPlanning.Partition(
            [listedVersion, unlistedVersion, absentVersion, missingKeyVersion], listingInfo);

        await Assert.That(plan.ToDelist.Count).IsEqualTo(1);
        await Assert.That(plan.ToDelist[0]).IsEqualTo(listedVersion);
        await Assert.That(plan.AlreadyDelisted.Count).IsEqualTo(1);
        await Assert.That(plan.AlreadyDelisted[0]).IsEqualTo(unlistedVersion);
        await Assert.That(plan.NotOnServer.Count).IsEqualTo(2);
        await Assert.That(plan.NotOnServer[0]).IsEqualTo(absentVersion);
        await Assert.That(plan.NotOnServer[1]).IsEqualTo(missingKeyVersion);
    }

    [Test]
    public async Task Partition_DeduplicatesRepeatedVersions()
    {
        NuGetVersion version = V("1.0.0-alpha.1");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [version] = Listed()
        };

        DelistPlan plan = DelistPlanning.Partition([version, version, version], listingInfo);

        await Assert.That(plan.ToDelist.Count).IsEqualTo(1);
        await Assert.That(plan.ToDelist[0]).IsEqualTo(version);
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer).IsEmpty();
    }

    [Test]
    public async Task Partition_MissingLookupKeyIsNotOnServerRatherThanAlreadyDelisted()
    {
        NuGetVersion ghost = V("1.2.3");

        DelistPlan plan = DelistPlanning.Partition([ghost],
            new Dictionary<NuGetVersion, PackageVersionListingInfo>());

        await Assert.That(plan.NotOnServer.Count).IsEqualTo(1);
        await Assert.That(plan.NotOnServer[0]).IsEqualTo(ghost);
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.ToDelist).IsEmpty();
    }

    [Test]
    public async Task Partition_PresentButAbsentFromServerNeverReportsAlreadyDelisted()
    {
        NuGetVersion ghost = V("0.9.0-beta");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [ghost] = Absent()
        };

        DelistPlan plan = DelistPlanning.Partition([ghost], listingInfo);

        await Assert.That(plan.NotOnServer.Count).IsEqualTo(1);
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.ToDelist).IsEmpty();
    }

    [Test]
    public async Task Partition_EmptyRequestYieldsAnEmptyPlan()
    {
        DelistPlan plan = DelistPlanning.Partition([],
            new Dictionary<NuGetVersion, PackageVersionListingInfo>());

        await Assert.That(plan.ToDelist).IsEmpty();
        await Assert.That(plan.AlreadyDelisted).IsEmpty();
        await Assert.That(plan.NotOnServer).IsEmpty();
    }
}
