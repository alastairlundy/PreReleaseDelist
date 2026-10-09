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
/// The documented exit-code contract driven end-to-end (T005): every run below goes through DotMake
/// argument binding, the production composition, the real HTTP backend, and the in-process mock NuGet
/// V3 server — the code that comes back is the code the process would exit with.
/// </summary>
/// <remarks>
/// Dry runs are asserted against the documented set too: a dry run dispatches nothing, so nothing can
/// be rate-limited (3) or left unattempted by a cancellation (4); it exits only with 0, 1 or 2.
/// </remarks>
[NotInParallel] // Console.SetOut/SetError are process-global; never interleave captured runs.
public class CliIntegrationExitCodeTests
{
    [Test]
    public async Task EveryRequestedVersionDelisted_ExitsZero_AndDispatchesOneDeleteEach()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitZero");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.0", "1.0.1"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitSuccess);
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(2);
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task VersionsAlreadyDelisted_ExitsZero_WithoutDispatchingAnyDelete()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitAlreadyDelisted");
        server.AddPackage(packageId, ("1.0.0", false));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitSuccess);
        await Assert.That(run.StdOut).Contains("Status=already-delisted");
        // The bucket reported it from listing metadata: no delete request was worth sending.
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task VersionMissingFromServer_ExitsOne_WhileTheRestAreStillDelisted()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitNotOnServer");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.0", "9.9.9"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.StdOut).Contains("Version=9.9.9 Status=not-on-server");
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=delisted");
        // The absent version never reaches the server; the listed one still does.
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(1);
        await Assert.That(server.DeleteRequests[0].Path).IsEqualTo("/publish/" + packageId + "/1.0.0");
    }

    [Test]
    public async Task DeleteRejectedByServer_ExitsOne()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitFailed");
        server.AddPackage(packageId, ("1.0.0", true));
        server.SetDeleteResponseStatus("1.0.0", 500);

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=failed");
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task InvalidBackendOption_ExitsTwo_WithoutTouchingTheServer()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitUsageBackend");

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            "--backend", "bogus",
            "1.0.0");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.StdErr).Contains("Invalid backend value");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.Requests).IsEmpty();
    }

    [Test]
    public async Task InvalidVersionStringUnderStrictParsing_ExitsTwo()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitUsageVersion");

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.x"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.StdErr).Contains("Invalid version string: 1.0.x");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.Requests).IsEmpty();
    }

    [Test]
    public async Task PackageNotOnServer_WritesItsDiagnosticToStderr_AndExitsTwo()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, "No.Such.Package.On.This.Server", "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.StdErr).Contains("No.Such.Package.On.This.Server");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task RateLimitedDelete_StopsTheRunFailFast_AndExitsThree()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitRateLimited");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", true));
        server.SetDeleteResponseStatus("1.0.0", 429);

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, "1.0.0", "1.0.1"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitRateLimited);
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Status=rate-limited");
        await Assert.That(run.StdOut).Contains("Version=1.0.1 Status=not-attempted");
        // Server-side proof of fail-fast: exactly one delete reached the server, so the second
        // version was reported not-attempted because nothing was dispatched for it.
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task InterruptedRun_Exits130_WithNothingPrintedToEitherStream()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("Exit130");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await RunInterruptedAsync(server, packageId);

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitInterrupted);
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(run.StdErr).IsEmpty();
        // The run really was in flight: the service index and availability reads reached the server,
        // and the cancellation landed before a single delete could be dispatched.
        await Assert.That(server.Requests).IsNotEmpty();
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task DryRunWithEveryVersionPresent_ExitsZero_AndSendsNoDeleteRequest()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("DryRunZero");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--dry-run",
            "1.0.0", "1.0.1");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitSuccess);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
        await Assert.That(run.StdOut).Contains("Version=1.0.0 Bucket=would-delist");
        await Assert.That(run.StdOut).Contains("Version=1.0.1 Bucket=already-delisted");
        // Server-side proof: reads happened, and not one delete request was ever sent.
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task DryRunWithAVersionMissingFromServer_ExitsOne_NeverThreeOrFour()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("DryRunOne");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--dry-run",
            "1.0.0", "9.9.9");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
        await Assert.That(run.StdOut).Contains("Version=9.9.9 Bucket=not-on-server");
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task DryRunForAPackageTheServerDoesNotHave_ExitsTwo_NeverThreeOrFour()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", "No.Such.Package.In.A.Dry.Run",
            "--server-url", server.ServiceIndexUrl,
            "--dry-run",
            "1.0.0");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.ExitCode is 0 or 1 or 2).IsTrue();
        await Assert.That(run.StdErr).Contains("No.Such.Package.In.A.Dry.Run");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.DeleteRequests).IsEmpty();
    }

    [Test]
    public async Task MissingRequiredOption_ReportsItOnStderr_AndExitsOneAsDocumented()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--server-url", server.ServiceIndexUrl);

        // README "Exit codes": command lines the argument parser rejects exit 1 with the parser's
        // message on stderr, before the CLI's own validation runs and before any request is made.
        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.StdErr).Contains("--package-id");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.Requests).IsEmpty();
    }

    [Test]
    public async Task OptionMissingItsValue_ReportsItOnStderr_AndExitsOneAsDocumented()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitParseValue");

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key");

        // The other parser-level rejection named in README "Exit codes": the option is present but
        // has no value, so the run is refused before it starts.
        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
        await Assert.That(run.StdErr).Contains("--api-key");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.Requests).IsEmpty();
    }

    [Test]
    public async Task UnknownOption_ReportsItOnStderr_AndExitsTwoWithoutTouchingTheServer()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ExitUnknownOption");

        // The parser has no error for an unknown option: it hands the token to the versions
        // position, so the command itself has to recognise it. A valid version sits next to it to
        // prove the rejection wins over a run that would otherwise have proceeded.
        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            "--typo-dry-run",
            "1.0.0");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        await Assert.That(run.StdErr).Contains("--typo-dry-run");
        await Assert.That(run.StdOut).IsEmpty();
        await Assert.That(server.Requests).IsEmpty();
    }

    private static string NewPackageId(string scenario)
        => $"{scenario}.{Guid.NewGuid():N}";

    private static string[] RealRunArgs(MockNuGetV3Server server, string packageId,
        params string[] versions)
        => ["--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            .. versions];

    /// <summary>
    /// Runs the command against the mock server through the real composing service, with one seam
    /// replaced: the listing read throws <see cref="OperationCanceledException"/> where Ctrl-C would
    /// interrupt an in-flight network read.
    /// </summary>
    /// <remarks>
    /// A real Ctrl-C signal cannot be raised from inside the test host without also signalling the
    /// test runner itself, so the interruption is simulated on the read path whose cancellation the
    /// command boundary maps to 130. Everything else the mapping depends on — argument-bound options,
    /// the composing service's availability check against the mock server, and the command's catch —
    /// is the production code path. The scripted deleter doubles as a tripwire: any dispatch attempt
    /// would throw and fail this test with the wrong exit code.
    /// </remarks>
    private static async Task<CommandRun> RunInterruptedAsync(MockNuGetV3Server server,
        string packageId)
    {
        ServiceCollection services = new();
        services.AddSingleton<IPackageAvailabilityDetector, PackageAvailabilityDetector>();
        services.AddKeyedSingleton<IPackageDelistService>("http", static (provider, _) =>
            new PackageDelistService(
                new InterruptedVersionService(),
                provider.GetRequiredService<IPackageAvailabilityDetector>(),
                new ScriptedDeleter()));

        await using ServiceProvider provider = services.BuildServiceProvider();

        DelistCommand command = new(new ConfigurationBuilder().Build(), provider)
        {
            PackageId = packageId,
            Versions = ["1.0.0"],
            ApiKey = CliIntegrationHarness.TestApiKey,
            ServerUrl = server.ServiceIndexUrl,
        };

        return await DelistCommandHarness.RunAsync(command);
    }
}

