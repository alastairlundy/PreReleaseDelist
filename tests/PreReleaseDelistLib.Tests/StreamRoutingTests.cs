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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Versioning;
using PreReleaseDelistCli;
using PreReleaseDelistLib;
using PreReleaseDelistLib.Abstractions;
using PreReleaseDelistLib.Detectors;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Stream routing under the presentation contract (T006): the dry-run plan and results go to
/// stdout, every diagnostic goes to stderr. The routing decision lives in the command body, so
/// these tests drive the real <see cref="DelistCommand"/> over hand-written service fakes.
/// </summary>
[NotInParallel] // Console.SetOut/SetError are process-global; never interleave captured runs.
public class StreamRoutingTests
{
    [Test]
    public async Task ValidationFailure_WritesOnlyToStderrAndExitsTwo()
    {
        DelistCommand command = DelistCommandHarness.CreateCommand(
            packageId: "   ", versions: ["1.0.0"], dryRun: true);

        CommandRun run = await DelistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.StdErr).IsNotEmpty();
        await Assert.That(run.StdOut).IsEmpty();
    }

    [Test]
    public async Task DryRun_WithEveryVersionPresent_WritesPlanToStdoutOnly()
    {
        NuGetVersion first = NuGetVersion.Parse("1.0.0");
        NuGetVersion second = NuGetVersion.Parse("1.0.1");
        DelistCommand command = DelistCommandHarness.CreateCommand(
            packageId: "Demo.Package", versions: ["1.0.0", "1.0.1"], dryRun: true,
            listingInfo: new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [first] = DelistCommandHarness.ListedInfo(first),
                [second] = DelistCommandHarness.ListedInfo(second)
            });

        CommandRun run = await DelistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitSuccess);
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Bucket=would-delist");
        await Assert.That(run.StdOut).Contains("Version=1.0.1 Bucket=would-delist");
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task DryRun_WithAVersionMissingFromServer_WritesPlanToStdoutAndExitsOne()
    {
        NuGetVersion present = NuGetVersion.Parse("1.0.0");
        DelistCommand command = DelistCommandHarness.CreateCommand(
            packageId: "Demo.Package", versions: ["1.0.0", "9.9.9"], dryRun: true,
            listingInfo: new Dictionary<NuGetVersion, PackageVersionListingInfo>
            {
                [present] = DelistCommandHarness.ListedInfo(present)
            });

        CommandRun run = await DelistCommandHarness.RunAsync(command);

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Bucket=would-delist");
        await Assert.That(run.StdOut).Contains("Version=9.9.9 Bucket=not-on-server");
        await Assert.That(run.StdErr).IsEmpty();
    }
}

/// <summary>
/// Builds a real <see cref="DelistCommand"/> over fake services and runs it with stdout and stderr
/// captured. Shared by every test that drives <c>RunAsync</c>, including the dry-run exit-code
/// proofs in <see cref="ExitCodeMappingTests"/>.
/// </summary>
/// <remarks>
/// The command reads <c>NUGET_API_KEY</c> and <c>NUGET_SERVER_URL</c> from the environment at
/// construction, so every run here sets both options explicitly: the option value always wins the
/// precedence chain, which keeps the tests independent of the machine they run on.
/// </remarks>
internal static class DelistCommandHarness
{
    internal const string TestServerUrl = "https://example.test/v3/index.json";

    internal const string TestApiKey = "unit-test-api-key";

    internal static PackageVersionListingInfo ListedInfo(NuGetVersion version) => new()
    {
        PackageVersion = version, IsListed = true, PackageVersionExists = true
    };

    internal static DelistCommand CreateCommand(string packageId, string[] versions, bool dryRun,
        IDictionary<NuGetVersion, PackageVersionListingInfo>? listingInfo = null,
        bool packageExists = true)
    {
        ServiceCollection services = new();
        services.AddSingleton<IPackageAvailabilityDetector>(new FixedAvailabilityDetector(packageExists));
        services.AddSingleton<IPackageVersionService>(new ScriptedVersionService(
            listingInfo ?? new Dictionary<NuGetVersion, PackageVersionListingInfo>()));
        // RunAsync resolves the keyed delisting service before it branches into the dry-run path,
        // so even a dry run needs an "http" registration to exist.
        services.AddKeyedSingleton<IPackageDelistService, UnusedDelistService>("http");

        DelistCommand command = new(new ConfigurationBuilder().Build(), services.BuildServiceProvider())
        {
            PackageId = packageId,
            Versions = versions,
            ApiKey = TestApiKey,
            ServerUrl = TestServerUrl,
            DryRun = dryRun,
        };

        return command;
    }

