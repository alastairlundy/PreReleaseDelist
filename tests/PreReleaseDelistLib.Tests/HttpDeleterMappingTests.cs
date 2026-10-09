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

    /// <summary>Runs one delete through the stubbed handler against the loopback service index.</summary>
    private static async Task<PackageVersionOutcome> DeleteThroughStubAsync(
        LoopbackServiceIndexServer server, ScriptedHttpMessageHandler handler)
    {
        HttpPackageVersionDeleter deleter = new(new StubHttpClientFactory(handler));

        return await deleter.DeleteAsync(server.ServiceIndexUrl, PackageId,
            NuGetVersion.Parse("1.0.0"), ApiKey);
    }
}
