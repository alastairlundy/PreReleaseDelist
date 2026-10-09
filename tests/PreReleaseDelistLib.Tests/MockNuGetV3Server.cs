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
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using NuGet.Versioning;

namespace PreReleaseDelistLib.Tests;

/// <summary>One request as observed by <see cref="MockNuGetV3Server"/>.</summary>
/// <param name="Method">The HTTP method, e.g. <c>GET</c> or <c>DELETE</c>.</param>
/// <param name="Path">The absolute path portion of the request URL.</param>
/// <param name="ApiKey">The <c>X-NuGet-ApiKey</c> header, when the client sent one.</param>
internal sealed record RecordedRequest(string Method, string Path, string? ApiKey);

/// <summary>
/// A loopback-only NuGet V3 mock server: service index, flat-container version list, registration
/// index, and delete endpoint, plus a server-side record of every request it received.
/// </summary>
/// <remarks>
/// <para>
/// The CLI integration tier drives the production composition against this server instead of a real
/// feed, so the whole pyramid runs in-process with no external network (T008). The four resources
/// above are exactly the ones NuGet.Protocol resolves for this feature: <c>PackageBaseAddress</c> for
/// the availability check, <c>RegistrationsBaseUrl</c> for listing metadata, and
/// <c>PackagePublish</c> for the DELETE dispatch; the service index binds them together.
/// </para>
/// <para>
/// Every delete the CLI sends is recorded (<see cref="DeleteRequests"/>), which is what makes
/// "a dry run sends no delete request" and "the fail-fast rule stopped after the first 429"
/// assertable from the server side rather than from client-side bookkeeping.
/// </para>
/// </remarks>
internal sealed class MockNuGetV3Server : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, List<MockPackageVersion>> _packages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _deleteResponseStatuses =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();
    private readonly string _baseUrl;
    private Task _serveLoop = Task.CompletedTask;

    private MockNuGetV3Server(HttpListener listener, int port)
    {
        _listener = listener;
        _baseUrl = $"http://127.0.0.1:{port}/";
        ServiceIndexUrl = $"{_baseUrl}index.json";
    }

    /// <summary>The URL to pass as the NuGet API (service index) endpoint.</summary>
    public string ServiceIndexUrl { get; }

    /// <summary>Every request the server received, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>The delete requests the server received, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> DeleteRequests
        => [.. Requests.Where(static request => request.Method == "DELETE")];

    /// <summary>Registers a package with the given versions and their listed state.</summary>
    public void AddPackage(string packageId, params (string Version, bool Listed)[] versions)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);

        lock (_gate)
        {
            _packages[packageId] =
            [
                .. versions.Select(static version =>
                    new MockPackageVersion(NuGetVersion.Parse(version.Version), version.Listed))
            ];
        }
    }

    /// <summary>
    /// Sets the status code the delete endpoint answers for one version; every other version keeps
    /// the default 204 (delisted).
    /// </summary>
    public void SetDeleteResponseStatus(string version, int statusCode)
    {
        NuGetVersion parsed = NuGetVersion.Parse(version);

        lock (_gate)
        {
            _deleteResponseStatuses[parsed.ToNormalizedString()] = statusCode;
        }
    }

    public static MockNuGetV3Server Start()
    {
        for (int attempt = 1; ; attempt++)
        {
            int port = ReserveFreeLoopbackPort();
            HttpListener listener = new();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException) when (attempt < 3)
            {
                // The ephemeral port raced away between reserving and binding; take a new one.
                listener.Close();
                continue;
            }

            MockNuGetV3Server server = new(listener, port);
            server._serveLoop = Task.Run(server.ServeAsync);
            return server;
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();

        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already closed by a failed serve loop; nothing left to stop.
        }

        try
        {
            _serveLoop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The loop exits via exception when the listener is stopped underneath it; the test
            // only cares that the port is released, which Stop() above already guaranteed.
        }

        _listener.Close();
        _stopping.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stopping.IsCancellationRequested)
            {
                // Stop() cancelled the pending GetContextAsync; shut the loop down quietly.
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await RespondAsync(context);
            }
            catch (Exception)
            {
                // The client vanished mid-response; the next request is still served, and a genuinely
                // broken answer surfaces in the tests as a wrong outcome status, never silently.
            }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        string method = context.Request.HttpMethod;
        string path = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? "/");
        string? apiKey = context.Request.Headers["X-NuGet-ApiKey"];

        lock (_gate)
        {
            _requests.Add(new RecordedRequest(method, path, apiKey));
        }

        int statusCode = (int)HttpStatusCode.OK;
        string? body = Route(method, path, ref statusCode);

        byte[] payload = body is null ? [] : Encoding.UTF8.GetBytes(body);

        context.Response.StatusCode = (int)statusCode;

        if (payload.Length > 0)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload);
        }
        else
        {
            context.Response.ContentLength64 = 0;
        }

        context.Response.Close();
    }

    private string? Route(string method, string path, ref int statusCode)
    {
        lock (_gate)
        {
            if (method == "GET" && (path == "/" || path == "/index.json"))
            {
                return BuildServiceIndex();
            }

            if (method == "GET" && TryTakeSegment(path, prefix: "/flat/", suffix: "/index.json",
                    out string flatId))
            {
                if (!_packages.TryGetValue(flatId, out List<MockPackageVersion>? flatVersions))
                {
                    statusCode = (int)HttpStatusCode.NotFound;
                    return null;
                }

                return new JsonObject
                {
                    ["versions"] = new JsonArray(
                        [.. flatVersions.Select(static version =>
                            (JsonNode?)JsonValue.Create(version.Version.ToNormalizedString()))])
                }.ToJsonString();
            }

            if (method == "GET" && TryTakeSegment(path, prefix: "/registration/",
                    suffix: "/index.json", out string registrationId))
            {
                if (!_packages.TryGetValue(registrationId,
                        out List<MockPackageVersion>? registrationVersions))
                {
                    statusCode = (int)HttpStatusCode.NotFound;
                    return null;
                }

                return BuildRegistrationIndex(registrationId, registrationVersions);
            }

            if (method == "DELETE" && TryTakeSegment(path, prefix: "/publish/", suffix: null,
                    out string publishTarget))
            {
                // The delete path is "<package id>/<normalized version>" under the publish URL.
                int separator = publishTarget.LastIndexOf('/');
                string version = separator < 0
                    ? publishTarget
                    : publishTarget[(separator + 1)..];

                statusCode = _deleteResponseStatuses.TryGetValue(version, out int configured)
                    ? configured
                    : (int)HttpStatusCode.NoContent;
                return null;
            }

            statusCode = (int)HttpStatusCode.NotFound;
            return null;
        }
    }

    private string BuildServiceIndex()
    {
        JsonObject Resource(string id, string type) => new()
        {
            ["@id"] = id,
            ["@type"] = type
        };

        // Only the resources this feature resolves are advertised; NuGet.Protocol tolerates the rest
        // of the V3 surface being absent because each resource provider treats its entry as optional.
        return new JsonObject
        {
            ["version"] = "3.0.0",
            ["resources"] = new JsonArray(
                Resource($"{_baseUrl}flat/", "PackageBaseAddress/3.0.0"),
                Resource($"{_baseUrl}registration/", "RegistrationsBaseUrl/3.6.0"),
                Resource($"{_baseUrl}publish/", "PackagePublish/2.0.0"))
        }.ToJsonString();
    }

    private string BuildRegistrationIndex(string packageId, List<MockPackageVersion> versions)
    {
        string idLower = packageId.ToLowerInvariant();
        string LeafUrl(string version) => $"{_baseUrl}registration/{idLower}/{version}.json";

        JsonArray leaves =
            [.. versions.Select(version => (JsonNode?)new JsonObject
            {
                ["@id"] = LeafUrl(version.Version.ToNormalizedString()),
                ["packageContent"] =
                    $"{_baseUrl}flat/{idLower}/{version.Version.ToNormalizedString()}/" +
                    $"{idLower}.{version.Version.ToNormalizedString()}.nupkg",
                ["catalogEntry"] = new JsonObject
                {
                    ["@id"] = LeafUrl(version.Version.ToNormalizedString()),
                    // "id", "version" and "listed" are the fields the production code reads: package
                    // identity, the version key of the listing lookup, and the bucket decision.
                    ["id"] = packageId,
                    ["version"] = version.Version.ToNormalizedString(),
                    ["listed"] = version.Listed
                }
            })];

        // One page with every leaf inlined; the alternative (a page whose "items" is null, fetched
        // from the page "@id") needs a second endpoint this mock deliberately does not serve.
        JsonObject page = new()
        {
            ["@id"] = $"{_baseUrl}registration/{idLower}/index.json",
            ["lower"] = versions.Select(static version => version.Version).Min()!.ToNormalizedString(),
            ["upper"] = versions.Select(static version => version.Version).Max()!.ToNormalizedString(),
            ["count"] = versions.Count,
            ["items"] = leaves
        };

        return new JsonObject
        {
            ["@id"] = $"{_baseUrl}registration/{idLower}/index.json",
            ["items"] = new JsonArray(page)
        }.ToJsonString();
    }

    /// <summary>
    /// Splits <c>&lt;prefix&gt;&lt;value&gt;&lt;suffix&gt;</c>; <see langword="null"/> suffix means
    /// "everything after the prefix".
    /// </summary>
    private static bool TryTakeSegment(string path, string prefix, string? suffix, out string value)
    {
        value = string.Empty;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = path[prefix.Length..];

        if (suffix is null)
        {
            value = rest;
            return value.Length > 0;
        }

        if (!rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = rest[..(rest.Length - suffix.Length)];
        return value.Length > 0;
    }

    /// <summary>Reserves an ephemeral loopback port so the listener can bind a predictable URL.</summary>
    private static int ReserveFreeLoopbackPort()
    {
        TcpListener reservation = new(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        return port;
    }

    /// <summary>One version the mock serves, as recorded by <see cref="AddPackage"/>.</summary>
    private sealed record MockPackageVersion(NuGetVersion Version, bool Listed);
}
