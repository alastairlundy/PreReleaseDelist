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
using PreReleaseDelistLib;
using PreReleaseDelistLib.Abstractions;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// The composing service's fail-fast dispatch loop and bucket merging (T002, T004): a rate limit on
/// the first dispatch stops the run, bucket outcomes need no dispatch at all, and every requested
/// version yields exactly one outcome in the documented order.
/// </summary>
/// <remarks>
/// Hand-written fakes drive the seams; the <see cref="ScriptedVersionService"/> and
/// <see cref="FixedAvailabilityDetector"/> doubles are shared with the command-level tests.
/// </remarks>
public class PackageDelistServiceTests
{
    private const string ServerUrl = "https://example.test/v3/index.json";

    private const string ApiKey = "unit-test-api-key";

    private const string PackageId = "Demo.Package";

    [Test]
    public async Task FailFast_RateLimitOnFirstDispatchMarksTheRestNotAttempted()
    {
        NuGetVersion first = V("1.0.0");
        NuGetVersion second = V("1.0.1");
        NuGetVersion third = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [first] = Listed(first),
            [second] = Listed(second),
            [third] = Listed(third)
        };

        ScriptedDeleter deleter = new();
        deleter.Enqueue(PackageVersionStatus.RateLimited);
        PackageDelistService service = CreateService(listingInfo, packageExists: true, deleter);

        List<PackageVersionOutcome> outcomes = await CollectAsync(service, [first, second, third]);

