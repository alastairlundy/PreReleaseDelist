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

using System.Text.Json;
using System.Text.RegularExpressions;
using PreReleaseDelistCli;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// The documented output formats and stream routing driven end-to-end (T006, T007): human grouping,
/// <c>--non-interactive</c> kebab lines, NDJSON with a fixed three-field order, and the stdout/stderr
/// split — all asserted against the in-process mock NuGet V3 server.
/// </summary>
[NotInParallel] // Console.SetOut/SetError are process-global; never interleave captured runs.
public class CliIntegrationOutputTests
{
    private const string StatusVocabulary =
        "delisted|already-delisted|not-on-server|rate-limited|failed|not-attempted";

    [Test]
    public async Task DefaultOutput_GroupsVersionsIntoDelistedAndFailureSectionsOnStdout()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("HumanOutput");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, [], "1.0.0", "9.9.9"));

        string[] lines = Lines(run.StdOut);

        await Assert.That(lines.Length).IsEqualTo(4);
        await Assert.That(lines[0]).IsEqualTo($"Versions Delisted for Package: {packageId}");
        await Assert.That(lines[1]).IsEqualTo("Version=1.0.0 Status=delisted");
        await Assert.That(lines[2]).IsEqualTo($"The following versions of {packageId} could not be delisted:");
        await Assert.That(lines[3]).IsEqualTo("Version=9.9.9 Status=not-on-server");
        // Results and summaries belong to stdout; this run produced no diagnostics at all.
        await Assert.That(run.StdErr).IsEmpty();
        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitFailure);
    }

    [Test]
    public async Task NonInteractivePrintsOneVersionStatusLinePerOutcome_InOutcomeOrder()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("NonInteractive");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, ["--non-interactive"], "1.0.0", "1.0.1", "9.9.9"));

        string[] lines = Lines(run.StdOut);

        // Outcome order: the already-delisted bucket, then the absent one, then the dispatches.
        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("Version=1.0.1 Status=already-delisted");
        await Assert.That(lines[1]).IsEqualTo("Version=9.9.9 Status=not-on-server");
        await Assert.That(lines[2]).IsEqualTo("Version=1.0.0 Status=delisted");

        foreach (string line in lines)
        {
            await Assert.That(Regex.IsMatch(line, $@"^Version=\S+ Status=({StatusVocabulary})$")).IsTrue();
        }

        // Non-interactive output drops the human grouping headers entirely.
        await Assert.That(run.StdOut.Contains("Versions Delisted for Package")).IsFalse();
        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task JsonOutputEmitsOneLinePerVersion_WithExactlyThreeFieldsInFixedOrder()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("JsonOutput");
        server.AddPackage(packageId, ("1.0.0", true), ("1.0.1", false));

        // --non-interactive is deliberately also set: --output json takes precedence over it (README).
        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, ["--output", "json", "--non-interactive"],
                "1.0.0", "1.0.1", "9.9.9"));

        string[] lines = Lines(run.StdOut);

        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo(
            $"{{\"package\":\"{packageId}\",\"version\":\"1.0.1\",\"status\":\"already-delisted\"}}");
        await Assert.That(lines[1]).IsEqualTo(
            $"{{\"package\":\"{packageId}\",\"version\":\"9.9.9\",\"status\":\"not-on-server\"}}");
        await Assert.That(lines[2]).IsEqualTo(
            $"{{\"package\":\"{packageId}\",\"version\":\"1.0.0\",\"status\":\"delisted\"}}");

        foreach (string line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);

            // Exactly three fields, no envelope, no array wrapper, fixed order package, version, status.
            string fieldOrder = string.Join(",",
                document.RootElement.EnumerateObject().Select(static property => property.Name));

            await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Object);
            await Assert.That(fieldOrder).IsEqualTo("package,version,status");
        }

        await Assert.That(run.StdErr).IsEmpty();
    }

    [Test]
    public async Task JsonOutputValidationFailure_WritesHumanDiagnosticsToStderr_AndNothingToStdout()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("JsonStderr");

        CommandRun run = await CliIntegrationHarness.RunAsync(
            "--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            "--output", "json",
            "--backend", "bogus",
            "1.0.0");

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitUsage);
        // stderr stays human-readable in JSON mode: a plain diagnostic line, not a JSON payload.
        await Assert.That(run.StdErr).Contains("Error: Invalid backend value");
        await Assert.That(run.StdErr.Contains("\"status\"")).IsFalse();
        await Assert.That(run.StdOut).IsEmpty();
    }

    [Test]
    public async Task ApiKeyNeverAppearsInEitherStream_ButStillReachesTheServer()
    {
        using MockNuGetV3Server server = MockNuGetV3Server.Start();
        string packageId = NewPackageId("ApiKeyRouting");
        server.AddPackage(packageId, ("1.0.0", true));

        CommandRun run = await CliIntegrationHarness.RunAsync(
            RealRunArgs(server, packageId, ["--output", "json"], "1.0.0"));

        await Assert.That(run.ExitCode).IsEqualTo(DelistCommand.ExitSuccess);
        await Assert.That(run.StdOut.Contains(CliIntegrationHarness.TestApiKey)).IsFalse();
        await Assert.That(run.StdErr.Contains(CliIntegrationHarness.TestApiKey)).IsFalse();

        // The key still went where it belongs: the delete request, on the wire to the server.
        await Assert.That(server.DeleteRequests.Count).IsEqualTo(1);
        await Assert.That(server.DeleteRequests[0].ApiKey).IsEqualTo(CliIntegrationHarness.TestApiKey);
    }

    private static string NewPackageId(string scenario)
        => $"{scenario}.{Guid.NewGuid():N}";

    private static string[] RealRunArgs(MockNuGetV3Server server, string packageId,
        string[] options, params string[] versions)
        => ["--package-id", packageId,
            "--server-url", server.ServiceIndexUrl,
            "--api-key", CliIntegrationHarness.TestApiKey,
            .. options,
            .. versions];

    /// <summary>Splits captured stdout into lines on either platform's terminator.</summary>
    private static string[] Lines(string text)
        => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
}
