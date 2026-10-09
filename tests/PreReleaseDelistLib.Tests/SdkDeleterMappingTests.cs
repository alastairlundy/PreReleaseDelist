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

using CliInvoke.Core;
using NuGet.Versioning;
using PreReleaseDelistLib;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Status classification in the SDK delete backend: exit code, the best-effort output patterns, and
/// the boundary between a transient-process failure and a cancellation the caller requested.
/// </summary>
public class SdkDeleterMappingTests
{
    private const string ServerUrl = "https://example.test/v3/index.json";

    private const string PackageId = "Demo.Package";

    private const string ApiKey = "unit-test-api-key";

    [Test]
    public async Task DeleteAsync_WithExitCodeZero_YieldsDelisted()
    {
        ScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(0, "", ""));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Delisted);
        await Assert.That(invoker.BufferedCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteAsync_WithAStandalone429_YieldsRateLimited()
    {
        ScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "",
            "One or more errors occurred. (Response status code does not indicate success: 429 (Too Many Requests).)"));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.RateLimited);
    }

    [Test]
    public async Task DeleteAsync_With429OnlyInsideAVersionNumeral_YieldsFailed()
    {
        // A "429" inside a version string is not a rate-limit signal: a version numeral in
        // unexpected-failure output must keep the outcome a plain failure.
        ScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "", "Unexpected error for version 2.429.0 on the server."));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
    }

    [Test]
    public async Task DeleteAsync_WithNotListedOutput_YieldsAlreadyDelisted()
    {
        ScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "Package 'Foo 1.0.0' is not listed on the server.", ""));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.AlreadyDelisted);
    }

    [Test]
    public async Task DeleteAsync_WithUnrecognizedNonZeroExit_YieldsFailed()
    {
        ScriptedProcessInvoker invoker = new();
        invoker.Enqueue(MakeResult(1, "", "Something went wrong on the server."));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(invoker);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
    }

    [Test]
    public async Task DeleteAsync_WhenTheProcessInvokerReportsCancellation_LetsTheCancellationEscape()
    {
        // CliInvoke throws OperationCanceledException when the caller's token cancels a run (verified
        // against CliInvoke 3.0.2, both mid-run and pre-cancelled). The backend must let that
        // cancellation escape so the composing service reports the run as interrupted, not Failed.
        ScriptedProcessInvoker invoker = new();
        invoker.EnqueueException(new OperationCanceledException("simulated Ctrl-C during process run"));
        CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        await Assert.That(async () => await new SdkPackageVersionDeleter(invoker).DeleteAsync(
                ServerUrl, PackageId, NuGetVersion.Parse("1.0.0"), ApiKey,
                cancelled.Token))
            .ThrowsExactly<OperationCanceledException>();
        await Assert.That(invoker.BufferedCallCount).IsEqualTo(1);
    }

    private static async Task<PackageVersionOutcome> DeleteThroughStubAsync(
        ScriptedProcessInvoker invoker, CancellationToken cancellationToken = default)
    {
        SdkPackageVersionDeleter deleter = new(invoker);

        return await deleter.DeleteAsync(ServerUrl, PackageId,
            NuGetVersion.Parse("1.0.0"), ApiKey, cancellationToken);
    }

    private static BufferedProcessResult MakeResult(int exitCode, string standardOutput,
        string standardError)
        => new("dotnet", exitCode, processId: 1, standardOutput, standardError,
            DateTime.UtcNow, DateTime.UtcNow, canceled: false, signal: null);
}

/// <summary>
/// An <see cref="IProcessInvoker"/> replaying a scripted queue of buffered results and recording
/// every configuration it was given; with an empty script it throws so tests can prove no call
/// reached the seam.
/// </summary>
internal sealed class ScriptedProcessInvoker : IProcessInvoker
{
    private readonly Queue<BufferedProcessResult> _results = new();
    private readonly Queue<Exception> _exceptions = [];

    public int BufferedCallCount { get; private set; }

    public void Enqueue(BufferedProcessResult result) => _results.Enqueue(result);

    public void EnqueueException(Exception exception) => _exceptions.Enqueue(exception);

    public Task<BufferedProcessResult> ExecuteBufferedAsync(ProcessConfiguration processConfiguration,
        ProcessExitConfiguration? processExitConfiguration, CancellationToken cancellationToken = default)
    {
        BufferedCallCount++;

        if (_exceptions.Count > 0)
        {
            throw _exceptions.Dequeue();
        }

        if (_results.Count == 0)
        {
            throw new InvalidOperationException(
                $"No scripted result left for '{processConfiguration.TargetFilePath}'.");
        }

        return Task.FromResult(_results.Dequeue());
    }

    public Task<ProcessResult> ExecuteAsync(ProcessConfiguration processConfiguration,
        ProcessExitConfiguration? processExitConfiguration, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("ScriptedProcessInvoker only scripts buffered runs.");
}
