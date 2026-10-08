/*
    prerelease-delist - Delist pre-release package versions from a Nuget Server
    Copyright (C) 2026 Alastair Lundy

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
     any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

namespace PreReleaseDelistCli;

/// <summary>
/// Describes a single options validation failure.
/// Ticket 007 maps any failure to the usage exit code (T005).
/// </summary>
/// <param name="Field">The option field that failed validation.</param>
/// <param name="Message">Human-readable description of the failure.</param>
public sealed record DelistOptionsValidationFailure(string Field, string Message);

/// <summary>
/// Resolved, validated CLI options. Pure logic only: no console access,
/// no service resolution, no <c>IConfiguration</c> or environment access.
/// Callers prefetch configuration values as plain strings and pass them in,
/// so unit tests can drive every branch. Per T009 (dedicated options type).
/// </summary>
public sealed record DelistOptions
{
    /// <summary>
    /// Built-in default when no server URL is supplied at any level.
    /// </summary>
    public const string DefaultServerUrl = "https://api.nuget.org/v3/index.json";

    /// <summary>
    /// The default delisting backend when none is supplied.
    /// </summary>
    public const string DefaultBackend = "http";

    /// <summary>
    /// The default output mode when none is supplied.
    /// </summary>
    public const string DefaultOutputMode = "text";

    public string PackageId { get; init; } = string.Empty;

    public IReadOnlyList<string> Versions { get; init; } = [];

    public string? ApiKey { get; init; }

    public string ServerUrl { get; init; } = DefaultServerUrl;

    public string Backend { get; init; } = DefaultBackend;

    public bool NonInteractive { get; init; }

    public bool IncludeZeroMajor { get; init; }

    public bool UseStrictParsing { get; init; } = true;

    public bool DelistAllVersions { get; init; }

    public bool DryRun { get; init; }

    public string OutputMode { get; init; } = DefaultOutputMode;

    /// <summary>
    /// Resolves the server URL through the documented four-step precedence chain:
    /// explicit <c>--server-url</c> option first, then the
    /// <c>PRERELEASEDELIST_NuGetServerUrl</c> prefixed variable, then the generic
    /// <c>NUGET_SERVER_URL</c> variable, then the built-in default.
    /// Whitespace counts as unset. Matches README Configuration section.
    /// </summary>
    public static string ResolveServerUrl(string? optionValue, string? prefixedValue, string? genericValue)
    {
        if (!string.IsNullOrWhiteSpace(optionValue))
        {
            return optionValue;
        }

        if (!string.IsNullOrWhiteSpace(prefixedValue))
        {
            return prefixedValue;
        }

        if (!string.IsNullOrWhiteSpace(genericValue))
        {
            return genericValue;
        }

        return DefaultServerUrl;
    }

    /// <summary>
    /// Resolves the API key through the documented chain:
    /// explicit <c>--api-key</c> option first, then the
    /// <c>PRERELEASEDELIST_NuGetApiKey</c> prefixed variable, then the generic
    /// <c>NUGET_API_KEY</c> variable, then unset (null — missing key is a
    /// validation failure unless dry-run mode skips it per T003).
    /// Empty counts as unset. Matches README Configuration section.
    /// </summary>
    public static string? ResolveApiKey(string? optionValue, string? prefixedValue, string? genericValue)
    {
        if (!string.IsNullOrEmpty(optionValue))
        {
            return optionValue;
        }

        if (!string.IsNullOrEmpty(prefixedValue))
        {
            return prefixedValue;
        }

        if (!string.IsNullOrEmpty(genericValue))
        {
            return genericValue;
        }

        return null;
    }

    /// <summary>
    /// Builds a fully-resolved <see cref="DelistOptions"/> from bound values plus
    /// prefetched configuration strings. All inputs are plain values so the type
    /// stays pure (no configuration or environment access inside).
    /// </summary>
    public static DelistOptions Create(
        string? packageId,
        IEnumerable<string>? versions,
        string? apiKeyOption,
        string? apiKeyPrefixed,
        string? apiKeyGeneric,
        string? serverUrlOption,
        string? serverUrlPrefixed,
        string? serverUrlGeneric,
        string? backend,
        bool nonInteractive,
        bool includeZeroMajor,
        bool useStrictParsing,
        bool delistAllVersions,
        bool dryRun,
        string? outputMode)
    {
        return new DelistOptions
        {
            PackageId = packageId ?? string.Empty,
            Versions = versions?.ToArray() ?? [],
            ApiKey = ResolveApiKey(apiKeyOption, apiKeyPrefixed, apiKeyGeneric),
            ServerUrl = ResolveServerUrl(serverUrlOption, serverUrlPrefixed, serverUrlGeneric),
            Backend = string.IsNullOrWhiteSpace(backend) ? DefaultBackend : backend,
            NonInteractive = nonInteractive,
            IncludeZeroMajor = includeZeroMajor,
            UseStrictParsing = useStrictParsing,
            DelistAllVersions = delistAllVersions,
            DryRun = dryRun,
            OutputMode = string.IsNullOrWhiteSpace(outputMode) ? DefaultOutputMode : outputMode,
        };
    }

    /// <summary>
    /// Validates the resolved options, returning one failure per violated rule.
    /// Dry-run mode skips the API-key requirement (delete requests are the only
    /// credential-bearing path per T003); a real run requires it.
    /// </summary>
    public IReadOnlyList<DelistOptionsValidationFailure> Validate()
    {
        List<DelistOptionsValidationFailure> failures = [];

        if (string.IsNullOrWhiteSpace(PackageId))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(PackageId),
                "Package id is required."));
        }

        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(ServerUrl),
                "Server URL must not be empty."));
        }

        if (!string.Equals(Backend, "http", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Backend, "sdk", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(Backend),
                "Invalid backend value. Valid values are: http, sdk."));
        }

        if (!DelistAllVersions && (Versions is null || Versions.Count == 0))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(Versions),
                "At least one version string is required unless --delist-all is set."));
        }

        if (!DryRun && string.IsNullOrEmpty(ApiKey))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(ApiKey),
                "API key is required for a real run (dry-run mode skips this requirement)."));
        }

        if (!string.Equals(OutputMode, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(OutputMode, "json", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(new DelistOptionsValidationFailure(
                nameof(OutputMode),
                "Invalid output mode. Valid values are: text, json."));
        }

        return failures;
    }
}
