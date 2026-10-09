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
using PreReleaseDelistLib.Abstractions;

namespace PreReleaseDelistLib.Tests;

/// <summary>One request as observed by <see cref="ScriptedHttpMessageHandler"/>.</summary>
internal sealed record DispatchedRequest(string Method, string RequestUri);

/// <summary>
/// An <see cref="HttpMessageHandler"/> replaying a scripted queue of status codes (or thrown
/// exceptions) and recording every request the production code dispatches through it, so tests can
/// prove both which status was translated and how many requests were actually sent.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<object> _script = new();

    public List<DispatchedRequest> DispatchedRequests { get; } = [];

    public int DispatchCount => DispatchedRequests.Count;

    public void EnqueueStatusCode(HttpStatusCode statusCode) => _script.Enqueue(statusCode);

    public void EnqueueException(Exception exception) => _script.Enqueue(exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        DispatchedRequests.Add(new DispatchedRequest(request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty));

        if (_script.Count == 0)
        {
            throw new InvalidOperationException(
                $"No scripted response for {request.Method} {request.RequestUri}.");
        }

        object next = _script.Dequeue();

        if (next is Exception exception)
        {
            throw exception;
        }

        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)next));
    }
}

/// <summary>
/// An <see cref="IHttpClientFactory"/> handing out fresh clients over the shared stubbed handler.
/// </summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient(handler, disposeHandler: false);
}

/// <summary>
/// A loopback-only HTTP server that serves the NuGet v3 service index.
/// </summary>
/// <remarks>
/// <see cref="HttpPackageVersionDeleter"/> resolves the service index through NuGet.Protocol's own
/// internal HTTP stack — not through the injected client factory — so handler-stub tests need a
/// real (but strictly local) listener for that one GET. Only loopback addresses are ever bound;
/// no external network is touched.
/// </remarks>
internal sealed class LoopbackServiceIndexServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly bool _includePublishEntry;
    private Task _serveLoop = Task.CompletedTask;

    private LoopbackServiceIndexServer(HttpListener listener, int port, bool includePublishEntry)
    {
        _listener = listener;
        _includePublishEntry = includePublishEntry;
        ServiceIndexUrl = $"http://127.0.0.1:{port}/index.json";
        PublishUrl = $"http://127.0.0.1:{port}/publish/";
    }

    /// <summary>The URL to pass as the NuGet API (service index) endpoint.</summary>
    public string ServiceIndexUrl { get; }

    /// <summary>The publish base URL advertised inside the served service index.</summary>
    public string PublishUrl { get; }

    /// <summary>
    /// Starts the server. With <see paramref="includePublishEntry"/> false the served index
    /// advertises no <c>PackagePublish/2.0.0</c> entry, forcing the backend's fallback route.
    /// </summary>
    public static LoopbackServiceIndexServer Start(bool includePublishEntry = true)
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

            LoopbackServiceIndexServer server = new(listener, port, includePublishEntry);
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

    /// <summary>Reserves an ephemeral loopback port so the listener can bind a predictable URL.</summary>
    private static int ReserveFreeLoopbackPort()
    {
        TcpListener reservation = new(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        return port;
    }

    private async Task ServeAsync()
    {
        string body = _includePublishEntry
            ? $"{{\"version\":\"3.0.0\",\"resources\":[{{\"@id\":\"{PublishUrl}\",\"@type\":\"PackagePublish/2.0.0\"}}]}}"
            : "{\"version\":\"3.0.0\",\"resources\":[]}";
        byte[] payload = Encoding.UTF8.GetBytes(body);

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

            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload);
                context.Response.Close();
            }
            catch (Exception)
            {
                // The client vanished mid-response; the next request still gets served. A genuinely
                // broken index surfaces in the tests as a wrong outcome status, never silently.
            }
        }
    }
}
