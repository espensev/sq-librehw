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
using LibreHardwareMonitor.Windows.Forms.UI;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

public class HttpServer
{
    internal const int DefaultMaxConcurrentHandlers = 16;

    private readonly HttpListener _listener;
    private readonly Node _root;
    private readonly IElement _rootElement;
    private readonly Version _version = typeof(HttpServer).Assembly.GetName().Version;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly BoundedRequestHandlerPool _requestHandlers = new(DefaultMaxConcurrentHandlers);

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

        try
        {
            _cts?.Cancel();
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

        _lifecycleGate.Wait();
        try
        {
            if (_listener.IsListening)
                return true;

            // A timed-out stop retains its canceled session so a restart cannot overwrite the
            // cancellation source while old handlers still own it. A later stop can drain it.
            if (_cts != null || _listenerTask != null)
                return false;

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
        finally
        {
            _lifecycleGate.Release();
        }

        return true;
    }

    public bool StopHttpListener()
    {
        return StopHttpListenerAsync().GetAwaiter().GetResult();
    }

    public async Task<bool> StopHttpListenerAsync()
    {
        if (PlatformNotSupported)
            return false;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource cancellation = _cts;
            Task listenerTask = _listenerTask;

            if (cancellation == null && listenerTask == null && !_listener.IsListening)
                return true;

            cancellation?.Cancel();
            // Stop() faults the pending GetContextAsync (which ignores the token) so the accept
            // loop exits immediately. Active request registrations abort their responses.
            _listener?.Stop();

            bool listenerStopped = await WaitForCompletionAsync(listenerTask, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            bool handlersDrained = listenerStopped &&
                                   await _requestHandlers.DrainAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            if (listenerStopped && handlersDrained)
            {
                _listenerTask = null;
                _cts = null;
                cancellation?.Dispose();
            }

            return listenerStopped && handlersDrained;
        }
        catch (HttpListenerException)
        { }
        catch (OperationCanceledException)
        { }
        catch (NullReferenceException)
        { }
        catch (Exception)
        { }
        finally
        {
            _lifecycleGate.Release();
        }

        return false;
    }

    private async Task ProcessRequestsAsync(CancellationToken cancellationToken)
    {
        while (_listener.IsListening && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                HttpListenerContext context = await _listener.GetContextAsync();
                try
                {
                    HttpListenerContext acceptedContext = context;
                    await _requestHandlers.QueueAsync(token => HandleContextAsync(acceptedContext, token), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AbortContext(context);
                    break;
                }
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

    private static async Task<bool> WaitForCompletionAsync(Task task, TimeSpan timeout)
    {
        if (task == null)
            return true;

        Task completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != task)
            return false;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException ||
                                   ex is HttpListenerException ||
                                   ex is ObjectDisposedException)
        { }

        return true;
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

    private static bool IsCrossOriginBrowserRequest(HttpListenerRequest request)
    {
        return IsCrossOriginBrowserRequest(request.Url, request.Headers["Origin"], request.Headers["Referer"]);
    }

    private static bool IsSameOrigin(Uri browserUri, Uri requestUri)
    {
        return browserUri != null &&
               requestUri != null &&
               string.Equals(browserUri.Scheme, requestUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(browserUri.Host, requestUri.Host, StringComparison.OrdinalIgnoreCase) &&
               browserUri.Port == requestUri.Port;
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

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        // Backstop: any unhandled error while handling a request must still close the response.
        // Otherwise the client connection hangs until it times out — e.g. a JSON serialization
        // failure on a non-finite sensor value (NaN/Infinity), which System.Text.Json rejects.
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(state => AbortContext((HttpListenerContext)state), context);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchRequestAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
        { }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        { }
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

    private static void AbortContext(HttpListenerContext context)
    {
        try
        {
            context?.Response.Abort();
        }
        catch
        {
            // The response may already have completed or been aborted by the client.
        }
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
                            await SendJsonSensorAsync(context.Response, HandleGetSensorRequest(request.QueryString), cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(path, "/ResetAllMinMax", StringComparison.OrdinalIgnoreCase))
                        {
                            _rootElement.Accept(new SensorVisitor(delegate (ISensor sensor)
                            {
                                sensor.ResetMin();
                                sensor.ResetMax();
                            }));
                            await SendJsonAsync(context.Response, request, cancellationToken).ConfigureAwait(false);
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
        QuitAsync().GetAwaiter().GetResult();
    }

    public async Task QuitAsync()
    {
        if (PlatformNotSupported)
            return;

        await StopHttpListenerAsync().ConfigureAwait(false);
        try
        {
            _listener?.Abort();
        }
        catch { }

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

internal sealed class BoundedRequestHandlerPool
{
    private readonly HashSet<Task> _activeHandlers = new();
    private readonly object _activeHandlersLock = new();
    private readonly SemaphoreSlim _handlerSlots;
    private int _activeCount;
    private int _peakActiveCount;

    public BoundedRequestHandlerPool(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        MaxConcurrency = maxConcurrency;
        _handlerSlots = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public int MaxConcurrency { get; }

    internal int ActiveCount => Volatile.Read(ref _activeCount);

    internal int PeakActiveCount => Volatile.Read(ref _peakActiveCount);

    public async Task QueueAsync(Func<CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        await _handlerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);

        Task handlerTask;
        try
        {
            handlerTask = ExecuteAsync(handler, cancellationToken);
        }
        catch
        {
            _handlerSlots.Release();
            throw;
        }

        lock (_activeHandlersLock)
            _activeHandlers.Add(handlerTask);

        _ = handlerTask.ContinueWith(completedTask =>
        {
            // Observe a delegate fault even though production handlers contain their own
            // exception boundary. This keeps test/injected handlers from becoming unobserved.
            _ = completedTask.Exception;
            lock (_activeHandlersLock)
                _activeHandlers.Remove(completedTask);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public async Task<bool> DrainAsync(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            Task[] handlers;
            lock (_activeHandlersLock)
                handlers = _activeHandlers.ToArray();

            if (handlers.Length == 0)
                return true;

            TimeSpan remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                return false;

            Task allHandlers = Task.WhenAll(handlers);
            Task completed = await Task.WhenAny(allHandlers, Task.Delay(remaining)).ConfigureAwait(false);
            if (completed != allHandlers)
                return false;

            try
            {
                await allHandlers.ConfigureAwait(false);
            }
            catch
            {
                // Completion, rather than success, is the lifetime condition. Individual
                // failures are observed by the tracking continuation above.
            }
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        int activeCount = Interlocked.Increment(ref _activeCount);
        UpdatePeakActiveCount(activeCount);

        try
        {
            await handler(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _activeCount);
            _handlerSlots.Release();
        }
    }

    private void UpdatePeakActiveCount(int activeCount)
    {
        int observedPeak = Volatile.Read(ref _peakActiveCount);
        while (activeCount > observedPeak)
        {
            int priorPeak = Interlocked.CompareExchange(ref _peakActiveCount, activeCount, observedPeak);
            if (priorPeak == observedPeak)
                return;

            observedPeak = priorPeak;
        }
    }
}
