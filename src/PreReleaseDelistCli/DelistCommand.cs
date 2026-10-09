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

using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PreReleaseDelistCli.Helpers;
using PreReleaseDelistLib.Models;

namespace PreReleaseDelistCli;

/// <summary>
/// The delisting command: the CLI's single presentation and failure boundary.
/// </summary>
/// <remarks>
/// <para>
/// DotMake binding attributes stay on this class and feed <see cref="DelistOptions"/>; the body consumes
/// the resolved options and never resolves precedence itself (T009). No options object crosses the seam:
/// the service layer keeps its explicit parameters.
/// </para>
/// <para>
/// Results, summaries, and the dry-run plan print to stdout; every error and diagnostic prints to stderr,
/// and the API key is never written to any stream (T006). The process exits only with the documented
/// codes 0, 1, 2, 3, 4, or 130 (T005); anything unexpected is left unhandled so it crashes with its stack
/// trace (T012).
/// </para>
/// </remarks>
[CliCommand(Name = "")]
public class DelistCommand
{
    /// <summary>Every requested version was delisted or was already delisted.</summary>
    internal const int ExitSuccess = 0;

    /// <summary>At least one version failed, including a version the server does not have.</summary>
    internal const int ExitFailure = 1;

    /// <summary>Usage or validation failure: bad options, package not found, invalid version string.</summary>
    internal const int ExitUsage = 2;

    /// <summary>The server rate-limited the run and the composing service stopped fail-fast.</summary>
    internal const int ExitRateLimited = 3;

    /// <summary>Remaining dispatches were cancelled before they were attempted.</summary>
    internal const int ExitCancelled = 4;

    /// <summary>The run was interrupted (Ctrl-C).</summary>
    internal const int ExitInterrupted = 130;

    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _apiKeyGeneric;
    private readonly string? _serverUrlGeneric;

    public DelistCommand(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;

        // Prefetched as plain values so the command body holds no environment-variable lookups; the
        // precedence chain itself lives in DelistOptions.Create (T009).
        _apiKeyGeneric = Environment.GetEnvironmentVariable("NUGET_API_KEY");
        _serverUrlGeneric = Environment.GetEnvironmentVariable("NUGET_SERVER_URL");
    }

    // Initialized by DotMake option/argument binding after construction.
    [CliOption(Name = "--package-id", Required = true,
        Arity = CliArgumentArity.ExactlyOne)]
    public string PackageId { get; set; } = null!;

    [CliOption(Name = "--delist-all")]
    public bool DelistAllVersions { get; set; } = false;

    [CliOption(Name = "--use-strict-parsing")]
    public bool UseStrictParsing { get; set; } = true;

    /// <summary>
    /// The pre-release versions to delist. Optional when <see cref="DelistAllVersions"/> is set.
    /// </summary>
    [CliArgument(Name = "versions", Required = false)]
    public string[] Versions { get; set; } = [];

    [CliOption(Name = "--api-key", Required = false)]
    [DefaultValue(null)]
    public string? ApiKey { get; set; }

    [CliOption(Name = "--non-interactive", Required = false)]
    [DefaultValue(false)]
    public bool NonInteractive { get; set; } = false;

    [CliOption(Name = "--server-url", Required = false)]
    public string? ServerUrl { get; set; }

    /// <summary>
    /// The backend to use for delisting: "http" (default) or "sdk".
    /// </summary>
    [CliOption(Name = "--backend", Required = false)]
    public string Backend { get; set; } = "http";

    /// <summary>
    /// Includes stable Major==0 versions when using --delist-all-versions.
    /// </summary>
    [CliOption(Name = "--include-zero-major", Required = false)]
    public bool IncludeZeroMajor { get; set; } = false;

