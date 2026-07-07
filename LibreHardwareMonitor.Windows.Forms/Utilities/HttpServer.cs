// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class HttpServer
{
    private readonly HttpListener _listener;
    private readonly Node _root;
    private readonly IElement _rootElement;
    private readonly Version _version = typeof(HttpServer).Assembly.GetName().Version;

    private Task _listenerTask;
    private CancellationTokenSource _cts;

    public HttpServer(Node node, IElement rootElement, string ip, int port, bool authEnabled = false, string userName = "", string passwordSHA256 = "")
    {
        _root = node;
        _rootElement = rootElement;
        ListenerIp = ip;
        ListenerPort = port;
        AuthEnabled = authEnabled;
        UserName = userName;
        PasswordSHA256 = passwordSHA256;

        try
        {
            _listener = new HttpListener { IgnoreWriteExceptions = true };
        }
        catch (PlatformNotSupportedException)
        {
            _listener = null;
        }
    }

    ~HttpServer()
    {
        if (PlatformNotSupported)
            return;

        StopHttpListener();
        try
        {
            _listener?.Abort();
        }
        catch { }
    }

    public bool AuthEnabled { get; set; }

    public string ListenerIp { get; set; }

    public int ListenerPort { get; set; }

    public void SetPassword(string plainPassword)
    {
        PasswordSHA256 = ComputeSHA256(plainPassword);
    }

    public bool PlatformNotSupported
    {
        get { return _listener == null; }
    }

    public string UserName { get; set; }

    public string PasswordSHA256 { get; set; }

    public bool StartHttpListener()
    {
        if (PlatformNotSupported)
            return false;

        try
        {
            if (_listener.IsListening)
                return true;

            // Validate that the selected IP exists (it could have been previously selected
            // before switching networks). Enumerate local interfaces instead of a DNS
            // round-trip: Dns.GetHostEntry can block for seconds on the UI thread while
            // NICs initialize or DNS is misconfigured.
            bool ipFound = false;
            foreach (System.Net.NetworkInformation.NetworkInterface nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ListenerIp == address.Address.ToString())
                    {
                        ipFound = true;
                        break;
                    }
                }

                if (ipFound)
                    break;
            }

            if (!ipFound)
            {
                // default to behavior of previous version if we don't know what interface to use.
                ListenerIp = "+";
            }

            string prefix = "http://" + ListenerIp + ":" + ListenerPort + "/";

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add(prefix);
            _listener.Realm = "Libre Hardware Monitor";
            _listener.AuthenticationSchemes = AuthEnabled ? AuthenticationSchemes.Basic : AuthenticationSchemes.Anonymous;
            _listener.Start();

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ProcessRequestsAsync(_cts.Token));
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    public bool StopHttpListener()
    {
        if (PlatformNotSupported)
            return false;

        try
        {
            _cts?.Cancel();
            // Stop() faults the pending GetContextAsync (which ignores the token) so the accept
            // loop exits immediately; the Wait below is only a backstop.
            _listener?.Stop();
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));
            _cts?.Dispose();
        }
        catch (HttpListenerException)
        { }
        catch (OperationCanceledException)
        { }
        catch (NullReferenceException)
        { }
        catch (Exception)
        { }

        return true;
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 50)
            {
                // Handle Windows update bug (e.g., 2025-10 Cumulative Update): retry after delay
                System.Diagnostics.Debug.WriteLine($"HttpListener error (code {ex.ErrorCode}): {ex.Message}. Retrying in 5 seconds.");
                await Task.Delay(5000, cancellationToken);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995)
            {
                break; // ERROR_OPERATION_ABORTED: Stop()/Abort() faulted the pending accept
            }
            catch (ObjectDisposedException)
            {
                break; // Listener stopped
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected HttpListener error: {ex.Message}");
            }
        }
    }

    public static IDictionary<string, string> ToDictionary(NameValueCollection col)
    {
        IDictionary<string, string> dict = new Dictionary<string, string>();
        foreach (string k in col.AllKeys)
        {
            dict.Add(k, col[k]);
        }

        return dict;
    }

    public SensorNode FindSensor(Node node, string id)
    {
        // Listener threads traverse the tree while UI/worker threads mutate it.
        lock (Node.SyncRoot)
        {
            return FindSensorCore(node, id);
        }
    }

    private static SensorNode FindSensorCore(Node node, string id)
    {
        if (node is SensorNode sNode)
        {
            if (sNode.Sensor.Identifier.ToString() == id)
                return sNode;
        }

        foreach (Node child in node.Nodes)
        {
            SensorNode s = FindSensorCore(child, id);
            if (s != null)
            {
                return s;
            }
        }

        return null;
    }
    public void SetSensorControlValue(SensorNode sNode, string value)
    {
        IControl control = sNode.Sensor.Control;

        if (control == null)
        {
            throw new ArgumentException("Specified sensor '" + sNode.Sensor.Identifier + "' can not be set");
        }

        if (value == "null")
        {
            control.SetDefault();
        }
        else
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float softwareValue) ||
                float.IsNaN(softwareValue) ||
                float.IsInfinity(softwareValue))
            {
                throw new ArgumentException("Invalid control value '" + value + "' specified");
            }

            if (softwareValue < control.MinSoftwareValue)
                softwareValue = control.MinSoftwareValue;
            else if (softwareValue > control.MaxSoftwareValue)
                softwareValue = control.MaxSoftwareValue;

            control.SetSoftware(softwareValue);
        }
    }

    internal Dictionary<string, object> HandleGetSensorRequest(NameValueCollection queryString)
    {
        var result = new Dictionary<string, object>();

        try
        {
            // Hardware control writes must not be reachable via GET (CSRF).
            if (queryString["action"] == "Set")
            {
                result["result"] = "fail";
                result["message"] = "Set requires a POST request";
            }
            else
            {
                HandleSensorRequest(queryString, null, result);
            }
        }
        catch (Exception e)
        {
            result["result"] = "fail";
            result["message"] = e.Message; // never e.ToString(): no stack traces to clients
        }

        return result;
    }

    //Handles "/Sensor" requests.
    //Parameters are taken from the query part of the URL.
    //Get:
    //http://localhost:8085/Sensor?action=Get&id=/some/node/path/0
    //The output is either:
    //{"result":"fail","message":"Some error message"}
    //or:
    //{"result":"ok","value":42.0, "format":"{0:F2} RPM"}
    //
    //Set:
    //http://localhost:8085/Sensor?action=Set&id=/some/node/path/0&value=42.0
    //http://localhost:8085/Sensor?action=Set&id=/some/node/path/0&value=null
    //The output is either:
    //{"result":"fail","message":"Some error message"}
    //or:
    //{"result":"ok"}
    private static bool IsCrossOriginBrowserRequest(HttpListenerRequest request)
    {
        string origin = request.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin))
        {
            // "null" origins (sandboxed frames, file://) fail TryCreate and are rejected.
            return !(Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri) &&
                     string.Equals(originUri.Host, request.Url.Host, StringComparison.OrdinalIgnoreCase));
        }

        string referer = request.Headers["Referer"];
        if (!string.IsNullOrEmpty(referer))
        {
            return !(Uri.TryCreate(referer, UriKind.Absolute, out Uri refererUri) &&
                     string.Equals(refererUri.Host, request.Url.Host, StringComparison.OrdinalIgnoreCase));
        }

        // No browser-context headers: a non-browser client (scripts, curl, the downstream poller).
        return false;
    }

    private void HandleSensorRequest(HttpListenerRequest request, Dictionary<string, object> result)
    {
        HandleSensorRequest(request.QueryString, request, result);
    }

    private void HandleSensorRequest(NameValueCollection queryString, HttpListenerRequest request, Dictionary<string, object> result)
    {
        IDictionary<string, string> dict = ToDictionary(queryString);

        if (dict.ContainsKey("action"))
        {
            if (dict.ContainsKey("id"))
            {
                SensorNode sNode = FindSensor(_root, dict["id"]);

                if (sNode == null)
                {
                    throw new ArgumentException("Unknown id " + dict["id"] + " specified");
                }

                if (dict["action"] == "ResetMinMax")
                {
                    // Reset Min/Max, then return Sensor values...
                    sNode.Sensor.ResetMin();
                    sNode.Sensor.ResetMax();
                    dict["action"] = "Get";
                }

                switch (dict["action"])
                {
                    case "Set" when dict.ContainsKey("value"):
                        // A cross-origin HTML form POST is a CORS "simple request" (no
                        // preflight), so rejecting GET alone does not stop drive-by CSRF
                        // against hardware control writes. Browsers always attach Origin
                        // (or at least Referer) to cross-site form posts; script clients
                        // like LiquidCool.py send neither and are unaffected.
                        if (request != null && IsCrossOriginBrowserRequest(request))
                            throw new ArgumentException("Set rejected: cross-origin browser requests are not allowed");

                        SetSensorControlValue(sNode, dict["value"]);
                        break;
                    case "Set":
                        throw new ArgumentException("No value provided");
                    case "Get":
                        // Non-finite readings (NaN/Infinity) are mapped to null so System.Text.Json
                        // does not throw; this path serves both GET and POST /Sensor requests.
                        result["value"] = SanitizeFloat(sNode.Sensor.Value);
                        result["min"] = SanitizeFloat(sNode.Sensor.Min);
                        result["max"] = SanitizeFloat(sNode.Sensor.Max);
                        result["format"] = sNode.Format;
                        break;
                    default:
                        throw new ArgumentException("Unknown action type " + dict["action"]);
                }
            }
            else
            {
                throw new ArgumentException("No id provided");
            }
        }
        else
        {
            throw new ArgumentException("No action provided");
        }
    }

    //Handles http POST requests in a REST like manner.
    //Currently the only supported base URL is http://localhost:8085/Sensor.
    private string HandlePostRequest(HttpListenerRequest request)
    {
        var result = new Dictionary<string, object> { ["result"] = "ok" };

        try
        {
            if (request.Url.Segments.Length == 2)
            {
                if (request.Url.Segments[1] == "Sensor")
                {
                    HandleSensorRequest(request, result);
                }
                else
                {
                    throw new ArgumentException("Invalid URL ('" + request.Url.Segments[1] + "'), possible values: ['Sensor']");
                }
            }
            else
                throw new ArgumentException("Empty URL, possible values: ['Sensor']");
        }
        catch (Exception e)
        {
            result["result"] = "fail";
            result["message"] = e.Message; // never e.ToString(): no stack traces to clients
        }
        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        // Backstop: any unhandled error while handling a request must still close the response.
        // Otherwise the client connection hangs until it times out — e.g. a JSON serialization
        // failure on a non-finite sensor value (NaN/Infinity), which System.Text.Json rejects.
        try
        {
            await DispatchRequestAsync(context);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP request handler error: {ex.Message}");
            try { context.Response.StatusCode = 500; }
            catch { }
        }
        finally
        {
            try { context.Response.Close(); }
            catch { /* client closed connection before the content was sent */ }
        }
    }

    private async Task DispatchRequestAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        bool authenticated = true;

        if (AuthEnabled)
        {
            try
            {
                HttpListenerBasicIdentity identity = (HttpListenerBasicIdentity)context.User.Identity;
                authenticated = (identity.Name == UserName) && (ComputeSHA256(identity.Password) == PasswordSHA256);
            }
            catch
            {
                authenticated = false;
            }
        }

        if (authenticated)
        {
            switch (request.HttpMethod)
            {
                case "POST":
                    {
                        string postResult = HandlePostRequest(request);
                        await SendResponseAsync(context.Response, postResult, "application/json");
                        break;
                    }
                case "GET":
                    {
                        string path = request.Url.AbsolutePath;
                        string requestedFile = path.TrimStart('/');

                        if (TryMapDashboardPreviewResource(path, out string previewResource, out string previewExt))
                        {
                            await ServeResourceFileAsync(context.Response, previewResource, previewExt);
                            return;
                        }

                        if (string.Equals(path, "/data.json", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendJsonAsync(context.Response, request);
                            return;
                        }

                        if (path.StartsWith("/images_icon/", StringComparison.OrdinalIgnoreCase))
                        {
                            await ServeResourceImageAsync(context.Response, requestedFile.Substring("images_icon/".Length));
                            return;
                        }

                        if (string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendPrometheusAsync(context.Response, request);
                            return;
                        }

                        if (string.Equals(path, "/Sensor", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendJsonSensorAsync(context.Response, HandleGetSensorRequest(request.QueryString));
                            return;
                        }

                        if (string.Equals(path, "/ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
                        {
                            _rootElement.Accept(new SensorVisitor(delegate (ISensor sensor)
                            {
                                sensor.ResetMin();
                                sensor.ResetMax();
                            }));
                            await SendJsonAsync(context.Response, request);
                            return;
                        }

                        // default file to be served
                        if (string.IsNullOrEmpty(requestedFile))
                            requestedFile = "index.html";

                        string[] splits = requestedFile.Split('.');
                        string ext = splits[splits.Length - 1];
                        await ServeResourceFileAsync(context.Response, "Web." + requestedFile.Replace('/', '.'), ext);
                        break;
                    }
                default:
                    {
                        context.Response.StatusCode = 404;
                        break;
                    }
            }
        }
        else
        {
            context.Response.StatusCode = 401;
        }

        if (context.Response.StatusCode == 401)
        {
            const string responseString = @"<HTML><HEAD><TITLE>401 Unauthorized</TITLE></HEAD>
  <BODY><H4>401 Unauthorized</H4>
  Authorization required.</BODY></HTML> ";

            await SendResponseAsync(context.Response, responseString, "text/html");
        }
    }

    internal static bool TryMapDashboardPreviewResource(string absolutePath, out string resourcePath, out string ext)
    {
        resourcePath = null;
        ext = null;

        if (string.IsNullOrEmpty(absolutePath))
            return false;

        string path = absolutePath.Trim('/');
        if (!path.StartsWith("dash/", StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        string slug = parts[1];
        if (!string.Equals(slug, "cardtruth", StringComparison.OrdinalIgnoreCase))
        {
            resourcePath = "WebDash.__missing__.index.html";
            ext = "html";
            return true;
        }

        slug = "cardtruth";

        for (int i = 0; i < slug.Length; i++)
        {
            char c = slug[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                resourcePath = "WebDash.__missing__.index.html";
                ext = "html";
                return true;
            }
        }

        string assetPath;
        if (parts.Length == 2)
        {
            assetPath = "index.html";
        }
        else
        {
            assetPath = string.Join("/", parts.Skip(2));
            if (string.IsNullOrWhiteSpace(assetPath) || assetPath.EndsWith("/", StringComparison.Ordinal))
                assetPath = "index.html";
        }

        string[] pathParts = assetPath.Split('/');
        for (int i = 0; i < pathParts.Length; i++)
        {
            string part = pathParts[i];
            if (string.IsNullOrEmpty(part) || part == "." || part == "..")
            {
                resourcePath = "WebDash.__missing__.index.html";
                ext = "html";
                return true;
            }

            for (int j = 0; j < part.Length; j++)
            {
                char c = part[j];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                {
                    resourcePath = "WebDash.__missing__.index.html";
                    ext = "html";
                    return true;
                }
            }
        }

        string[] assetParts = assetPath.Split('.');
        ext = assetParts[assetParts.Length - 1];
        resourcePath = "WebDash." + slug + "." + assetPath.Replace('/', '.');
        return true;
    }

    private async Task ServeResourceFileAsync(HttpListenerResponse response, string name, string ext)
    {
        // resource names do not support the hyphen
        name = Assembly.GetExecutingAssembly().GetName().Name + ".Resources." +
               name.Replace("custom-theme", "custom_theme");

        string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();

        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Replace('\\', '.') == name)
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(names[i]);

                response.ContentType = GetContentType("." + ext);
                response.ContentLength64 = stream.Length;
                byte[] buffer = new byte[512 * 1024];
                try
                {
                    int len;
                    while ((len = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await response.OutputStream.WriteAsync(buffer, 0, len);
                    }

                    await response.OutputStream.FlushAsync();
                    response.OutputStream.Close();
                    response.Close();
                }
                catch (HttpListenerException)
                { }
                catch (InvalidOperationException)
                { }

                return;
            }
        }

        response.StatusCode = 404;
        response.Close();
    }

    private async Task ServeResourceImageAsync(HttpListenerResponse response, string name)
    {
        name = Assembly.GetExecutingAssembly().GetName().Name + ".Resources." + name;

        string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();

        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Replace('\\', '.') == name)
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(names[i]);

                using Image image = Image.FromStream(stream);
                response.ContentType = "image/png";
                try
                {
                    using var ms = new MemoryStream();
                    image.Save(ms, ImageFormat.Png);
                    byte[] buffer = ms.ToArray();
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                catch (HttpListenerException)
                { }

                response.Close();
                return;
            }
        }

        response.StatusCode = 404;
        response.Close();
    }

    // Serialization buffer reused across data.json requests: the payload is ~155 KB, so a fresh
    // array per 1 Hz poll would be a Large Object Heap allocation every second for the lifetime
    // of the process. Contexts are handled concurrently, hence the gate.
    private readonly MemoryStream _dataJsonBuffer = new();
    private readonly SemaphoreSlim _dataJsonBufferGate = new(1, 1);

    // The data.json object graph and its serialization are the external downstream contract;
    // exposed internal (see LibreHardwareMonitor.Tests golden-master tests) so any change to
    // either path is locked to byte-identical output.
    internal Dictionary<string, object> BuildDataJsonObject()
    {
        Dictionary<string, object> json = new();

        int nodeIndex = 0;

        json["id"] = nodeIndex++;
        json["Version"] = $"{_version.Major}.{_version.Minor}.{_version.Build}";
        json["Text"] = "Sensor";
        json["Min"] = "Min";
        json["Value"] = "Value";
        json["Max"] = "Max";
        json["ImageURL"] = string.Empty;

        // Snapshot the node tree under the lock; serialization and I/O happen outside it.
        lock (Node.SyncRoot)
        {
            json["Children"] = new List<object> { GenerateJsonForNode(_root, ref nodeIndex) };
        }

        return json;
    }

    internal void WriteDataJson(Stream output)
    {
        System.Text.Json.JsonSerializer.Serialize(output, BuildDataJsonObject());
    }

    private async Task SendJsonAsync(HttpListenerResponse response, HttpListenerRequest request = null)
    {
        bool acceptGzip;
        try
        {
            acceptGzip = (request != null) && (request.Headers["Accept-Encoding"].IndexOf("gzip", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
            acceptGzip = false;
        }

        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.ContentType = "application/json";

        await _dataJsonBufferGate.WaitAsync();

        try
        {
            _dataJsonBuffer.SetLength(0);
            WriteDataJson(_dataJsonBuffer);

            try
            {
                if (acceptGzip)
                {
                    response.AddHeader("Content-Encoding", "gzip");
                    using var ms = new MemoryStream();
                    using (var zip = new GZipStream(ms, CompressionMode.Compress, true))
                        await zip.WriteAsync(_dataJsonBuffer.GetBuffer(), 0, (int)_dataJsonBuffer.Length);

                    // Write the stream's internal buffer directly instead of copying it via ToArray().
                    response.ContentLength64 = ms.Length;
                    await response.OutputStream.WriteAsync(ms.GetBuffer(), 0, (int)ms.Length);
                }
                else
                {
                    response.ContentLength64 = _dataJsonBuffer.Length;
                    await response.OutputStream.WriteAsync(_dataJsonBuffer.GetBuffer(), 0, (int)_dataJsonBuffer.Length);
                }

                response.OutputStream.Close();
            }
            catch (HttpListenerException)
            { }
        }
        finally
        {
            _dataJsonBufferGate.Release();
        }

        response.Close();
    }

    // Dictionary to convert all data to base units for OpenMetrics
    // SensorType, Item1 suffix, Item2 factor
    private static readonly Dictionary<SensorType, (string, double)> _prometheusUnits = new()
    {
        { SensorType.Clock, ("hertz", 1000000)},                           //originally megahertz
        { SensorType.Conductivity, ("seconds_per_centimeter", 0.000001) }, //originally microseconds per centimeter
        { SensorType.Control, ("percent", 1) },
        { SensorType.Current, ("amperes", 1) },
        { SensorType.Data, ("bytes", 1000000000) },                        //originally GB
        { SensorType.Energy, ("watthour", 0.001) },
        { SensorType.Factor, ("", 1) },
        { SensorType.Fan, ("rpm", 1) },
        { SensorType.Flow, ("liters_per_hour", 1) },
        { SensorType.Frequency, ("hertz", 1) },
        { SensorType.Humidity, ("percent", 1) },
        { SensorType.Level, ("percent", 1) },
        { SensorType.Load, ("percent", 1) },
        { SensorType.Noise, ("decibels", 1) },
        { SensorType.Power, ("watts", 1) },
        { SensorType.SmallData, ("bytes", 1024*1024) },                    //originally MiB
        { SensorType.Temperature, ("celsius", 1) },
        { SensorType.Throughput, ("bytes_per_second", 1) },
        { SensorType.TimeSpan, ("seconds", 1) },
        { SensorType.Timing, ("seconds", 0.000000001 ) },                  //originally nanoseconds
        { SensorType.Voltage, ("volts", 1) },
    };

    private void GeneratePrometheusResponse(Node node, Dictionary<string, int> prometheusSettings, StringBuilder responseBuilder)
    {
        // Intentionally local: each HardwareNode recursion re-emits the TYPE line for its first tag.
        string lastTagName = "";

        for (int i = 0; i < node.Nodes.Count; i++)
        {
            if (node.Nodes[i].GetType().Name == "HardwareNode")
            {
                GeneratePrometheusResponse(node.Nodes[i], prometheusSettings, responseBuilder);
            }

            if (node.Nodes[i].GetType().Name == "TypeNode")
            {
                string tagHardware = "";
                string valueHardwareName = "";
                string valueHardwareId = ((HardwareNode)node).Hardware.Identifier.ToString();

                if (((HardwareNode)node).Hardware.Parent != null)
                {
                    tagHardware = ((HardwareNode)node).Hardware.Parent.HardwareType.ToString();
                    valueHardwareName = ((HardwareNode)node).Hardware.Parent.Name;
                }
                else
                {
                    tagHardware = ((HardwareNode)node).Hardware.HardwareType.ToString();
                    valueHardwareName = node.Text;
                }

                string valueHardwareAlias = $"{valueHardwareName} ({valueHardwareId})";

                foreach (SensorNode sensor in node.Nodes[i].Nodes)
                {
                    string valueSensorName = sensor.Text.Replace("#", String.Empty);

                    // Variables needed in dictionary lookup and error message
                    string tagSensorType = sensor.Sensor.SensorType.ToString();

                    double factor = 1;
                    string tagSensorUnits = "";

                    // Get factor and unit suffix from dictionary ...
                    if (_prometheusUnits.ContainsKey(sensor.Sensor.SensorType))
                    {
                        factor = _prometheusUnits[sensor.Sensor.SensorType].Item2;
                        tagSensorUnits = (_prometheusUnits[sensor.Sensor.SensorType].Item1.Length == 0 ? String.Empty : "_" + _prometheusUnits[sensor.Sensor.SensorType].Item1);
                    }
                    // ... or print an error message
                    else
                    {
                        responseBuilder.Append($"# HELP {tagHardware}_{tagSensorType}:{valueSensorName} This Sensor type is not defined in the prometheus adapter [{sensor.Sensor.SensorType}]\n");
                    }

                    // Creating the tag name for prometheus
                    string tagName = $"lhm_{tagHardware}_{tagSensorType}{tagSensorUnits}";
                    tagName = tagName.ToLower();

                    // Preparing the labels for all data and uniqueness
                    string valueSensorId = sensor.Sensor.Identifier.ToString().Substring(valueHardwareId.Length);
                    string valueSensorAlias = $"{valueSensorName} ({valueSensorId})";
                    string valueHost = _root.Text;

                    // Creates the tag with labels
                    string tagLine = $$"""{{tagName}} {"sensorName"="{{valueSensorName}}", "sensorAlias"="{{valueSensorAlias}}", "hardwareName"="{{valueHardwareName}}", "hardwareAlias"="{{valueHardwareAlias}}", "sensorId"="{{valueSensorId}}", "hardwareId"="{{valueHardwareId}}", "host"="{{valueHost}}"}""";

                    if (lastTagName != tagName)
                    {
                        responseBuilder.Append($"# TYPE {tagName} gauge\n");
                        lastTagName = tagName;
                    }

                    // Iterate newest-to-oldest by index instead of LINQ Reverse(): Reverse would
                    // buffer the entire history snapshot per sensor per scrape, and this loop
                    // runs while holding Node.SyncRoot (shared with data.json).
                    IEnumerable<SensorValue> sensorValues = sensor.Sensor.Values;
                    SensorValue[] history = sensorValues as SensorValue[] ?? sensorValues.ToArray();

                    int counter = 0;
                    for (int v = history.Length - 1; v >= 0; v--)
                    {
                        SensorValue val = history[v];
                        if (counter++ > prometheusSettings["archivelength"])
                            break;

                        if (float.IsNaN(val.Value))
                        {
                            // Print a help line saying what tag had an invalid value
                            responseBuilder.Append($"# HELP {tagLine} has an invalid value and was skipped.\n");
                        }
                        else
                        {
                            if (counter == 1 && prometheusSettings["lastvalue"] == 0)
                                continue; // skip the first value in the list

                            if (prometheusSettings["timestamps"] == 1)
                            {
                                responseBuilder.Append($"{tagLine} {(val.Value * factor).ToString(CultureInfo.InvariantCulture)} {((DateTimeOffset)val.Time).ToUnixTimeMilliseconds()}\n");
                            }
                            else
                            {
                                responseBuilder.Append($"{tagLine} {(val.Value * factor).ToString(CultureInfo.InvariantCulture)}\n");
                            }
                        }
                    }
                }
            }
        }
    }

    private async Task SendPrometheusAsync(HttpListenerResponse response, HttpListenerRequest request = null)
    {
        Dictionary<string, int> prometheusSettings = new Dictionary<string, int>();
        //Default values: archivelength=0, timestamps=0, lastvalue=1
        prometheusSettings["archivelength"] = 0;
        prometheusSettings["timestamps"] = 0;
        prometheusSettings["lastvalue"] = 1;

        if (request != null && request.QueryString != null && request.QueryString.Count > 0)
        {
            int archive = 0, timestamps = 0, lastvalue = 1;
            
            foreach (string key in request.QueryString.AllKeys)
            {
                switch (key)
                {
                    case "timestamps":
                        int.TryParse(request.QueryString[key], out timestamps);     

                        if (timestamps < 0 || timestamps > 1)
                            timestamps = 0;     // Enforce boolean range 0 to 1

                        if (archive > 0)
                            timestamps = 1;     // If archive is requested, timestamps must be enabled

                        break;
                    case "archivelength":
                        int.TryParse(request.QueryString[key], out archive);
                        archive = Math.Min(10, archive); // Enforce max 10
                        archive = Math.Max(0, archive); // Enforce min 0

                        if (archive == 0 && lastvalue == 0)
                            archive = 1; // If lastvalue was not requested then return at least 1 archived value

                        if (archive > 0)
                            timestamps = 1; // If archive is requested, timestamps must be enabled

                        break;
                    case "lastvalue":
                        int.TryParse(request.QueryString[key], out lastvalue);

                        if (lastvalue < 0 || lastvalue > 1)
                            lastvalue = 1; // Enforce boolean range 0 to 1

                        if (lastvalue == 0 && archive  == 0)
                        {
                            archive = 1;
                            timestamps = 1;
                        }

                        break;
                    default:
                        break;
                }
            }

            prometheusSettings["archivelength"] = archive;
            prometheusSettings["timestamps"] = timestamps;
            prometheusSettings["lastvalue"] = lastvalue;
        }

        StringBuilder responseBuilder = new StringBuilder();

        // Snapshot the node tree under the lock; the response is written outside it.
        lock (Node.SyncRoot)
        {
            GeneratePrometheusResponse(_root, prometheusSettings, responseBuilder);
        }

        string responseContent = responseBuilder.ToString();
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");

        // Add custom headers to inform the user what settings are in effect
        response.AddHeader("X-archivelength", prometheusSettings["archivelength"].ToString());
        response.AddHeader("X-timestamps", prometheusSettings["timestamps"].ToString());
        response.AddHeader("X-lastvalue", prometheusSettings["lastvalue"].ToString());

        await SendResponseAsync(response, responseContent, "text/plain");
    }

    private async Task SendJsonSensorAsync(HttpListenerResponse response, Dictionary<string, object> sensorData)
    {
        // Convert the JObject to a JSON string
        string responseContent = System.Text.Json.JsonSerializer.Serialize(sensorData);
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        await SendResponseAsync(response, responseContent, "application/json");
    }
        
    private Dictionary<string, object> GenerateJsonForNode(Node n, ref int nodeIndex)
    {
        Dictionary<string, object> jsonNode = new()
        {
            ["id"] = nodeIndex++,
            ["Text"] = n.Text,
            ["Min"] = string.Empty,
            ["Value"] = string.Empty,
            ["Max"] = string.Empty
        };

        switch (n)
        {
            case SensorNode sensorNode:
                jsonNode["SensorId"] = sensorNode.Sensor.Identifier.ToString();
                jsonNode["Type"] = sensorNode.Sensor.SensorType.ToString();

                // Formatted values, e.g. Throughput will be measured in KB/s or MB/s depending on the value
                jsonNode["Min"] = sensorNode.Min;
                jsonNode["Value"] = sensorNode.Value;
                jsonNode["Max"] = sensorNode.Max;

                // Unformatted values for external systems to have consistent readings, e.g. Throughput will always be measured in B/s
                // Non-finite readings (NaN/Infinity) are mapped to null: System.Text.Json rejects them and they mean "no reading".
                jsonNode["RawMin"] = SanitizeFloat(sensorNode.Sensor.Min);
                jsonNode["RawValue"] = SanitizeFloat(sensorNode.Sensor.Value);
                jsonNode["RawMax"] = SanitizeFloat(sensorNode.Sensor.Max);

                jsonNode["ImageURL"] = "images/transparent.png";
                break;
            case HardwareNode hardwareNode:
                jsonNode["HardwareId"] = hardwareNode.Hardware.Identifier.ToString();
                jsonNode["ImageURL"] = "images_icon/" + GetHardwareImageFile(hardwareNode);
                break;
            case TypeNode typeNode:
                jsonNode["ImageURL"] = "images_icon/" + GetTypeImageFile(typeNode);
                break;
            default:
                jsonNode["ImageURL"] = "images_icon/computer.png";
                break;
        }

        List<object> children = new();
        foreach (Node child in n.Nodes)
        {
            children.Add(GenerateJsonForNode(child, ref nodeIndex));
        }

        jsonNode["Children"] = children;

        return jsonNode;
    }

    // System.Text.Json throws on NaN / Infinity by default. Many sensors report a non-finite
    // value when no reading is available (e.g. unwired motherboard voltages, idle GPU clocks),
    // so map those to null ("no reading") to keep data.json and the Sensor API valid and
    // responsive instead of throwing mid-serialization and hanging the client connection.
    private static object SanitizeFloat(float? value)
    {
        if (value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value))
            return value.Value;

        return null;
    }

    private static string GetContentType(string extension)
    {
        switch (extension)
        {
            case ".avi": return "video/x-msvideo";
            case ".css": return "text/css";
            case ".doc": return "application/msword";
            case ".gif": return "image/gif";
            case ".htm":
            case ".html": return "text/html";
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".js": return "application/x-javascript";
            case ".mp3": return "audio/mpeg";
            case ".png": return "image/png";
            case ".pdf": return "application/pdf";
            case ".ppt": return "application/vnd.ms-powerpoint";
            case ".zip": return "application/zip";
            case ".txt": return "text/plain";
            default: return "application/octet-stream";
        }
    }
    private static string GetHardwareImageFile(HardwareNode hn)
    {
        switch (hn.Hardware.HardwareType)
        {
            case HardwareType.Cpu:
                return "cpu.png";
            case HardwareType.GpuNvidia:
                return "nvidia.png";
            case HardwareType.GpuAmd:
                return "ati.png";
            case HardwareType.GpuIntel:
                return "intel.png";
            case HardwareType.Storage:
                return "hdd.png";
            case HardwareType.Motherboard:
                return "mainboard.png";
            case HardwareType.SuperIO:
                return "chip.png";
            case HardwareType.Memory:
                return "ram.png";
            case HardwareType.Cooler:
                return "fan.png";
            case HardwareType.Network:
                return "nic.png";
            case HardwareType.Psu:
                return "power-supply.png";
            case HardwareType.Battery:
                return "battery.png";
            case HardwareType.PowerMonitor:
                return "powermonitor.png";
            default:
                return "cpu.png";
        }
    }

    private static string GetTypeImageFile(TypeNode tn)
    {
        switch (tn.SensorType)
        {
            case SensorType.Voltage:
            case SensorType.Current:
                return "voltage.png";
            case SensorType.Clock:
            case SensorType.Timing:
                return "clock.png";
            case SensorType.Load:
                return "load.png";
            case SensorType.Temperature:
                return "temperature.png";
            case SensorType.Fan:
                return "fan.png";
            case SensorType.Flow:
                return "flow.png";
            case SensorType.Control:
                return "control.png";
            case SensorType.Level:
                return "level.png";
            case SensorType.Power:
                return "power.png";
            case SensorType.Noise:
                return "loudspeaker.png";
            case SensorType.Conductivity:
                return "voltage.png";
            case SensorType.Throughput:
                return "throughput.png";
            case SensorType.Humidity:
                return "flow.png";
            default:
                return "power.png";
        }
    }

    private string ComputeSHA256(string text)
    {
        using SHA256 hash = SHA256.Create();
        return string.Concat(hash
                            .ComputeHash(Encoding.UTF8.GetBytes(text))
                            .Select(item => item.ToString("x2")));
    }

    public void Quit()
    {
        if (PlatformNotSupported)
            return;

        StopHttpListener();
        try
        {
            _listener?.Abort();
        }
        catch { }
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, string content, string contentType)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;

        try
        {
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
        catch (HttpListenerException)
        { }
    }

}
