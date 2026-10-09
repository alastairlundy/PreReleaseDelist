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

using PreReleaseDelistCli;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// <see cref="DelistOptions"/> precedence resolution (T009) and mode-aware validation (T003):
/// option, then <c>PRERELEASEDELIST_*</c>, then <c>NUGET_*</c>, then default — and every validation
/// rule shown with both its failing and its passing shape.
/// </summary>
public class DelistOptionsTests
{
    [Test]
    public async Task ResolveServerUrl_PrefersOptionThenPrefixedThenGenericThenDefault()
    {
        await Assert.That(DelistOptions.ResolveServerUrl(
                "https://option.example", "https://prefixed.example", "https://generic.example"))
            .IsEqualTo("https://option.example");
        await Assert.That(DelistOptions.ResolveServerUrl(
                null, "https://prefixed.example", "https://generic.example"))
            .IsEqualTo("https://prefixed.example");
        await Assert.That(DelistOptions.ResolveServerUrl(null, null, "https://generic.example"))
            .IsEqualTo("https://generic.example");
        await Assert.That(DelistOptions.ResolveServerUrl(null, null, null))
            .IsEqualTo(DelistOptions.DefaultServerUrl);
    }

    [Test]
    public async Task ResolveServerUrl_TreatsWhitespaceAsUnset()
    {
        await Assert.That(DelistOptions.ResolveServerUrl("   ", "https://prefixed.example", null))
            .IsEqualTo("https://prefixed.example");
        await Assert.That(DelistOptions.ResolveServerUrl("  ", " \t ", " "))
            .IsEqualTo(DelistOptions.DefaultServerUrl);
    }

    [Test]
    public async Task ResolveApiKey_PrefersOptionThenPrefixedThenGenericThenUnset()
    {
        await Assert.That(DelistOptions.ResolveApiKey("option-key", "prefixed-key", "generic-key"))
            .IsEqualTo("option-key");
        await Assert.That(DelistOptions.ResolveApiKey(null, "prefixed-key", "generic-key"))
            .IsEqualTo("prefixed-key");
        await Assert.That(DelistOptions.ResolveApiKey(null, null, "generic-key"))
            .IsEqualTo("generic-key");
        await Assert.That(DelistOptions.ResolveApiKey(null, null, null)).IsNull();
        await Assert.That(DelistOptions.ResolveApiKey("", "", "")).IsNull();
    }

    [Test]
    public async Task ResolveApiKey_CountsWhitespaceOnlyValuesAsSet()
    {
        // Actual behaviour: the API-key chain uses IsNullOrEmpty, not IsNullOrWhiteSpace, so a
        // whitespace-only value short-circuits the chain instead of falling through.
        await Assert.That(DelistOptions.ResolveApiKey("   ", "prefixed-key", null)).IsEqualTo("   ");
        await Assert.That(DelistOptions.ResolveApiKey(null, "  ", "generic-key")).IsEqualTo("  ");
    }

    [Test]
    public async Task Create_WhenEverythingUnset_AppliesTheDocumentedDefaults()
    {
        DelistOptions options = DelistOptions.Create(
            packageId: null, versions: null, apiKeyOption: null, apiKeyPrefixed: null, apiKeyGeneric: null,
            serverUrlOption: null, serverUrlPrefixed: null, serverUrlGeneric: null,
            backend: null, nonInteractive: false, includeZeroMajor: false, useStrictParsing: false,
            delistAllVersions: false, dryRun: false, outputMode: null);

        await Assert.That(options.PackageId).IsEqualTo(string.Empty);
        await Assert.That(options.Versions).IsEmpty();
        await Assert.That(options.ApiKey).IsNull();
        await Assert.That(options.ServerUrl).IsEqualTo(DelistOptions.DefaultServerUrl);
        await Assert.That(options.Backend).IsEqualTo(DelistOptions.DefaultBackend);
        await Assert.That(options.OutputMode).IsEqualTo(DelistOptions.DefaultOutputMode);
        await Assert.That(options.UseStrictParsing).IsFalse();
        await Assert.That(options.DryRun).IsFalse();
    }

    [Test]
    public async Task Create_WhenBackendOrOutputModeAreWhitespace_AppliesTheDefaults()
    {
        DelistOptions options = DelistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0"], apiKeyOption: null, apiKeyPrefixed: null,
            apiKeyGeneric: null, serverUrlOption: null, serverUrlPrefixed: null, serverUrlGeneric: null,
            backend: "  ", nonInteractive: false, includeZeroMajor: false, useStrictParsing: true,
            delistAllVersions: false, dryRun: false, outputMode: "\t");

