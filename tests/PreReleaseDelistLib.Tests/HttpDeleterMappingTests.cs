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

using System.Net;
using NuGet.Versioning;
using PreReleaseDelistLib;
using PreReleaseDelistLib.Models;
using TUnit.Core;

namespace PreReleaseDelistLib.Tests;

/// <summary>
/// Status translation in the HTTP delete backend (T002): every response the stub replays maps to
/// exactly one closed <see cref="PackageVersionStatus"/> member, with no external network — the
/// service index is served from a loopback listener and the DELETE itself goes through the stub.
/// </summary>
public class HttpDeleterMappingTests
{
    private const string PackageId = "Demo.Package";

    private const string ApiKey = "unit-test-api-key";

    [Test]
    [Arguments(200)]
    [Arguments(204)]
    [Arguments(201)]
    public async Task DeleteAsync_WithAnySuccessStatusCode_YieldsDelisted(int statusCode)
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueStatusCode((HttpStatusCode)statusCode);

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Delisted);
        await Assert.That(outcome.PackageId).IsEqualTo(PackageId);
        await Assert.That(outcome.Version.ToNormalizedString()).IsEqualTo("1.0.0");
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteAsync_WithNotFound_YieldsAlreadyDelisted()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueStatusCode(HttpStatusCode.NotFound);

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.AlreadyDelisted);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
        // The delete went to the publish URL advertised by the loopback service index, not to the
        // service index itself.
        await Assert.That(handler.DispatchedRequests[0].RequestUri)
            .IsEqualTo($"{server.PublishUrl}{PackageId}/1.0.0");
        await Assert.That(handler.DispatchedRequests[0].Method).IsEqualTo("DELETE");
    }

    [Test]
    public async Task DeleteAsync_WithNotFoundOnTheFallbackRoute_YieldsFailed()
    {
        // Without a PackagePublish/2.0.0 entry in the service index the delete goes to the raw
        // server URL; a 404 from that route says nothing about the version's listing state, so it
        // must not be reported as an already-delisted success.
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start(includePublishEntry: false);
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueStatusCode(HttpStatusCode.NotFound);

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchedRequests[0].RequestUri)
            .IsEqualTo($"{server.ServiceIndexUrl}/{PackageId}/1.0.0");
    }

    [Test]
    public async Task DeleteAsync_WithTooManyRequests_YieldsRateLimited()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueStatusCode((HttpStatusCode)429);

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.RateLimited);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteAsync_WithInternalServerError_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueStatusCode(HttpStatusCode.InternalServerError);

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteAsync_WhenTheTransportThrows_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueException(new HttpRequestException("stubbed transport failure"));

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteAsync_WhenTheCallerCancelsMidDispatch_LetsTheCancellationEscape()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        handler.EnqueueException(new TaskCanceledException("simulated Ctrl-C during dispatch"));

        // The caller's token is cancelled before the dispatch: the resulting cancellation must
        // escape the backend so the run can be reported as interrupted (130), not as Failed.
        HttpPackageVersionDeleter deleter = new(new StubHttpClientFactory(handler));

        await Assert.That(async () => await deleter.DeleteAsync(
                server.ServiceIndexUrl, PackageId, NuGetVersion.Parse("1.0.0"), ApiKey,
                new CancellationToken(canceled: true)))
            .Throws<OperationCanceledException>();
        await Assert.That(handler.DispatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task DeleteAsync_WhenTheHandlerThrowsACancellationWithoutTheCallerCancelling_YieldsFailed()
    {
        using LoopbackServiceIndexServer server = LoopbackServiceIndexServer.Start();
        ScriptedHttpMessageHandler handler = new();
        // A TaskCanceledException whose own token is not our caller's token (e.g. the HttpClient
        // timeout) must stay a Failed outcome, not an interruption.
        handler.EnqueueException(new TaskCanceledException());

        PackageVersionOutcome outcome = await DeleteThroughStubAsync(server, handler,
            new CancellationTokenSource().Token);

        await Assert.That(outcome.Status).IsEqualTo(PackageVersionStatus.Failed);
        await Assert.That(handler.DispatchCount).IsEqualTo(1);
    }

    /// <summary>Deletes through the stubbed handler against the loopback service index.</summary>
    private static async Task<PackageVersionOutcome> DeleteThroughStubAsync(
        LoopbackServiceIndexServer server, ScriptedHttpMessageHandler handler,
        CancellationToken cancellationToken = default)
    {
        HttpPackageVersionDeleter deleter = new(new StubHttpClientFactory(handler));

        return await deleter.DeleteAsync(server.ServiceIndexUrl, PackageId,
            NuGetVersion.Parse("1.0.0"), ApiKey, cancellationToken);
    }
}