        await Assert.That(outcomes.Count).IsEqualTo(3);
        await Assert.That(outcomes[0].Version).IsEqualTo(first);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.RateLimited);
        await Assert.That(outcomes[1].Version).IsEqualTo(second);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.NotAttempted);
        await Assert.That(outcomes[2].Version).IsEqualTo(third);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.NotAttempted);
        // The rate limit is authoritative: exactly one delete request left the process.
        await Assert.That(deleter.CallCount).IsEqualTo(1);
        await Assert.That(deleter.DispatchedVersions.Count).IsEqualTo(1);
        await Assert.That(deleter.DispatchedVersions[0]).IsEqualTo(first);
    }

    [Test]
    public async Task BucketMerge_AlreadyDelistedAndNotOnServer_YieldOutcomesWithoutAnyDispatch()
    {
        NuGetVersion firstUnlisted = V("1.0.1");
        NuGetVersion absent = V("2.0.0");
        NuGetVersion secondUnlisted = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [firstUnlisted] = Unlisted(firstUnlisted),
            [secondUnlisted] = Unlisted(secondUnlisted)
            // absent is deliberately missing from the lookup: not-on-server, not already-delisted.
        };

        // No statuses scripted: any dispatch would throw inside the fake and fail the test loudly.
        ScriptedDeleter deleter = new();
        PackageDelistService service = CreateService(listingInfo, packageExists: true, deleter);

        List<PackageVersionOutcome> outcomes =
            await CollectAsync(service, [firstUnlisted, absent, secondUnlisted]);

        await Assert.That(outcomes.Count).IsEqualTo(3);
        await Assert.That(outcomes[0].Version).IsEqualTo(firstUnlisted);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.AlreadyDelisted);
        await Assert.That(outcomes[1].Version).IsEqualTo(secondUnlisted);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.AlreadyDelisted);
        await Assert.That(outcomes[2].Version).IsEqualTo(absent);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.NotOnServer);
        await Assert.That(deleter.CallCount).IsEqualTo(0);
        await Assert.That(deleter.DispatchedVersions).IsEmpty();
    }

    [Test]
    public async Task OneOutcomePerRequestedVersion_InAlreadyDelistedThenNotOnServerThenDispatchOrder()
    {
        NuGetVersion absent = V("2.0.0");
        NuGetVersion unlisted = V("1.0.1");
        NuGetVersion firstListed = V("1.0.0");
        NuGetVersion secondListed = V("1.0.2");

        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [unlisted] = Unlisted(unlisted),
            [firstListed] = Listed(firstListed),
            [secondListed] = Listed(secondListed)
            // absent is deliberately missing from the lookup.
        };

        ScriptedDeleter deleter = new();
        deleter.Enqueue(PackageVersionStatus.Delisted);
        deleter.Enqueue(PackageVersionStatus.Delisted);
        PackageDelistService service = CreateService(listingInfo, packageExists: true, deleter);

        List<PackageVersionOutcome> outcomes = await CollectAsync(
            service, [absent, unlisted, firstListed, secondListed]);

        await Assert.That(outcomes.Count).IsEqualTo(4);
        await Assert.That(outcomes[0].Version).IsEqualTo(unlisted);
        await Assert.That(outcomes[0].Status).IsEqualTo(PackageVersionStatus.AlreadyDelisted);
        await Assert.That(outcomes[1].Version).IsEqualTo(absent);
        await Assert.That(outcomes[1].Status).IsEqualTo(PackageVersionStatus.NotOnServer);
        await Assert.That(outcomes[2].Version).IsEqualTo(firstListed);
        await Assert.That(outcomes[2].Status).IsEqualTo(PackageVersionStatus.Delisted);
        await Assert.That(outcomes[3].Version).IsEqualTo(secondListed);
        await Assert.That(outcomes[3].Status).IsEqualTo(PackageVersionStatus.Delisted);
        await Assert.That(deleter.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task MissingPackage_ThrowsArgumentExceptionBeforeAnyOutcomeOrDispatch()
    {
        NuGetVersion version = V("1.0.0");
        Dictionary<NuGetVersion, PackageVersionListingInfo> listingInfo = new()
        {
            [version] = Listed(version)
        };

        ScriptedDeleter deleter = new();
        PackageDelistService service = CreateService(listingInfo, packageExists: false, deleter);

        List<PackageVersionOutcome> outcomes = [];
        ArgumentException? caught = null;

        try
        {
            await foreach (PackageVersionOutcome outcome in service.RequestPackageDelistingAsync(
                ServerUrl, ApiKey, PackageId, [version], CancellationToken.None))
            {
                outcomes.Add(outcome);
            }
        }
        catch (ArgumentException exception)
        {
            caught = exception;
        }

        await Assert.That(caught).IsNotNull();

        ArgumentException failure = caught!;

        await Assert.That(failure.ParamName).IsEqualTo("packageId");
        await Assert.That(failure.Message).Contains(PackageId);
        await Assert.That(outcomes).IsEmpty();
        await Assert.That(deleter.CallCount).IsEqualTo(0);
    }

    private static NuGetVersion V(string version) => NuGetVersion.Parse(version);

    private static PackageVersionListingInfo Listed(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = true, PackageVersionExists = true
    };

    private static PackageVersionListingInfo Unlisted(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = false, PackageVersionExists = true
    };

    private static PackageDelistService CreateService(
        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo, bool packageExists,
        IPackageVersionDeleter deleter)
        => new(new ScriptedVersionService(listingInfo),
            new FixedAvailabilityDetector(packageExists), deleter);

    private static async Task<List<PackageVersionOutcome>> CollectAsync(
        PackageDelistService service, IList<NuGetVersion> versions)
    {
        List<PackageVersionOutcome> outcomes = [];

        await foreach (PackageVersionOutcome outcome in service.RequestPackageDelistingAsync(
            ServerUrl, ApiKey, PackageId, versions, CancellationToken.None))
        {
            outcomes.Add(outcome);
        }

        return outcomes;
    }
}

/// <summary>
/// Delete backend double returning scripted statuses in order and counting every dispatch; with an
/// empty script it throws, which doubles as proof that no dispatch happened at all.
/// </summary>
internal sealed class ScriptedDeleter : IPackageVersionDeleter
{
    private readonly Queue<PackageVersionStatus> _statuses = new();

    public int CallCount { get; private set; }

    public List<NuGetVersion> DispatchedVersions { get; } = [];

    public void Enqueue(PackageVersionStatus status) => _statuses.Enqueue(status);

    public ValueTask<PackageVersionOutcome> DeleteAsync(string nugetApiUrl, string packageId,
        NuGetVersion version, string apiKey, CancellationToken cancellationToken = default)
    {
        CallCount++;
        DispatchedVersions.Add(version);

        if (_statuses.Count == 0)
        {
            throw new InvalidOperationException($"No scripted status left for version {version}.");
        }

        return ValueTask.FromResult(new PackageVersionOutcome(packageId, version, _statuses.Dequeue()));
    }
}