        await Assert.That(options.Backend).IsEqualTo(DelistOptions.DefaultBackend);
        await Assert.That(options.OutputMode).IsEqualTo(DelistOptions.DefaultOutputMode);
    }

    [Test]
    public async Task Create_PassesSuppliedValuesThroughUnchanged()
    {
        DelistOptions options = DelistOptions.Create(
            packageId: "Demo.Package", versions: ["1.0.0", "1.0.1"], apiKeyOption: "option-key",
            apiKeyPrefixed: "prefixed-key", apiKeyGeneric: "generic-key",
            serverUrlOption: "https://option.example", serverUrlPrefixed: "https://prefixed.example",
            serverUrlGeneric: "https://generic.example", backend: "sdk", nonInteractive: true,
            includeZeroMajor: true, useStrictParsing: false, delistAllVersions: true, dryRun: true,
            outputMode: "json");

        await Assert.That(options.PackageId).IsEqualTo("Demo.Package");
        await Assert.That(options.Versions).IsEquivalentTo(new[] { "1.0.0", "1.0.1" });
        await Assert.That(options.ApiKey).IsEqualTo("option-key");
        await Assert.That(options.ServerUrl).IsEqualTo("https://option.example");
        await Assert.That(options.Backend).IsEqualTo("sdk");
        await Assert.That(options.OutputMode).IsEqualTo("json");
        await Assert.That(options.NonInteractive).IsTrue();
        await Assert.That(options.IncludeZeroMajor).IsTrue();
        await Assert.That(options.UseStrictParsing).IsFalse();
        await Assert.That(options.DelistAllVersions).IsTrue();
        await Assert.That(options.DryRun).IsTrue();
    }

    [Test]
    public async Task Validate_DryRunWithoutApiKey_HasNoFailures()
    {
        IReadOnlyList<DelistOptionsValidationFailure> failures = Valid(dryRun: true, apiKey: null).Validate();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Validate_RealRunWithoutApiKey_FailsOnlyOnTheApiKeyRule()
    {
        IReadOnlyList<DelistOptionsValidationFailure> failures = Valid(dryRun: false, apiKey: null).Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.ApiKey));
    }

    [Test]
    public async Task Validate_RealRunWithApiKey_HasNoFailures()
    {
        IReadOnlyList<DelistOptionsValidationFailure> failures = Valid(dryRun: false, apiKey: "unit-test-api-key").Validate();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Validate_MissingPackageId_FailsWithThePackageIdFieldOnly()
    {
        DelistOptions options = Valid() with { PackageId = "   " };

        IReadOnlyList<DelistOptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.PackageId));
    }

    [Test]
    public async Task Validate_WhitespaceServerUrl_FailsWithTheServerUrlFieldOnly()
    {
        DelistOptions options = Valid() with { ServerUrl = " " };

        IReadOnlyList<DelistOptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.ServerUrl));
    }

    [Test]
    public async Task Validate_InvalidBackend_FailsWhileHttpAndSdkPass()
    {
        DelistOptions invalid = Valid() with { Backend = "ftp" };
        IReadOnlyList<DelistOptionsValidationFailure> failures = invalid.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.Backend));

        await Assert.That((Valid() with { Backend = "http" }).Validate()).IsEmpty();
        await Assert.That((Valid() with { Backend = "SDK" }).Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_NoVersionsFailsUnlessDelistAllIsSet()
    {
        DelistOptions noVersions = Valid() with { Versions = [] };
        IReadOnlyList<DelistOptionsValidationFailure> failures = noVersions.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.Versions));

        await Assert.That((Valid() with { Versions = [], DelistAllVersions = true }).Validate()).IsEmpty();
        await Assert.That(Valid().Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_InvalidOutputMode_FailsWhileTextAndJsonPass()
    {
        DelistOptions invalid = Valid() with { OutputMode = "xml" };
        IReadOnlyList<DelistOptionsValidationFailure> failures = invalid.Validate();

        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.OutputMode));

        await Assert.That((Valid() with { OutputMode = "text" }).Validate()).IsEmpty();
        await Assert.That((Valid() with { OutputMode = "JSON" }).Validate()).IsEmpty();
    }

    [Test]
    public async Task Validate_ReportsEveryViolatedRuleAtOnceInDeclarationOrder()
    {
        DelistOptions options = new()
        {
            PackageId = " ",
            ServerUrl = " ",
            Backend = "bogus",
            Versions = [],
            ApiKey = null,
            OutputMode = "xml",
            DryRun = false,
            DelistAllVersions = false,
        };

        IReadOnlyList<DelistOptionsValidationFailure> failures = options.Validate();

        await Assert.That(failures.Count).IsEqualTo(6);
        await Assert.That(failures[0].Field).IsEqualTo(nameof(DelistOptions.PackageId));
        await Assert.That(failures[1].Field).IsEqualTo(nameof(DelistOptions.ServerUrl));
        await Assert.That(failures[2].Field).IsEqualTo(nameof(DelistOptions.Backend));
        await Assert.That(failures[3].Field).IsEqualTo(nameof(DelistOptions.Versions));
        await Assert.That(failures[4].Field).IsEqualTo(nameof(DelistOptions.ApiKey));
        await Assert.That(failures[5].Field).IsEqualTo(nameof(DelistOptions.OutputMode));
    }

    /// <summary>An otherwise-valid, real-run configuration; each test overrides one rule at a time.</summary>
    private static DelistOptions Valid(bool dryRun = false, string? apiKey = "unit-test-api-key") => new()
    {
        PackageId = "Demo.Package",
        Versions = ["1.0.0"],
        ApiKey = apiKey,
        ServerUrl = "https://example.test/v3/index.json",
        Backend = "http",
        OutputMode = "text",
        UseStrictParsing = true,
        DryRun = dryRun,
    };
}