    /// <summary>
    /// Prints the delist plan without sending any delete request (T003). No API key is required in
    /// this mode, and it exits only with 0, 1, or 2.
    /// </summary>
    [CliOption(Name = "--dry-run", Required = false)]
    [DefaultValue(false)]
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// The output format: "text" (default) or "json" for newline-delimited JSON on stdout, one object
    /// per requested version (T007).
    /// </summary>
    [CliOption(Name = "--output", Required = false)]
    [DefaultValue(DelistOptions.DefaultOutputMode)]
    public string OutputMode { get; set; } = DelistOptions.DefaultOutputMode;

    public async Task<int> RunAsync()
    {
        // The argument tokenizer turns an unknown option such as --typo into a plain argument token
        // (only a missing required option or a missing option value reaches it as an error), and the
        // greedy versions position absorbs it — where the digit filter in ResolveOptions would drop
        // it silently. Reject option-looking tokens here so a typo fails the run as the usage error
        // it is, instead of vanishing (T005).
        string[] unknownOptions = (Versions ?? [])
            .Where(static version => version.StartsWith('-'))
            .ToArray();

        if (unknownOptions.Length > 0)
        {
            foreach (string unknownOption in unknownOptions)
            {
                await Console.Error.WriteLineAsync($"Error: Unrecognized option: '{unknownOption}'.");
            }

            return ExitUsage;
        }

        DelistOptions options = ResolveOptions();

        IReadOnlyList<DelistOptionsValidationFailure> failures = options.Validate();

        if (failures.Count > 0)
        {
            foreach (DelistOptionsValidationFailure failure in failures)
            {
                await Console.Error.WriteLineAsync(DescribeValidationFailure(failure));
            }

            return ExitUsage;
        }

        using CancellationTokenSource cancellationSource = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            // Resolving the keyed service here proves the composition-root wiring (TK008) before any
            // work starts, for dry runs and real runs alike.
            IPackageDelistService delistService = GetDelistService(options.Backend);

            return options.DryRun
                ? await RunDryRunAsync(options, cancellationSource.Token)
                : await RunDelistAsync(options, delistService, cancellationSource.Token);
        }
        catch (ArgumentException exception)
        {
            // Package-not-found-on-server (thrown by the composing services) and invalid version
            // strings under strict parsing both surface as ArgumentException → usage (T012).
            await Console.Error.WriteLineAsync(exception.Message);
            return ExitUsage;
        }
        catch (NuGetProtocolException exception)
        {
            // NuGet.Protocol failures, including resource unavailability, are feed-side problems → 1 (T012).
            await Console.Error.WriteLineAsync(exception.Message);
            return ExitFailure;
        }
        catch (InvalidOperationException exception)
        {
            // The library reports an unavailable NuGet.Protocol resource with this type → 1 (T012).
            await Console.Error.WriteLineAsync(exception.Message);
            return ExitFailure;
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C cancels the token above; a cancelled run exits 130 (T005).
            return ExitInterrupted;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    /// <summary>
    /// Builds the resolved options from the bound values plus the prefetched configuration strings.
    /// Precedence resolution and mode-aware validation belong to <see cref="DelistOptions"/> (T009).
    /// </summary>
    private DelistOptions ResolveOptions()
    {
        string[] requestedVersions = Versions ?? [];

        if (!DelistAllVersions)
        {
            // Only version strings starting with a digit are considered; the rest are ignored.
            requestedVersions = requestedVersions
                .Where(version => !string.IsNullOrWhiteSpace(version) && char.IsDigit(version.Trim()[0]))
                .ToArray();
        }

        return DelistOptions.Create(
            packageId: PackageId,
            versions: requestedVersions,
            apiKeyOption: ApiKey,
            apiKeyPrefixed: _configuration["NuGetApiKey"],
            apiKeyGeneric: _apiKeyGeneric,
            serverUrlOption: ServerUrl,
            serverUrlPrefixed: _configuration["NuGetServerUrl"],
            serverUrlGeneric: _serverUrlGeneric,
            backend: Backend,
            nonInteractive: NonInteractive,
            includeZeroMajor: IncludeZeroMajor,
            useStrictParsing: UseStrictParsing,
            delistAllVersions: DelistAllVersions,
            dryRun: DryRun,
            outputMode: OutputMode);
    }

    private static string DescribeValidationFailure(DelistOptionsValidationFailure failure)
    {
        // The two failures with an established message keep their localized wording: T006 moves the
        // API-key error to stderr, where it stays one line like every other diagnostic.
        return failure.Field switch
        {
            nameof(DelistOptions.ApiKey) => Resources.Exceptions_Configuration_NugetApiKey,
            nameof(DelistOptions.Versions) => Resources.Errors_Input_NoVersionStrings,
            _ => $"Error: {failure.Message}"
        };
    }

    /// <summary>
    /// Resolves the composing service for the selected backend. Validation has already proven the
    /// backend is one of the two registered keys, so the keys here stay exactly "http" and "sdk".
    /// </summary>
    private IPackageDelistService GetDelistService(string backend)
    {
        string serviceKey = string.Equals(backend, "sdk", StringComparison.OrdinalIgnoreCase)
            ? "sdk"
            : DelistOptions.DefaultBackend;

        return _serviceProvider.GetRequiredKeyedService<IPackageDelistService>(serviceKey);
    }

    /// <summary>
    /// Sends the delete requests through the composing service, renders every outcome, and returns the
    /// merged exit code (T005).
    /// </summary>
    private async Task<int> RunDelistAsync(DelistOptions options, IPackageDelistService delistService,
        CancellationToken cancellationToken)
    {
        IAsyncEnumerable<PackageVersionOutcome> results;

        if (options.DelistAllVersions)
        {
            results = delistService.RequestPackageDelistingAsync(options.ServerUrl, options.ApiKey!,
                options.PackageId, options.IncludeZeroMajor, cancellationToken);
        }
        else
        {
            IList<NuGetVersion> requestedVersions =
                ParseVersions([.. options.Versions], options.UseStrictParsing);

            results = delistService.RequestPackageDelistingAsync(options.ServerUrl, options.ApiKey!,
                options.PackageId, requestedVersions, cancellationToken);
        }

        List<PackageVersionOutcome> outcomes = [];
        int exitCode = ExitSuccess;

        await foreach (PackageVersionOutcome outcome in results)
        {
            outcomes.Add(outcome);
            exitCode = MergeExitCode(exitCode, ToExitCode(outcome.Status));
        }

        await WriteResultsAsync(options, outcomes);

        return exitCode;
    }

    /// <summary>
    /// Resolves and prints the delist plan without sending any delete request, then exits bucket-aware:
    /// 0 only when every requested version exists on the server, 1 when any is absent, 2 when the
    /// package itself does not exist (T003). Dry runs never emit 3 or 4.
    /// </summary>
    private async Task<int> RunDryRunAsync(DelistOptions options, CancellationToken cancellationToken)
    {
        IPackageAvailabilityDetector availabilityDetector =
            _serviceProvider.GetRequiredService<IPackageAvailabilityDetector>();
        IPackageVersionService versionService =
            _serviceProvider.GetRequiredService<IPackageVersionService>();

        // Network reads still happen (T003). The read paths require a non-empty API key string but
        // never send it, so a keyless dry-run passes a placeholder; no key is ever written to a stream.
        string readApiKey = options.ApiKey ?? "dry-run";

        bool packageExists = await availabilityDetector.CheckPackageExistsAsync(options.ServerUrl,
            options.PackageId, cancellationToken);

        if (!packageExists)
        {
            await Console.Error.WriteLineAsync(
                $"Package '{options.PackageId}' does not exist on NuGet server '{options.ServerUrl}'.");
            return ExitUsage;
        }

        IList<NuGetVersion> requestedVersions = options.DelistAllVersions
            ? await versionService.GetPrereleasePackageVersionsAsync(options.ServerUrl, readApiKey,
                options.PackageId, options.IncludeZeroMajor, cancellationToken)
            : ParseVersions([.. options.Versions], options.UseStrictParsing);

        IDictionary<NuGetVersion, PackageVersionListingInfo> listingInfo =
            await versionService.CheckPackageVersionsListedAsync(options.ServerUrl, readApiKey,
                options.PackageId, includePreReleaseVersions: true, requestedVersions, cancellationToken);

        List<DryRunPlanLine> plan = [];

        foreach (NuGetVersion version in requestedVersions.Distinct())
        {
            // The same partition rule the composing services apply, resolved from read-only listing
            // metadata: absent from the server, present but unlisted, or listed and to be delisted.
            DryRunBucket bucket;

            if (!listingInfo.TryGetValue(version, out PackageVersionListingInfo? listing)
                || !listing.PackageVersionExists)
            {
                bucket = DryRunBucket.NotOnServer;
            }
            else
            {
                bucket = listing.IsListed ? DryRunBucket.WouldDelist : DryRunBucket.AlreadyDelisted;
            }

            plan.Add(new DryRunPlanLine(version, bucket));
        }

        if (IsJsonOutput(options))
        {
            foreach (DryRunPlanLine line in plan)
            {
                await Console.Out.WriteLineAsync(ToJsonPropertyLine(options.PackageId, line.Version,
                    ToBucketStatus(line.Bucket)));
            }
        }
        else
        {
            foreach (DryRunPlanLine line in plan)
            {
                await Console.Out.WriteLineAsync(
                    $"Version={line.Version.ToNormalizedString()} Bucket={DescribeBucket(line.Bucket)}");
            }
        }

        return plan.Any(line => line.Bucket == DryRunBucket.NotOnServer) ? ExitFailure : ExitSuccess;
    }

    /// <summary>
    /// Renders every outcome to stdout: JSON lines, bare non-interactive lines, or grouped human
    /// output (T006, T007).
    /// </summary>
    private static async Task WriteResultsAsync(DelistOptions options,
        IReadOnlyList<PackageVersionOutcome> outcomes)
    {
        if (IsJsonOutput(options))
        {
            foreach (PackageVersionOutcome outcome in outcomes)
            {
                await Console.Out.WriteLineAsync(
                    ToJsonPropertyLine(outcome.PackageId, outcome.Version, outcome.Status));
            }

            return;
        }

        if (options.NonInteractive)
        {
            foreach (PackageVersionOutcome outcome in outcomes)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome));
            }

