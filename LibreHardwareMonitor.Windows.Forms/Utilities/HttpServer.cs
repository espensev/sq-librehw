// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Buffers;
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
using LibreHardwareMonitor.Windows.Forms.Adapters;
using LibreHardwareMonitor.Windows.Forms.ApplicationModel.Snapshots;
using LibreHardwareMonitor.Windows.Forms.UI;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class HttpServer
{
    internal const string CrossOriginResetMessage = "Reset rejected: cross-origin browser requests are not allowed";
    internal const string ResetMinMaxRequiresPostMessage = "ResetMinMax requires a POST request";
    internal const string ResetAllMinMaxRequiresPostMessage = "ResetAllMinMax requires a POST request";

    private readonly HttpListenerDispatchService _listenerService;
    private readonly Node _root;
    private readonly IElement _rootElement;
    private readonly Version _version = typeof(HttpServer).Assembly.GetName().Version;

    public HttpServer(Node node, IElement rootElement, string ip, int port, bool authEnabled = false, string userName = "", string passwordSHA256 = "")
    {
        _root = node;
        _rootElement = rootElement;
        ListenerIp = ip;
        ListenerPort = port;
        AuthEnabled = authEnabled;
        UserName = userName;
        PasswordSHA256 = passwordSHA256;
        _listenerService = new HttpListenerDispatchService(DispatchRequestAsync);
    }

    ~HttpServer()
    {
        if (PlatformNotSupported)
            return;

        _listenerService?.Abort();
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
        get { return _listenerService.PlatformNotSupported; }
    }

    public string UserName { get; set; }

    public string PasswordSHA256 { get; set; }

    public bool StartHttpListener()
    {
        bool started = _listenerService.Start(ListenerIp, ListenerPort, AuthEnabled, out string effectiveListenerIp);
        ListenerIp = effectiveListenerIp;
        return started;
    }

    public bool StopHttpListener()
    {
        return StopHttpListenerAsync().GetAwaiter().GetResult();
    }

    public async Task<bool> StopHttpListenerAsync()
    {
        return await _listenerService.StopAsync().ConfigureAwait(false);
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
            if (string.Equals(queryString["action"], "Set", StringComparison.OrdinalIgnoreCase))
            {
                result["result"] = "fail";
                result["message"] = "Set requires a POST request";
            }
            else if (string.Equals(queryString["action"], "ResetMinMax", StringComparison.OrdinalIgnoreCase))
            {
                result["result"] = "fail";
                result["message"] = ResetMinMaxRequiresPostMessage;
            }
            else
            {
                HandleSensorRequest(queryString, false, result);
            }
        }
        catch (Exception e)
        {
            result["result"] = "fail";
            result["message"] = e.Message; // never e.ToString(): no stack traces to clients
        }

        return result;
    }

    internal Dictionary<string, object> HandlePostSensorRequest(NameValueCollection queryString, Uri requestUrl, string origin, string referer)
    {
        var result = new Dictionary<string, object> { ["result"] = "ok" };

        try
        {
            HandleSensorRequest(queryString, IsCrossOriginBrowserRequest(requestUrl, origin, referer), result);
        }
        catch (Exception e)
        {
            result["result"] = "fail";
            result["message"] = e.Message; // never e.ToString(): no stack traces to clients
        }

        return result;
    }

    internal bool TryResetAllMinMax(Uri requestUrl, string origin, string referer)
    {
        if (IsCrossOriginBrowserRequest(requestUrl, origin, referer))
            return false;

        _rootElement.Accept(new SensorVisitor(delegate (ISensor sensor)
        {
            sensor.ResetMin();
            sensor.ResetMax();
        }));

        return true;
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
    internal static bool IsCrossOriginBrowserRequest(Uri requestUrl, string origin, string referer)
    {
        if (!string.IsNullOrEmpty(origin))
        {
            // "null" origins (sandboxed frames, file://) fail TryCreate and are rejected.
            return !(Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri) &&
                     IsSameOrigin(originUri, requestUrl));
        }

        if (!string.IsNullOrEmpty(referer))
        {
            return !(Uri.TryCreate(referer, UriKind.Absolute, out Uri refererUri) &&
                     IsSameOrigin(refererUri, requestUrl));
        }

        // No browser-context headers: a non-browser client (scripts, curl, the downstream poller).
        return false;
    }

    private static bool IsSameOrigin(Uri browserUri, Uri requestUri)
    {
        return browserUri != null &&
               requestUri != null &&
               string.Equals(browserUri.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(browserUri.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase) &&
               browserUri.Port == requestUri.Port;
    }

    // Prometheus label values must escape backslash, double-quote, and newline.
    // Backslash is escaped first so the later escapes are not double-escaped.
    internal static string EscapePrometheusLabelValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n");
    }

    private void HandleSensorRequest(NameValueCollection queryString, bool isCrossOriginBrowserRequest, Dictionary<string, object> result)
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
                    if (isCrossOriginBrowserRequest)
                        throw new ArgumentException(CrossOriginResetMessage);

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
                        if (isCrossOriginBrowserRequest)
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
                    return System.Text.Json.JsonSerializer.Serialize(
                        HandlePostSensorRequest(request.QueryString, request.Url, request.Headers["Origin"], request.Headers["Referer"]));
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

    private async Task DispatchRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
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
                        string path = request.Url.AbsolutePath;
                        if (string.Equals(path, "/ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!TryResetAllMinMax(request.Url, request.Headers["Origin"], request.Headers["Referer"]))
                            {
                                context.Response.StatusCode = 403;
                                await SendJsonSensorAsync(context.Response, new Dictionary<string, object>
                                {
                                    ["result"] = "fail",
                                    ["message"] = CrossOriginResetMessage
                                }, cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            await SendJsonAsync(context.Response, request, cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        string postResult = HandlePostRequest(request);
                        await SendResponseAsync(context.Response, postResult, "application/json", cancellationToken).ConfigureAwait(false);
                        break;
                    }
                case "GET":
                    {
                        string path = request.Url.AbsolutePath;
                        string requestedFile = path.TrimStart('/');

                        if (string.Equals(path, "/data.json", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendJsonAsync(context.Response, request, cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (path.StartsWith("/images_icon/", StringComparison.OrdinalIgnoreCase))
                        {
                            await ServeResourceImageAsync(context.Response, requestedFile.Substring("images_icon/".Length), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendPrometheusAsync(context.Response, request, cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(path, "/Sensor", StringComparison.OrdinalIgnoreCase))
                        {
                            await SendJsonSensorAsync(context.Response,
                                HandleGetSensorRequest(request.QueryString),
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(path, "/ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.StatusCode = 405;
                            context.Response.AddHeader("Allow", "POST");
                            await SendJsonSensorAsync(context.Response, new Dictionary<string, object>
                            {
                                ["result"] = "fail",
                                ["message"] = ResetAllMinMaxRequiresPostMessage
                            }, cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (TryMapStableWebResource(path, out string resourcePath, out string ext))
                            await ServeResourceFileAsync(context.Response, resourcePath, ext, cancellationToken).ConfigureAwait(false);
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

            await SendResponseAsync(context.Response, responseString, "text/html", cancellationToken).ConfigureAwait(false);
        }
    }

    internal static bool TryMapStableWebResource(string absolutePath, out string resourcePath, out string ext)
    {
        resourcePath = null;
        ext = null;

        if (absolutePath == null)
            return false;

        string requestedFile = absolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(requestedFile))
            requestedFile = "index.html";

        string[] splits = requestedFile.Split('.');
        ext = splits[splits.Length - 1];
        resourcePath = "Web." + requestedFile.Replace('/', '.');
        return true;
    }

    private async Task ServeResourceFileAsync(HttpListenerResponse response, string name, string ext, CancellationToken cancellationToken)
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
                    while ((len = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await response.OutputStream.WriteAsync(buffer, 0, len, cancellationToken).ConfigureAwait(false);
                    }

                    await response.OutputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task ServeResourceImageAsync(HttpListenerResponse response, string name, CancellationToken cancellationToken)
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
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
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
    // of the process. Each response copies into an ArrayPool lease before releasing the gate, so
    // a slow network write never blocks the next serialization or owns this shared buffer.
    private readonly MemoryStream _dataJsonBuffer = new();
    private readonly SemaphoreSlim _dataJsonBufferGate = new(1, 1);

    // The data.json object graph and its serialization are the external downstream contract;
    // exposed internal (see LibreHardwareMonitor.Tests golden-master tests) so any change to
    // either path is locked to byte-identical output.
    internal Dictionary<string, object> BuildDataJsonObject()
    {
        SensorSnapshot snapshot = WinFormsNodeSensorSnapshotSource.Capture(_root);
        return DataJsonProjection.Project(snapshot, _version);
    }

    internal void WriteDataJson(Stream output)
    {
        System.Text.Json.JsonSerializer.Serialize(output, BuildDataJsonObject());
    }

    private async Task SendJsonAsync(HttpListenerResponse response, HttpListenerRequest request, CancellationToken cancellationToken)
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

        byte[] responseBuffer = null;
        int responseLength = 0;

        await _dataJsonBufferGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _dataJsonBuffer.SetLength(0);
            WriteDataJson(_dataJsonBuffer);
            responseLength = checked((int)_dataJsonBuffer.Length);
            responseBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, responseLength));
            Buffer.BlockCopy(_dataJsonBuffer.GetBuffer(), 0, responseBuffer, 0, responseLength);
        }
        catch
        {
            if (responseBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(responseBuffer);
                responseBuffer = null;
            }

            throw;
        }
        finally
        {
            _dataJsonBufferGate.Release();
        }

        try
        {
            if (acceptGzip)
            {
                response.AddHeader("Content-Encoding", "gzip");
                using var compressed = new MemoryStream();
                using (var zip = new GZipStream(compressed, CompressionMode.Compress, true))
                    await zip.WriteAsync(responseBuffer, 0, responseLength, cancellationToken).ConfigureAwait(false);

                // Write the stream's internal buffer directly instead of copying it via ToArray().
                response.ContentLength64 = compressed.Length;
                await response.OutputStream.WriteAsync(compressed.GetBuffer(), 0, (int)compressed.Length, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response.ContentLength64 = responseLength;
                await response.OutputStream.WriteAsync(responseBuffer, 0, responseLength, cancellationToken).ConfigureAwait(false);
            }

            response.OutputStream.Close();
        }
        catch (HttpListenerException)
        { }
        finally
        {
            if (responseBuffer != null)
                ArrayPool<byte>.Shared.Return(responseBuffer);
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
        { SensorType.TemperatureRate, ("celsius_per_second", 1) },
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
                    string tagLine = $$"""{{tagName}} {"sensorName"="{{EscapePrometheusLabelValue(valueSensorName)}}", "sensorAlias"="{{EscapePrometheusLabelValue(valueSensorAlias)}}", "hardwareName"="{{EscapePrometheusLabelValue(valueHardwareName)}}", "hardwareAlias"="{{EscapePrometheusLabelValue(valueHardwareAlias)}}", "sensorId"="{{EscapePrometheusLabelValue(valueSensorId)}}", "hardwareId"="{{EscapePrometheusLabelValue(valueHardwareId)}}", "host"="{{EscapePrometheusLabelValue(valueHost)}}"}""";

                    if (lastTagName != tagName)
                    {
                        responseBuilder.Append($"# TYPE {tagName} gauge\n");
                        lastTagName = tagName;
                    }

                    // The built-in Sensor implements the optional bounded reader, so the default
                    // scrape copies one point instead of materializing its full history. Third-party
                    // sensors that only implement ISensor retain the existing Values fallback.
                    int maxValues = prometheusSettings["archivelength"] + 1;
                    IReadOnlyList<SensorValue> history = ReadPrometheusHistory(sensor.Sensor, maxValues);

                    int counter = 0;
                    for (int v = history.Count - 1; v >= 0; v--)
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

    private static IReadOnlyList<SensorValue> ReadPrometheusHistory(ISensor sensor, int maxValues)
    {
        if (sensor is ISensorHistoryReader historyReader)
            return historyReader.ReadHistory(0, maxValues);

        IEnumerable<SensorValue> sensorValues = sensor.Values;
        return sensorValues as SensorValue[] ?? sensorValues.ToArray();
    }

    internal (string Content, string ContentType, IReadOnlyList<KeyValuePair<string, string>> Headers) BuildPrometheusResponse(NameValueCollection queryString)
    {
        Dictionary<string, int> prometheusSettings = GetPrometheusSettings(queryString);
        StringBuilder responseBuilder = new();

        // Snapshot the node tree under the lock; the response is written outside it.
        lock (Node.SyncRoot)
        {
            GeneratePrometheusResponse(_root, prometheusSettings, responseBuilder);
        }

        KeyValuePair<string, string>[] headers =
        {
            new("Cache-Control", "no-cache"),
            new("Access-Control-Allow-Origin", "*"),
            new("X-archivelength", prometheusSettings["archivelength"].ToString()),
            new("X-timestamps", prometheusSettings["timestamps"].ToString()),
            new("X-lastvalue", prometheusSettings["lastvalue"].ToString())
        };

        return (responseBuilder.ToString(), "text/plain", headers);
    }

    private static Dictionary<string, int> GetPrometheusSettings(NameValueCollection queryString)
    {
        Dictionary<string, int> prometheusSettings = new Dictionary<string, int>();
        //Default values: archivelength=0, timestamps=0, lastvalue=1
        prometheusSettings["archivelength"] = 0;
        prometheusSettings["timestamps"] = 0;
        prometheusSettings["lastvalue"] = 1;

        if (queryString != null && queryString.Count > 0)
        {
            int archive = 0, timestamps = 0, lastvalue = 1;

            foreach (string key in queryString.AllKeys)
            {
                switch (key)
                {
                    case "timestamps":
                        int.TryParse(queryString[key], out timestamps);

                        if (timestamps < 0 || timestamps > 1)
                            timestamps = 0;     // Enforce boolean range 0 to 1

                        if (archive > 0)
                            timestamps = 1;     // If archive is requested, timestamps must be enabled

                        break;
                    case "archivelength":
                        int.TryParse(queryString[key], out archive);
                        archive = Math.Min(10, archive); // Enforce max 10
                        archive = Math.Max(0, archive); // Enforce min 0

                        if (archive == 0 && lastvalue == 0)
                            archive = 1; // If lastvalue was not requested then return at least 1 archived value

                        if (archive > 0)
                            timestamps = 1; // If archive is requested, timestamps must be enabled

                        break;
                    case "lastvalue":
                        int.TryParse(queryString[key], out lastvalue);

                        if (lastvalue < 0 || lastvalue > 1)
                            lastvalue = 1; // Enforce boolean range 0 to 1

                        if (lastvalue == 0 && archive == 0)
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

        return prometheusSettings;
    }

    private async Task SendPrometheusAsync(HttpListenerResponse response, HttpListenerRequest request, CancellationToken cancellationToken)
    {
        (string content, string contentType, IReadOnlyList<KeyValuePair<string, string>> headers) =
            BuildPrometheusResponse(request?.QueryString);

        foreach (KeyValuePair<string, string> header in headers)
            response.AddHeader(header.Key, header.Value);

        await SendResponseAsync(response, content, contentType, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendJsonSensorAsync(HttpListenerResponse response, Dictionary<string, object> sensorData, CancellationToken cancellationToken)
    {
        // Convert the JObject to a JSON string
        string responseContent = System.Text.Json.JsonSerializer.Serialize(sensorData);
        response.AddHeader("Cache-Control", "no-cache");
        response.AddHeader("Access-Control-Allow-Origin", "*");
        await SendResponseAsync(response, responseContent, "application/json", cancellationToken).ConfigureAwait(false);
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
    private string ComputeSHA256(string text)
    {
        using SHA256 hash = SHA256.Create();
        return string.Concat(hash
                            .ComputeHash(Encoding.UTF8.GetBytes(text))
                            .Select(item => item.ToString("x2")));
    }

    public void Quit()
    {
        QuitAsync().GetAwaiter().GetResult();
    }

    public async Task QuitAsync()
    {
        if (PlatformNotSupported)
            return;

        await StopHttpListenerAsync().ConfigureAwait(false);
        _listenerService.Abort();

        GC.SuppressFinalize(this);
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, string content, string contentType, CancellationToken cancellationToken)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(content);
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;

        try
        {
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            response.OutputStream.Close();
        }
        catch (HttpListenerException)
        { }
    }

}
