// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;
using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class HttpServerWireContractCollection
{
    public const string CollectionName = "HttpServer wire contract";
}

[Collection(HttpServerWireContractCollection.CollectionName)]
public sealed class HttpServerWireContractTests
{
    [Fact]
    public async Task Routes_PreserveMethodStatusContentTypeAndHeaders()
    {
        using SensorFixture fixture = CreateServerFixture();
        await using RunningServer running = StartServer(fixture.Server);
        if (running == null)
            return;

        string sensorPath = "/Sensor?action=Get&id=" + Uri.EscapeDataString(fixture.SensorId);
        (HttpMethod Method, string Path, HttpStatusCode Status, string MediaType, string CacheControl, string Cors, string Allow, string BodyContains)[] cases =
        {
            (HttpMethod.Get, "/", HttpStatusCode.OK, "text/html", null, null, null, "<!doctype html>"),
            (HttpMethod.Get, "/console.css", HttpStatusCode.OK, "text/css", null, null, null, "--bg"),
            (HttpMethod.Get, "/images_icon/cpu.png", HttpStatusCode.OK, "image/png", null, null, null, null),
            (HttpMethod.Get, "/missing.css", HttpStatusCode.NotFound, null, null, null, null, null),
            (HttpMethod.Get, "/images_icon/missing.png", HttpStatusCode.NotFound, null, null, null, null, null),
            (HttpMethod.Get, "/DaTa.JsOn", HttpStatusCode.OK, "application/json", "no-cache", "*", null, "WIRE-PC"),
            (HttpMethod.Get, "/MeTrIcS", HttpStatusCode.OK, "text/plain", "no-cache", "*", null, "lhm_"),
            (HttpMethod.Get, sensorPath.Replace("/Sensor", "/sEnSoR"), HttpStatusCode.OK, "application/json", "no-cache", "*", null, "\"value\":42"),
            (HttpMethod.Get, "/rEsEtAlLmInMaX", HttpStatusCode.MethodNotAllowed, "application/json", "no-cache", "*", "POST", HttpServer.ResetAllMinMaxRequiresPostMessage),
            (HttpMethod.Post, sensorPath, HttpStatusCode.OK, "application/json", null, null, null, "\"result\":\"ok\""),
            (HttpMethod.Post, "/sensor?action=Get&id=" + Uri.EscapeDataString(fixture.SensorId), HttpStatusCode.OK, "application/json", null, null, null, "Invalid URL"),
            (HttpMethod.Put, "/data.json", HttpStatusCode.NotFound, null, null, null, null, null)
        };

        foreach ((HttpMethod method, string path, HttpStatusCode status, string mediaType, string cacheControl, string cors, string allow, string bodyContains) in cases)
        {
            using HttpResponseMessage response = await running.SendAsync(method, path);
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(cacheControl, HeaderValue(response, "Cache-Control"));
            Assert.Equal(cors, HeaderValue(response, "Access-Control-Allow-Origin"));
            Assert.Equal(allow, HeaderValue(response, "Allow"));
            if (bodyContains != null)
                Assert.Contains(bodyContains, body, StringComparison.Ordinal);

            if (string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal("0", HeaderValue(response, "X-archivelength"));
                Assert.Equal("0", HeaderValue(response, "X-timestamps"));
                Assert.Equal("1", HeaderValue(response, "X-lastvalue"));
            }
            else if (method == HttpMethod.Post && path.StartsWith("/Sensor", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Null(HeaderValue(response, "X-archivelength"));
                Assert.Null(HeaderValue(response, "X-timestamps"));
                Assert.Null(HeaderValue(response, "X-lastvalue"));
                Assert.Empty(response.Content.Headers.ContentEncoding);
            }
        }
    }

    [Fact]
    public async Task DataJson_PreservesIdentityAndGzipPayloads()
    {
        using SensorFixture fixture = CreateServerFixture();
        await using RunningServer running = StartServer(fixture.Server);
        if (running == null)
            return;

        using HttpResponseMessage identityResponse = await running.SendAsync(HttpMethod.Get, "/data.json", acceptEncoding: "identity");
        byte[] identity = await identityResponse.Content.ReadAsByteArrayAsync();

        using HttpResponseMessage gzipResponse = await running.SendAsync(HttpMethod.Get, "/data.json", acceptEncoding: "gzip");
        byte[] compressed = await gzipResponse.Content.ReadAsByteArrayAsync();
        byte[] inflated;
        using (var input = new MemoryStream(compressed))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            await gzip.CopyToAsync(output);
            inflated = output.ToArray();
        }

        Assert.Equal(HttpStatusCode.OK, identityResponse.StatusCode);
        Assert.Equal("application/json", identityResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(identity.Length, identityResponse.Content.Headers.ContentLength);
        Assert.Equal("no-cache", HeaderValue(identityResponse, "Cache-Control"));
        Assert.Equal("*", HeaderValue(identityResponse, "Access-Control-Allow-Origin"));
        Assert.Empty(identityResponse.Content.Headers.ContentEncoding);

        Assert.Equal(HttpStatusCode.OK, gzipResponse.StatusCode);
        Assert.Equal("application/json", gzipResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(compressed.Length, gzipResponse.Content.Headers.ContentLength);
        Assert.True(compressed.Length >= 2);
        Assert.Equal((byte)0x1f, compressed[0]);
        Assert.Equal((byte)0x8b, compressed[1]);
        Assert.Equal("gzip", Assert.Single(gzipResponse.Content.Headers.ContentEncoding));
        Assert.Equal("no-cache", HeaderValue(gzipResponse, "Cache-Control"));
        Assert.Equal("*", HeaderValue(gzipResponse, "Access-Control-Allow-Origin"));
        Assert.Equal(identity, inflated);
    }

    [Fact]
    public async Task Mutations_PreservePostOnlyAndOriginPolicy()
    {
        using SensorFixture fixture = CreateServerFixture();
        await using RunningServer running = StartServer(fixture.Server);
        if (running == null)
            return;

        string id = Uri.EscapeDataString(fixture.SensorId);
        using (HttpResponseMessage getSet = await running.SendAsync(HttpMethod.Get, $"/Sensor?action=Set&id={id}&value=55"))
        {
            Assert.Equal(HttpStatusCode.OK, getSet.StatusCode);
            Assert.Contains("Set requires a POST request", await getSet.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, fixture.Control.SetSoftwareCallCount);
        }

        using (HttpResponseMessage getReset = await running.SendAsync(HttpMethod.Get, $"/Sensor?action=ResetMinMax&id={id}"))
        {
            Assert.Equal(HttpStatusCode.OK, getReset.StatusCode);
            Assert.Contains(HttpServer.ResetMinMaxRequiresPostMessage, await getReset.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, fixture.Sensor.ResetMinCallCount);
            Assert.Equal(0, fixture.Sensor.ResetMaxCallCount);
        }

        using (HttpResponseMessage rejectedSet = await running.SendAsync(HttpMethod.Post, $"/Sensor?action=Set&id={id}&value=55", origin: "http://evil.test"))
        {
            Assert.Equal(HttpStatusCode.OK, rejectedSet.StatusCode);
            Assert.Contains("cross-origin browser requests are not allowed", await rejectedSet.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, fixture.Control.SetSoftwareCallCount);
        }

        using (HttpResponseMessage sameOriginSet = await running.SendAsync(HttpMethod.Post, $"/Sensor?action=Set&id={id}&value=125", origin: running.Origin))
        {
            Assert.Equal(HttpStatusCode.OK, sameOriginSet.StatusCode);
            Assert.Contains("\"result\":\"ok\"", await sameOriginSet.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(90f, fixture.Control.SoftwareValue);
            Assert.Equal(1, fixture.Control.SetSoftwareCallCount);
        }

        using (HttpResponseMessage headerlessSet = await running.SendAsync(HttpMethod.Post, $"/Sensor?action=Set&id={id}&value=-10"))
        {
            Assert.Equal(HttpStatusCode.OK, headerlessSet.StatusCode);
            Assert.Contains("\"result\":\"ok\"", await headerlessSet.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(10f, fixture.Control.SoftwareValue);
            Assert.Equal(2, fixture.Control.SetSoftwareCallCount);
        }

        using (HttpResponseMessage rejectedResetAll = await running.SendAsync(HttpMethod.Post, "/ResetAllMinMax", origin: "http://evil.test"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rejectedResetAll.StatusCode);
            Assert.Contains(HttpServer.CrossOriginResetMessage, await rejectedResetAll.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(0, fixture.Sensor.ResetMinCallCount);
            Assert.Equal(0, fixture.Sensor.ResetMaxCallCount);
        }

        using (HttpResponseMessage allowedResetAll = await running.SendAsync(HttpMethod.Post, "/ResetAllMinMax", origin: running.Origin))
        {
            Assert.Equal(HttpStatusCode.OK, allowedResetAll.StatusCode);
            Assert.Equal("application/json", allowedResetAll.Content.Headers.ContentType?.MediaType);
            Assert.Contains("WIRE-PC", await allowedResetAll.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(1, fixture.Sensor.ResetMinCallCount);
            Assert.Equal(1, fixture.Sensor.ResetMaxCallCount);
        }

        using HttpResponseMessage getResetAll = await running.SendAsync(HttpMethod.Get, "/ResetAllMinMax");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getResetAll.StatusCode);
        Assert.Equal("POST", HeaderValue(getResetAll, "Allow"));
    }

    [Fact]
    public async Task BasicAuthentication_PreservesFailureAndSuccessResponses()
    {
        const string unauthorizedHtml = "<HTML><HEAD><TITLE>401 Unauthorized</TITLE></HEAD>\r\n  <BODY><H4>401 Unauthorized</H4>\r\n  Authorization required.</BODY></HTML> ";
        using SensorFixture fixture = CreateServerFixture(authEnabled: true, userName: "wire-user");
        fixture.Server.SetPassword("wire-password");
        await using RunningServer running = StartServer(fixture.Server);
        if (running == null)
            return;

        using (HttpResponseMessage absent = await running.SendAsync(HttpMethod.Get, "/data.json"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, absent.StatusCode);
            Assert.Contains(absent.Headers.WwwAuthenticate, value => string.Equals(value.Scheme, "Basic", StringComparison.OrdinalIgnoreCase));
        }

        using (HttpResponseMessage wrongUser = await running.SendAsync(HttpMethod.Get, "/data.json", basicCredentials: ("other-user", "wire-password")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongUser.StatusCode);
            Assert.Equal("text/html", wrongUser.Content.Headers.ContentType?.MediaType);
            Assert.Equal(unauthorizedHtml, await wrongUser.Content.ReadAsStringAsync());
        }

        using (HttpResponseMessage wrongPassword = await running.SendAsync(HttpMethod.Get, "/data.json", basicCredentials: ("wire-user", "wrong-password")))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
            Assert.Equal("text/html", wrongPassword.Content.Headers.ContentType?.MediaType);
            Assert.Equal(unauthorizedHtml, await wrongPassword.Content.ReadAsStringAsync());
        }

        using HttpResponseMessage accepted = await running.SendAsync(HttpMethod.Get, "/data.json", basicCredentials: ("wire-user", "wire-password"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains("WIRE-PC", await accepted.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchFailure_Returns500AndClosesResponse()
    {
        int port = ReserveLoopbackPort();
        var server = new HttpServer(null, null, "127.0.0.1", port);
        await using RunningServer running = StartServer(server);
        if (running == null)
            return;

        using HttpResponseMessage response = await running.SendAsync(HttpMethod.Get, "/data.json");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", HeaderValue(response, "Cache-Control"));
        Assert.Equal("*", HeaderValue(response, "Access-Control-Allow-Origin"));
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ListenerLifecycle_StartStopAndRestartRemainIdempotent()
    {
        int port = ReserveLoopbackPort();
        using SensorFixture fixture = CreateServerFixture(port: port);
        if (fixture.Server.PlatformNotSupported)
            return;

        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            UseProxy = false
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            Assert.True(fixture.Server.StartHttpListener());
            Assert.True(fixture.Server.StartHttpListener());
            using (HttpResponseMessage first = await client.GetAsync("data.json"))
                Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            Assert.True(await fixture.Server.StopHttpListenerAsync());
            Assert.True(await fixture.Server.StopHttpListenerAsync());

            Assert.True(fixture.Server.StartHttpListener());
            using (HttpResponseMessage restarted = await client.GetAsync("data.json"))
                Assert.Equal(HttpStatusCode.OK, restarted.StatusCode);
            Assert.True(fixture.Server.StopHttpListener());
        }
        finally
        {
            await fixture.Server.QuitAsync();
        }
    }

    private static string HeaderValue(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out IEnumerable<string> responseValues))
            return string.Join(",", responseValues);
        if (response.Content.Headers.TryGetValues(name, out IEnumerable<string> contentValues))
            return string.Join(",", contentValues);
        return null;
    }

    private static RunningServer StartServer(HttpServer server)
    {
        if (server.PlatformNotSupported)
            return null;

        Assert.True(server.StartHttpListener());
        return new RunningServer(server);
    }

    private static int ReserveLoopbackPort()
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        try
        {
            return ((IPEndPoint)reservation.LocalEndpoint).Port;
        }
        finally
        {
            reservation.Stop();
        }
    }

    private static SensorFixture CreateServerFixture(int? port = null, bool authEnabled = false, string userName = "")
    {
        PersistentSettings settings = new();
        UnitManager unitManager = new(settings);

        FakeHardware hardware = new(new Identifier("wire", "0"), "Wire CPU", HardwareType.Cpu);
        FakeSensor sensor = new(hardware, SensorType.Control, 0, "Pump", 42f, 10f, 90f);
        FakeControl control = new(sensor, 10f, 90f);
        sensor.Control = control;
        hardware.AddSensor(sensor);

        Node root = new("WIRE-PC");
        var hardwareNode = new HardwareNode(hardware, settings, unitManager);
        root.Nodes.Add(hardwareNode);

        return new SensorFixture
        {
            Control = control,
            HardwareNode = hardwareNode,
            Sensor = sensor,
            SensorId = sensor.Identifier.ToString(),
            Server = new HttpServer(root, hardware, "127.0.0.1", port ?? ReserveLoopbackPort(), authEnabled, userName)
        };
    }

    private sealed class RunningServer : IAsyncDisposable
    {
        private readonly HttpClient _client;
        private readonly HttpServer _server;

        internal RunningServer(HttpServer server)
        {
            _server = server;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                UseProxy = false
            };
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://127.0.0.1:{server.ListenerPort}/"),
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        internal string Origin => _client.BaseAddress.GetLeftPart(UriPartial.Authority);

        internal async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string path,
            string origin = null,
            string acceptEncoding = null,
            (string User, string Password)? basicCredentials = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post)
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            if (origin != null)
                request.Headers.TryAddWithoutValidation("Origin", origin);
            if (acceptEncoding != null)
                request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            if (basicCredentials.HasValue)
            {
                string raw = basicCredentials.Value.User + ":" + basicCredentials.Value.Password;
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
            }

            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _server.StopHttpListenerAsync();
                await _server.QuitAsync();
            }
            finally
            {
                _client.Dispose();
            }
        }
    }

    private sealed class SensorFixture : IDisposable
    {
        internal FakeControl Control { get; init; }
        internal HardwareNode HardwareNode { get; init; }
        internal FakeSensor Sensor { get; init; }
        internal HttpServer Server { get; init; }
        internal string SensorId { get; init; }

        public void Dispose() => HardwareNode.Dispose();
    }

#pragma warning disable CS0067 // Events are required by the interfaces and not raised by these fakes.

    private sealed class FakeHardware : IHardware
    {
        private readonly List<ISensor> _sensors = new();

        internal FakeHardware(Identifier identifier, string name, HardwareType hardwareType)
        {
            Identifier = identifier;
            Name = name;
            HardwareType = hardwareType;
        }

        public event SensorEventHandler SensorAdded;
        public event SensorEventHandler SensorRemoved;

        public HardwareType HardwareType { get; }
        public Identifier Identifier { get; }
        public string Name { get; set; }
        public IHardware Parent => null;
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public ISensor[] Sensors => _sensors.ToArray();
        public IHardware[] SubHardware => Array.Empty<IHardware>();

        internal void AddSensor(ISensor sensor) => _sensors.Add(sensor);

        public string GetReport() => string.Empty;
        public void Update() { }
        public void Accept(IVisitor visitor) => visitor.VisitHardware(this);

        public void Traverse(IVisitor visitor)
        {
            foreach (ISensor sensor in _sensors)
                sensor.Accept(visitor);
        }
    }

    private sealed class FakeSensor : ISensor
    {
        internal FakeSensor(IHardware hardware, SensorType sensorType, int index, string name, float? value, float? min, float? max)
        {
            Hardware = hardware;
            SensorType = sensorType;
            Index = index;
            Name = name;
            Value = value;
            Min = min;
            Max = max;
            Identifier = new Identifier(hardware.Identifier, sensorType.ToString().ToLowerInvariant(), index.ToString(CultureInfo.InvariantCulture));
        }

        public IControl Control { get; set; }
        public IHardware Hardware { get; }
        public Identifier Identifier { get; }
        public int Index { get; }
        public bool IsDefaultHidden => false;
        public float? Max { get; }
        public float? Min { get; }
        public string Name { get; set; }
        public IReadOnlyList<IParameter> Parameters => Array.Empty<IParameter>();
        public SensorType SensorType { get; }
        public float? Value { get; }
        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }
        internal int ResetMinCallCount { get; private set; }
        internal int ResetMaxCallCount { get; private set; }

        public void ResetMin() => ResetMinCallCount++;
        public void ResetMax() => ResetMaxCallCount++;
        public void ClearValues() { }
        public void Accept(IVisitor visitor) => visitor.VisitSensor(this);
        public void Traverse(IVisitor visitor) { }
    }

    private sealed class FakeControl : IControl
    {
        internal FakeControl(ISensor sensor, float minSoftwareValue, float maxSoftwareValue)
        {
            Sensor = sensor;
            MinSoftwareValue = minSoftwareValue;
            MaxSoftwareValue = maxSoftwareValue;
            Identifier = new Identifier(sensor.Identifier, "control");
        }

        public ControlMode ControlMode { get; private set; } = ControlMode.Undefined;
        public Identifier Identifier { get; }
        public float MaxSoftwareValue { get; }
        public float MinSoftwareValue { get; }
        public ISensor Sensor { get; }
        public float SoftwareValue { get; private set; }
        internal int SetDefaultCallCount { get; private set; }
        internal int SetSoftwareCallCount { get; private set; }

        public void SetDefault()
        {
            ControlMode = ControlMode.Default;
            SetDefaultCallCount++;
        }

        public void SetSoftware(float value)
        {
            ControlMode = ControlMode.Software;
            SoftwareValue = value;
            SetSoftwareCallCount++;
        }
    }

#pragma warning restore CS0067
}