            return;
        }

        List<PackageVersionOutcome> delisted = [.. outcomes.Where(IsDelistedOutcome)];
        List<PackageVersionOutcome> notDelisted = [.. outcomes.Where(outcome => !IsDelistedOutcome(outcome))];

        if (delisted.Count > 0)
        {
            await Console.Out.WriteLineAsync($"Versions Delisted for Package: {options.PackageId}");

            foreach (PackageVersionOutcome outcome in delisted)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome));
            }
        }

        if (notDelisted.Count > 0)
        {
            await Console.Out.WriteLineAsync(
                $"The following versions of {options.PackageId} could not be delisted:");

            foreach (PackageVersionOutcome outcome in notDelisted)
            {
                await Console.Out.WriteLineAsync(DescribeOutcome(outcome));
            }
        }
    }

    private static bool IsDelistedOutcome(PackageVersionOutcome outcome) =>
        outcome.Status is PackageVersionStatus.Delisted or PackageVersionStatus.AlreadyDelisted;

    /// <summary>
    /// The machine line shared by the human and non-interactive text formats (T004, T018).
    /// </summary>
    private static string DescribeOutcome(PackageVersionOutcome outcome) =>
        $"Version={outcome.Version.ToNormalizedString()} Status={outcome.Status.ToKebabCase()}";

    private static bool IsJsonOutput(DelistOptions options) =>
        string.Equals(options.OutputMode, "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One NDJSON line with exactly three fields in the fixed order package, version, status — no
    /// envelope, no array wrapper, no additional fields (T007). The version is the normalized string
    /// and the package id is carried exactly as supplied.
    /// </summary>
    private static string ToJsonPropertyLine(string packageId, NuGetVersion version,
        PackageVersionStatus status)
    {
        string package = JsonSerializer.Serialize(packageId);
        string normalizedVersion = JsonSerializer.Serialize(version.ToNormalizedString());
        string statusText = JsonSerializer.Serialize(status.ToKebabCase());

        return $"{{\"package\":{package},\"version\":{normalizedVersion},\"status\":{statusText}}}";
    }

    /// <summary>
    /// Maps a per-version outcome to its documented exit code. Every member of the closed vocabulary
    /// is handled explicitly (T004, T005): a member added to the enum reaches the throwing catch-all
    /// arm — C# forces that arm on an enum switch expression (CS8524) — and fails loudly here and in
    /// <c>ToKebabCase</c> until its exit code is decided (T018).
    /// </summary>
    internal static int ToExitCode(PackageVersionStatus status) => status switch
    {
        PackageVersionStatus.Delisted => ExitSuccess,
        PackageVersionStatus.AlreadyDelisted => ExitSuccess,
        PackageVersionStatus.NotOnServer => ExitFailure,
        PackageVersionStatus.RateLimited => ExitRateLimited,
        PackageVersionStatus.Failed => ExitFailure,
        PackageVersionStatus.NotAttempted => ExitCancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status,
            $"Unhandled {nameof(PackageVersionStatus)} member: decide its exit code in {nameof(ToExitCode)}.")
    };

    /// <summary>
    /// Folds one outcome's code into the run's code. Rate-limited outranks cancelled, which outranks
    /// per-version failure: a rate-limit is the root cause of the NotAttempted outcomes behind it.
    /// </summary>
    private static int MergeExitCode(int current, int candidate)
    {
        if (current == ExitRateLimited || candidate == ExitRateLimited)
        {
            return ExitRateLimited;
        }

        if (current == ExitCancelled || candidate == ExitCancelled)
        {
            return ExitCancelled;
        }

        if (current == ExitFailure || candidate == ExitFailure)
        {
            return ExitFailure;
        }

        return ExitSuccess;
    }

    /// <summary>
    /// One line of the dry-run plan: a requested version and the bucket it landed in.
    /// </summary>
    private sealed record DryRunPlanLine(NuGetVersion Version, DryRunBucket Bucket);

    private enum DryRunBucket
    {
        WouldDelist,
        AlreadyDelisted,
        NotOnServer
    }

    private static string DescribeBucket(DryRunBucket bucket) => bucket switch
    {
        DryRunBucket.WouldDelist => "would-delist",
        DryRunBucket.AlreadyDelisted => "already-delisted",
        DryRunBucket.NotOnServer => "not-on-server",
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket,
            $"Unhandled {nameof(DryRunBucket)} member.")
    };

    /// <summary>
    /// The JSON rendering of a bucket uses only the closed status vocabulary: a version a dry run
    /// would delist is a delete attempt that was never made (T004, T007).
    /// </summary>
    private static PackageVersionStatus ToBucketStatus(DryRunBucket bucket) => bucket switch
    {
        DryRunBucket.WouldDelist => PackageVersionStatus.NotAttempted,
        DryRunBucket.AlreadyDelisted => PackageVersionStatus.AlreadyDelisted,
        DryRunBucket.NotOnServer => PackageVersionStatus.NotOnServer,
        _ => throw new ArgumentOutOfRangeException(nameof(bucket), bucket,
            $"Unhandled {nameof(DryRunBucket)} member.")
    };

    internal static IList<NuGetVersion> ParseVersions(string[] versions, bool throwOnError)
    {
        List<NuGetVersion> output = new(capacity: versions.Length);

        foreach (string versionString in versions)
        {
            bool success = NuGetVersion.TryParse(versionString, out NuGetVersion? version);

            if (success && version is not null)
            {
                output.Add(version);
            }
            else
            {
                if(throwOnError)
                    throw new ArgumentException("Invalid version string: " + versionString);
            }
        }

        return output.Distinct().ToList();
    }
}