    internal static async Task<CommandRun> RunAsync(DelistCommand command)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter stdout = new();
        using StringWriter stderr = new();

        try
        {
            // TUnit0055 warns that overwriting the console writer can break TUnit logging. The
            // capture is deliberate and safe here: every test class that drives RunAsync is marked
            // [NotInParallel], so no two captured runs (or uncaptured test output) can interleave,
            // and the finally block always restores the original writers.
#pragma warning disable TUnit0055
            Console.SetOut(stdout);
            Console.SetError(stderr);
#pragma warning restore TUnit0055

            int exitCode = await command.RunAsync();

            return new CommandRun(exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
            Console.SetError(originalError);
#pragma warning restore TUnit0055
        }
    }
}

/// <summary>The exit code and captured stream contents of one <c>RunAsync</c> invocation.</summary>
internal sealed record CommandRun(int ExitCode, string StdOut, string StdErr);

/// <summary>Availability detector with a fixed, scripted answer.</summary>
internal sealed class FixedAvailabilityDetector(bool packageExists) : IPackageAvailabilityDetector
{
    public Task<bool> CheckPackageExistsAsync(string nugetApiUrl, string packageId,
        CancellationToken cancellationToken)
        => Task.FromResult(packageExists);
}

/// <summary>
/// Version service returning scripted listing metadata from <see cref="CheckPackageVersionsListedAsync"/>;
/// every enumeration member is deliberately unscripted and fails loudly if a test reaches it.
/// </summary>
internal sealed class ScriptedVersionService(
    IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo) : IPackageVersionService
{
    public Task<IDictionary<NuGetVersion, PackageVersionListingInfo>> CheckPackageVersionsListedAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, bool includePreReleaseVersions,
        IList<NuGetVersion> packageVersions, CancellationToken cancellationToken)
        => Task.FromResult(listingInfo);

    public Task<NuGetVersion[]> GetPrereleasePackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Array.Empty<NuGetVersion>());

    public IAsyncEnumerable<NuGetVersion> EnumeratePrereleasePackageVersionsAsync(string nugetApiUrl,
        string nugetApiKey, string packageId, bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ScriptedVersionService does not script enumeration.");

    public IAsyncEnumerable<PackageVersionListingInfo> EnumerateAllPackageVersionsAsync(string nugetApiUrl,
        string nugetApiKey, string packageId, CancellationToken cancellationToken)
        => throw new NotSupportedException("ScriptedVersionService does not script enumeration.");

    public Task<NuGetVersion[]> GetAllPackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, CancellationToken cancellationToken)
        => throw new NotSupportedException("ScriptedVersionService does not script enumeration.");

    public Task<bool> IsPackageVersionDelistedAsync(string nugetApiUrl, string nugetApiKey, string packageId,
        bool includePreReleaseVersions, NuGetVersion packageVersion, CancellationToken cancellationToken)
        => throw new NotSupportedException("ScriptedVersionService does not script delisting checks.");
}

/// <summary>
/// A delisting service that must never be invoked: the dry-run path resolves it from the container
/// but performs no dispatch, so any call is a contract violation.
/// </summary>
internal sealed class UnusedDelistService : IPackageDelistService
{
    public IAsyncEnumerable<PackageVersionOutcome> RequestPackageDelistingAsync(string nugetApiUrl,
        string nugetApiKey, string packageName, bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("A dry run must not dispatch delisting requests.");

    public IAsyncEnumerable<PackageVersionOutcome> RequestPackageDelistingAsync(string nugetApiUrl,
        string nugetApiKey, string packageName, IList<NuGetVersion> versions,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("A dry run must not dispatch delisting requests.");
}