/// <summary>
/// Version service whose listing read simulates Ctrl-C arriving mid-flight: the availability check
/// before it is the real one, talking to the mock server, so cancellation lands on an actual
/// in-progress run rather than before it started.
/// </summary>
internal sealed class InterruptedVersionService : IPackageVersionService
{
    public Task<IDictionary<NuGetVersion, PackageVersionListingInfo>> CheckPackageVersionsListedAsync(
        string nugetApiUrl, string nugetApiKey, string packageId, bool includePreReleaseVersions,
        IList<NuGetVersion> packageVersions, CancellationToken cancellationToken)
        => throw new OperationCanceledException("Simulated Ctrl-C during the listing read.");

    public Task<NuGetVersion[]> GetPrereleasePackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("InterruptedVersionService only scripts the listing read.");

    public IAsyncEnumerable<NuGetVersion> EnumeratePrereleasePackageVersionsAsync(string nugetApiUrl,
        string nugetApiKey, string packageId, bool includeZeroMajorVersions = false,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("InterruptedVersionService only scripts the listing read.");

    public IAsyncEnumerable<PackageVersionListingInfo> EnumerateAllPackageVersionsAsync(
        string nugetApiUrl, string nugetApiKey, string packageId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("InterruptedVersionService only scripts the listing read.");

    public Task<NuGetVersion[]> GetAllPackageVersionsAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, CancellationToken cancellationToken)
        => throw new NotSupportedException("InterruptedVersionService only scripts the listing read.");

    public Task<bool> IsPackageVersionDelistedAsync(string nugetApiUrl, string nugetApiKey,
        string packageId, bool includePreReleaseVersions, NuGetVersion packageVersion,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("InterruptedVersionService only scripts the listing read.");
}
