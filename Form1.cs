using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MMBLAI;

public partial class Form1 : Form
{
    private enum ModelSortMode
    {
        Name,
        Size
    }

    private Process? _llamaProcess;
    private string? _currentModelPath;
    private string? _currentModelName;
    private string? _llamaDir;
    private string? _modelDir;
    private bool _isClosing = false;
    private Icon? _appIcon;
    private int _runningModelIndex = -1;
    private int _lastSelectedIndex = -1;
    private ModelSortMode _modelSortMode = ModelSortMode.Name;

    private int _port = 7777;
    private int _apiPort = 7778;
    private int _reservedLlamaPort = 0;
    private System.Net.Sockets.TcpListener? _reservedLlamaListener;
    private bool _logChatInput = false;
    private bool _logChatOutput = false;
    private readonly Dictionary<int, string> _taggedRawByLine = new();
    private int _trimmedLinesOffset;
    private int _logLineCount;
    private Size _defaultWindowSize;
    private int _otherArgsBaseHeight = 28;
    // llama.cpp 真正监听的内部端口。7777 上的 _probeListener 保持运行，
    // /v1/models 由本程序直接返回统一格式，其余请求转发到这个内部端口。
    private int _llamaPort = 0;

    private HttpListener? _httpListener;
    private Task? _httpServerTask;
    private CancellationTokenSource? _httpServerCts;

    // Model-discovery listener bound to _port (7777 by default). It stays active
    // both before and during model execution, so http://127.0.0.1:7777/v1/models
    // always returns the same format. While llama.cpp runs it forwards all other
    // requests to the internal llama.cpp port.
    private HttpListener? _probeListener;
    private Task? _probeServerTask;
    private CancellationTokenSource? _probeServerCts;

    private static readonly HttpClient _proxyHttpClient = new HttpClient
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    // GPU memory info (in GB)
    private double _gpuTotalMemoryGB = 0;
    private double _gpuFreeMemoryGB = 0;
    private double _gpuUsedMemoryGB = 0;

    public Form1()
    {
        InitializeComponent();
        _otherArgsBaseHeight = txtOtherArgs.Height;
        LoadAppIcon();
        txtLog.MouseUp += TxtLog_MouseUp;
        this.DpiChanged += (s, e) => RecenterTextBoxes();
        this.Resize += (s, e) => RecenterTextBoxes();
    }

    // Suppress all system beep sounds
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;
    private const int WM_CHAR = 0x0102;
    private const int WM_SYSCHAR = 0x0106;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    protected override void WndProc(ref Message m)
    {
        // Suppress Alt/F10 key menu beep
        if (m.Msg == WM_SYSCOMMAND && (int)m.WParam == SC_KEYMENU)
        {
            return;
        }
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Suppress Enter key beep in read-only controls
        if (keyData == Keys.Enter)
        {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LoadAppIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = "MMBLAI.logo.ico";

        using (var stream = asm.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                _appIcon = new Icon(stream);
            }
        }

        if (_appIcon == null)
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
            if (File.Exists(iconPath))
            {
                _appIcon = new Icon(iconPath);
            }
        }

        if (_appIcon != null)
        {
            this.Icon = _appIcon;
            notifyIcon1.Icon = _appIcon;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isClosing)
        {
            StopCurrentModel();
            StopHttpServer();
            StopReservedLlamaListener();
            notifyIcon1.Visible = false;
            _appIcon?.Dispose();
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        this.Hide();
        this.ShowInTaskbar = false;
        notifyIcon1.ShowBalloonTip(3000, "MMBLAI", "程序已最小化到托盘，模型在后台运行。", ToolTipIcon.Info);
    }

    #region Form Events

    private void Form1_Load(object? sender, EventArgs e)
    {
        chkLogInput.Checked = true;
        chkLogOutput.Checked = true;
        _defaultWindowSize = this.Size;
        PositionLeftCenter();
        InitCustomScrollBars();
        UpdateTrayText();
        CenterTextBoxText();

        // Auto-detect available port pair for model server and model list server (support multi-instance)
        (_port, _apiPort, _reservedLlamaPort) = FindAvailablePortBlock();
        txtPort.Text = _port.ToString();
        StartReservedLlamaListener();
        // Detect GPU memory
        GetGpuMemoryInfo();
        if (_gpuTotalMemoryGB > 0)
        {
            Log(BuildGpuMemoryDisplay(true));
        }
        else
        {
            Log("未检测到GPU，使用CPU模式");
        }

        // Start HTTP API server for model list
        StartHttpServer();
        UpdatePortTooltip();

        // Auto-detect llama and model directories
        AutoDetectDirectories();
    }

    private void UpdatePortTooltip()
    {
        int internalPort = _llamaPort > 0 ? _llamaPort : _reservedLlamaPort;
        string tip = $"API端口: {_port}\r\n模型列表端口: {_apiPort}\r\n内部转发端口: {internalPort}";
        toolTip1.SetToolTip(txtPort, tip);
        toolTip1.SetToolTip(lblPort, tip);
    }

    private (int ModelPort, int ModelListPort, int LlamaPort) FindAvailablePortBlock()
    {
        int basePort = 7777;

        for (int block = 0; block < 100; block++)
        {
            int modelPort = basePort + (block * 3);
            int modelListPort = modelPort + 1;
            int llamaPort = modelPort + 2;

            if (llamaPort > 65535)
            {
                continue;
            }

            if (IsPortAvailable(modelPort) &&
                IsPortAvailable(modelListPort) &&
                IsPortAvailable(llamaPort))
            {
                return (modelPort, modelListPort, llamaPort);
            }
        }

        return (basePort, basePort + 1, basePort + 2);
    }

    private void StartReservedLlamaListener()
    {
        StopReservedLlamaListener();

        if (_reservedLlamaPort <= 0 || _reservedLlamaPort > 65535)
        {
            return;
        }

        try
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, _reservedLlamaPort);
            listener.Start();
            _reservedLlamaListener = listener;
        }
        catch (Exception ex)
        {
            _reservedLlamaListener = null;
            Log($"[端口] 内部转发端口 {_reservedLlamaPort} 预占失败: {ex.Message}");
        }
    }

    private void StopReservedLlamaListener()
    {
        if (_reservedLlamaListener == null)
        {
            return;
        }

        try
        {
            _reservedLlamaListener.Stop();
        }
        catch
        {
            // 端口可能已经释放。
        }

        _reservedLlamaListener = null;
    }

    private void StartHttpServer()
    {
        StopHttpServer();

        int apiPort = _apiPort;
        if (apiPort <= 0 || !IsPortAvailable(apiPort))
        {
            apiPort = FindApiPort();
        }
        if (apiPort <= 0)
        {
            Log("未找到可用端口，接口未启动");
            return;
        }

        _apiPort = apiPort;
        var portLog = $"API端口【{_port}】, 模型列表端口【{_apiPort}】, 内部转发端口【{_reservedLlamaPort}】";
        Log(portLog);
        Log($"API接口: http://127.0.0.1:{_port}/v1/chat/completions, 模型探测接口: http://127.0.0.1:{_port}/v1/models");
        Log($"模型列表接口: http://127.0.0.1:{_apiPort}/models");
        Log($"运行模型接口: http://127.0.0.1:{_apiPort}/run?model=xxx.gguf");
        Log($"运行模型参数示例: http://127.0.0.1:{_apiPort}/run?model=xxx.gguf&args=--cache-type-k%20q4_0%20--cache-type-v%20q4_0%20--ctx-size%202048，%20代表空格");

        _httpServerCts = new CancellationTokenSource();
        _httpServerTask = Task.Run(async () =>
        {
            var prefix = $"http://127.0.0.1:{apiPort}/";
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add(prefix);
                _httpListener.Start();
            }
            catch (Exception ex)
            {
                this.BeginInvoke(() => Log($"启动失败: {ex.Message}"));
                return;
            }

            while (!_httpServerCts.Token.IsCancellationRequested && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync().WaitAsync(_httpServerCts.Token);
                    if (_httpServerCts.Token.IsCancellationRequested) break;
                    _ = Task.Run(() => HandleHttpRequestAsync(context));
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }

            if (_httpListener?.IsListening == true)
            {
                try { _httpListener.Stop(); } catch { }
            }
            _httpListener?.Close();
        });

        StartProbeServer();
    }

    private int FindApiPort()
    {
        int basePort = _port + 1;
        if (basePort > 65535) basePort = 1;

        return FindAvailablePort(basePort);
    }

    private static int FindAvailablePort(int basePort)
    {
        if (basePort < 1 || basePort > 65535)
        {
            basePort = 7778;
        }

        for (int offset = 0; offset < 100; offset++)
        {
            int port = basePort + offset;
            if (port > 65535) port = 1 + offset;

            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        return -1;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpListener(IPAddress.Any, port);
            tcp.Start();
            tcp.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StopHttpServer()
    {
        _httpServerCts?.Cancel();

        if (_httpListener?.IsListening == true)
        {
            try { _httpListener.Stop(); } catch { }
        }

        if (_httpServerTask != null)
        {
            try { _httpServerTask.Wait(3000); } catch { }
            _httpServerTask = null;
        }

        _httpListener?.Close();
        _httpListener = null;

        _httpServerCts?.Dispose();
        _httpServerCts = null;

        StopProbeServer();
    }

    private void StartProbeServer()
    {
        StopProbeServer();

        int probePort = _port;
        if (probePort <= 0 || probePort > 65535)
        {
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{probePort}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Log($"[接口] {probePort} 模型探测服务启动失败: {ex.Message}");
            try { listener.Close(); } catch { }
            return;
        }

        _probeListener = listener;
        _probeServerCts = new CancellationTokenSource();
        var cts = _probeServerCts;

        _probeServerTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync().WaitAsync(cts.Token);
                    if (cts.Token.IsCancellationRequested) break;
                    _ = Task.Run(() => HandleProbeRequestAsync(context));
                }
                catch (OperationCanceledException) { break; }
                catch { break; }
            }

            if (listener.IsListening)
            {
                try { listener.Stop(); } catch { }
            }
            try { listener.Close(); } catch { }
        });
    }

    private void StopProbeServer()
    {
        _probeServerCts?.Cancel();

        if (_probeListener?.IsListening == true)
        {
            try { _probeListener.Stop(); } catch { }
        }

        if (_probeServerTask != null)
        {
            try { _probeServerTask.Wait(3000); } catch { }
            _probeServerTask = null;
        }

        try { _probeListener?.Close(); } catch { }
        _probeListener = null;

        _probeServerCts?.Dispose();
        _probeServerCts = null;
    }

    private void EnsureProbeServer()
    {
        if (_probeListener?.IsListening == true)
        {
            return;
        }

        StartProbeServer();
    }

    private async Task HandleProbeRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization");
                response.StatusCode = 200;
                response.ContentLength64 = 0;
                return;
            }

            if (request.HttpMethod == "GET" &&
                (request.Url?.AbsolutePath.Equals("/v1/models", StringComparison.OrdinalIgnoreCase) == true ||
                 request.Url?.AbsolutePath.Equals("/models", StringComparison.OrdinalIgnoreCase) == true))
            {
                await SendJsonAsync(response, 200, BuildV1ModelsJson());
            }
            else if (_llamaPort > 0)
            {
                await ForwardToLlamaAsync(request, response, _llamaPort);
            }
            else
            {
                await SendJsonAsync(response, 404, "{\"error\":\"Not found\"}");
            }
        }
        catch (Exception ex)
        {
            try
            {
                await SendJsonAsync(response, 500, $"{{\"error\":\"{ex.Message}\"}}");
            }
            catch { }
        }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }

    private async Task ForwardToLlamaAsync(HttpListenerRequest request, HttpListenerResponse response, int llamaPort)
    {
        var pathAndQuery = request.Url?.PathAndQuery;
        if (string.IsNullOrEmpty(pathAndQuery))
        {
            pathAndQuery = "/";
        }

        var upstreamUri = new Uri($"http://127.0.0.1:{llamaPort}{pathAndQuery}");
        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), upstreamUri);

        byte[]? body = null;
        if (request.HasEntityBody)
        {
            using var bodyStream = new MemoryStream();
            await request.InputStream.CopyToAsync(bodyStream);
            body = bodyStream.ToArray();
            upstreamRequest.Content = new ByteArrayContent(body);
        }

        var absolutePath = request.Url?.AbsolutePath ?? string.Empty;
        bool isChatCompletions =
            request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            absolutePath.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase);
        bool isEmbeddingRequest =
            request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            absolutePath.EndsWith("/v1/embeddings", StringComparison.OrdinalIgnoreCase);
        bool isRerankerRequest =
            request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            absolutePath.EndsWith("/v1/rerank", StringComparison.OrdinalIgnoreCase);

        bool logInput = _logChatInput && (isChatCompletions || isEmbeddingRequest || isRerankerRequest);
        bool logOutput = _logChatOutput && (isChatCompletions || isEmbeddingRequest || isRerankerRequest);

        if (logInput && body != null)
        {
            var promptText = Encoding.UTF8.GetString(body);
            if (isEmbeddingRequest)
            {
                LogEmbeddingInput(promptText);
            }
            else if (isRerankerRequest)
            {
                LogRerankerInput(promptText);
            }
            else
            {
                LogClickableRaw("[显示输入]", promptText);
            }
        }

        foreach (string headerName in request.Headers)
        {
            if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                headerName.Equals("Expect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerValue = request.Headers[headerName];
            if (string.IsNullOrEmpty(headerValue))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(headerName, headerValue))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(headerName, headerValue);
            }
        }

        if (upstreamRequest.Content != null)
        {
            if (!string.IsNullOrEmpty(request.ContentType))
            {
                upstreamRequest.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
            }

            if (request.ContentLength64 > 0)
            {
                upstreamRequest.Content.Headers.ContentLength = request.ContentLength64;
            }
        }

        using var upstreamResponse = await _proxyHttpClient.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode = (int)upstreamResponse.StatusCode;
        response.AddHeader("Access-Control-Allow-Origin", "*");

        foreach (var header in upstreamResponse.Headers)
        {
            if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers[header.Key] = string.Join(", ", header.Value);
        }

        if (upstreamResponse.Content.Headers.ContentLength.HasValue)
        {
            response.ContentLength64 = upstreamResponse.Content.Headers.ContentLength.Value;
        }
        else
        {
            response.SendChunked = true;
        }

        if (upstreamResponse.Content.Headers.ContentType != null)
        {
            response.ContentType = upstreamResponse.Content.Headers.ContentType.ToString();
        }

        var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync();
        bool isEventStream = upstreamResponse.Content.Headers.ContentType?.MediaType
            ?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true;

        if (logOutput && isEventStream)
        {
            using var reader = new StreamReader(upstreamStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            string? aggregateId = null;
            string? aggregateModel = null;
            string? aggregateObject = null;
            string? aggregateSystemFingerprint = null;
            string? aggregateFinishReason = null;
            long aggregateCreated = 0;
            var aggregateContent = new StringBuilder();
            var aggregateReasoningContent = new StringBuilder();
            var toolCalls = new Dictionary<int, (string? Id, string? Type, string? Name, StringBuilder Arguments)>();

            bool aggregateLogged = false;

            void LogAggregatedOutputIfAny()
            {
                if (aggregateLogged)
                {
                    return;
                }

                if (aggregateContent.Length == 0 &&
                    aggregateReasoningContent.Length == 0 &&
                    toolCalls.Count == 0 &&
                    aggregateId == null)
                {
                    return;
                }

                var aggregatedToolCalls = toolCalls
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => (Index: kvp.Key, kvp.Value.Id, kvp.Value.Type, kvp.Value.Name, Arguments: kvp.Value.Arguments.ToString()))
                    .ToList();

                var aggregatedOutput = BuildAggregatedOutputJson(
                    aggregateId,
                    aggregateModel,
                    aggregateObject,
                    aggregateSystemFingerprint,
                    aggregateFinishReason,
                    aggregateCreated,
                    aggregateContent.ToString(),
                    aggregateReasoningContent.ToString(),
                    aggregatedToolCalls);
                LogClickableRaw("[聚合输出]", aggregatedOutput);
                aggregateLogged = true;
            }

            while (await reader.ReadLineAsync() is { } line)
            {
                bool isDone = false;

                if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = line.Substring(5).Trim();
                    if (payload == "[DONE]")
                    {
                        aggregateFinishReason ??= "stop";
                        isDone = true;
                    }
                    else
                    {
                        try
                        {
                            using var chunk = JsonDocument.Parse(payload);
                            var root = chunk.RootElement;

                            if (aggregateId == null && root.TryGetProperty("id", out var idElement))
                            {
                                aggregateId = idElement.GetString();
                            }
                            if (aggregateModel == null && root.TryGetProperty("model", out var modelElement))
                            {
                                aggregateModel = modelElement.GetString();
                            }
                            if (aggregateObject == null && root.TryGetProperty("object", out var objectElement))
                            {
                                aggregateObject = objectElement.GetString();
                            }
                            if (aggregateSystemFingerprint == null && root.TryGetProperty("system_fingerprint", out var fingerprintElement))
                            {
                                aggregateSystemFingerprint = fingerprintElement.GetString();
                            }
                            if (aggregateCreated == 0 && root.TryGetProperty("created", out var createdElement) && createdElement.TryGetInt64(out long created))
                            {
                                aggregateCreated = created;
                            }

                            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                            {
                                var choice = choices[0];
                                if (choice.TryGetProperty("delta", out var delta))
                                {
                                    if (delta.TryGetProperty("content", out var contentElement) &&
                                        contentElement.ValueKind == JsonValueKind.String)
                                    {
                                        aggregateContent.Append(contentElement.GetString());
                                    }

                                    if (delta.TryGetProperty("reasoning_content", out var reasoningElement) &&
                                        reasoningElement.ValueKind == JsonValueKind.String)
                                    {
                                        aggregateReasoningContent.Append(reasoningElement.GetString());
                                    }

                                    if (delta.TryGetProperty("tool_calls", out var toolCallsElement) &&
                                        toolCallsElement.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var toolCall in toolCallsElement.EnumerateArray())
                                        {
                                            if (!toolCall.TryGetProperty("index", out var indexElement) ||
                                                !indexElement.TryGetInt32(out int callIndex))
                                            {
                                                continue;
                                            }

                                            if (!toolCalls.TryGetValue(callIndex, out var accumulated))
                                            {
                                                accumulated = (null, null, null, new StringBuilder());
                                                toolCalls[callIndex] = accumulated;
                                            }

                                            if (toolCall.TryGetProperty("id", out var callIdElement))
                                            {
                                                accumulated.Id ??= callIdElement.GetString();
                                            }
                                            if (toolCall.TryGetProperty("type", out var callTypeElement))
                                            {
                                                accumulated.Type ??= callTypeElement.GetString();
                                            }

                                            if (toolCall.TryGetProperty("function", out var functionElement))
                                            {
                                                if (functionElement.TryGetProperty("name", out var functionNameElement))
                                                {
                                                    accumulated.Name ??= functionNameElement.GetString();
                                                }
                                                if (functionElement.TryGetProperty("arguments", out var argumentsElement) &&
                                                    argumentsElement.ValueKind == JsonValueKind.String)
                                                {
                                                    accumulated.Arguments.Append(argumentsElement.GetString());
                                                }
                                            }

                                            toolCalls[callIndex] = accumulated;
                                        }
                                    }
                                }

                                if (aggregateFinishReason == null &&
                                    choice.TryGetProperty("finish_reason", out var finishElement) &&
                                    finishElement.ValueKind == JsonValueKind.String)
                                {
                                    aggregateFinishReason = finishElement.GetString();
                                }
                            }
                        }
                        catch
                        {
                            // 非 JSON 的 data 行不参与聚合。
                        }
                    }
                }

                if (isDone)
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        LogOutputLine(line);
                    }
                    LogAggregatedOutputIfAny();

                    var doneLineBytes = Encoding.UTF8.GetBytes(line + "\n");
                    await response.OutputStream.WriteAsync(doneLineBytes, 0, doneLineBytes.Length);
                    await response.OutputStream.FlushAsync();
                    break;
                }

                var lineBytes = Encoding.UTF8.GetBytes(line + "\n");
                await response.OutputStream.WriteAsync(lineBytes, 0, lineBytes.Length);
                await response.OutputStream.FlushAsync();

                if (!string.IsNullOrEmpty(line))
                {
                    LogOutputLine(line);
                }
            }

            LogAggregatedOutputIfAny();
        }
        else if (logOutput && isEmbeddingRequest)
        {
            using var reader = new StreamReader(upstreamStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var outputText = await reader.ReadToEndAsync();
            var outputBytes = Encoding.UTF8.GetBytes(outputText);
            await response.OutputStream.WriteAsync(outputBytes, 0, outputBytes.Length);
            await response.OutputStream.FlushAsync();

            if (!string.IsNullOrWhiteSpace(outputText))
            {
                LogEmbeddingOutput(outputText);
            }
        }
        else if (logOutput && isRerankerRequest)
        {
            using var reader = new StreamReader(upstreamStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var outputText = await reader.ReadToEndAsync();
            var outputBytes = Encoding.UTF8.GetBytes(outputText);
            await response.OutputStream.WriteAsync(outputBytes, 0, outputBytes.Length);
            await response.OutputStream.FlushAsync();

            if (!string.IsNullOrWhiteSpace(outputText))
            {
                LogRerankerOutput(outputText);
            }
        }
        else if (logOutput && isChatCompletions)
        {
            using var reader = new StreamReader(upstreamStream, Encoding.UTF8, false, 1024, leaveOpen: true);
            var outputText = await reader.ReadToEndAsync();
            var outputBytes = Encoding.UTF8.GetBytes(outputText);
            await response.OutputStream.WriteAsync(outputBytes, 0, outputBytes.Length);
            await response.OutputStream.FlushAsync();

            if (!string.IsNullOrWhiteSpace(outputText))
            {
                LogOutputLine(outputText);
                LogClickableRaw("[聚合输出]", outputText);
            }
        }
        else
        {
            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await upstreamStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
            {
                await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                await response.OutputStream.FlushAsync();
            }
        }
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // CORS 预检请求
            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                response.StatusCode = 200;
                response.ContentLength64 = 0;
                return;
            }

            if (request.Url?.AbsolutePath.Equals("/models", StringComparison.OrdinalIgnoreCase) == true && request.HttpMethod == "GET")
            {
                var json = BuildModelListJson();
                await SendJsonAsync(response, 200, json);
            }
            else if (request.Url?.AbsolutePath.Equals("/v1/models", StringComparison.OrdinalIgnoreCase) == true && request.HttpMethod == "GET")
            {
                var json = BuildV1ModelsJson();
                await SendJsonAsync(response, 200, json);
            }
            else if (request.Url?.AbsolutePath.Equals("/current", StringComparison.OrdinalIgnoreCase) == true && request.HttpMethod == "GET")
            {
                string json = BuildCurrentModelJson();
                await SendJsonAsync(response, 200, json);
            }
            else if (request.Url?.AbsolutePath.Equals("/run", StringComparison.OrdinalIgnoreCase) == true
                     && request.HttpMethod == "GET")
            {
                await HandleRunRequestAsync(context);
            }
            else
            {
                await SendJsonAsync(response, 404, "{\"error\":\"Not found\"}");
            }
        }
        catch (Exception ex)
        {
            try
            {
                await SendJsonAsync(response, 500, $"{{\"error\":\"{ex.Message}\"}}");
            }
            catch { }
        }
        finally
        {
            try { response.OutputStream.Close(); } catch { }
        }
    }

    private static async Task SendJsonAsync(HttpListenerResponse response, int statusCode, string json)
    {
        response.StatusCode = statusCode;
        var outputJson = TryFormatJson(json) ?? json;
        var buffer = Encoding.UTF8.GetBytes(outputJson);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.AddHeader("Access-Control-Allow-Origin", "*");
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
    }

    private static string BuildRunResponseJson(bool success, string message)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteBoolean("success", success);
        writer.WriteString("message", message);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 处理 /run 接口：强行停止当前模型（若有），然后启动指定模型。
    /// 调用方式：GET /run?model=xxx.gguf&args=--cache-type-k%20q4_0%20--ctx-size%202048
    /// </summary>
    private async Task HandleRunRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // 从 query string 解析模型名和额外参数
        string? modelName = request.QueryString["model"];
        string? extraArgs = request.QueryString["args"];

        if (string.IsNullOrWhiteSpace(modelName))
        {
            Log("[接口] /run 请求缺少模型名参数");
            await SendJsonAsync(response, 400, BuildRunResponseJson(false, "缺少模型名参数"));
            return;
        }

        modelName = modelName.Trim();
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            extraArgs = Uri.UnescapeDataString(extraArgs).Trim();
        }

        // 查找模型文件
        string? modelPath = null;
        if (!string.IsNullOrEmpty(_modelDir) && Directory.Exists(_modelDir))
        {
            var directPath = Path.Combine(_modelDir, modelName);
            if (File.Exists(directPath))
            {
                modelPath = directPath;
            }
            else
            {
                modelPath = FindModelPath(_modelDir, modelName);
            }
        }

        if (modelPath == null || !File.Exists(modelPath))
        {
            Log($"[接口] 模型运行失败，无此模型: {modelName}");
            await SendJsonAsync(response, 404, BuildRunResponseJson(false, $"模型运行失败，无此模型: {modelName}"));
            return;
        }

        // 停止当前模型（同步等待其退出）
        string logMsg = $"[接口] 收到运行请求: {modelName}";
        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            logMsg += $"，额外参数: {extraArgs}";
        }
        logMsg += "，正在停止当前模型...";
        Log(logMsg);
        await StopCurrentModelAsync();

        // 启动新模型（必须在 UI 线程执行）
        if (this.IsDisposed)
        {
            await SendJsonAsync(response, 500, BuildRunResponseJson(false, "模型运行失败: 程序正在关闭"));
            return;
        }

        Exception? launchError = null;
        try
        {
            this.Invoke(new Action(() => LaunchModel(modelPath!, extraArgs)));
        }
        catch (Exception ex)
        {
            launchError = ex;
        }

        if (launchError != null)
        {
            Log($"[接口] 模型启动异常: {launchError.Message}");
            await SendJsonAsync(response, 500, BuildRunResponseJson(false, $"模型运行失败: {modelName}（{launchError.Message}）"));
            return;
        }

        // 等待几秒确认进程稳定（加载失败的进程通常会在数秒内退出）
        await Task.Delay(3000);

        bool isRunning;
        try
        {
            isRunning = _llamaProcess != null && !_llamaProcess.HasExited;
        }
        catch
        {
            isRunning = false;
        }

        if (isRunning)
        {
            Log($"[接口] 模型 {modelName} 运行成功");
            await SendJsonAsync(response, 200, BuildRunResponseJson(true, $"模型 {modelName} 运行成功"));
        }
        else
        {
            Log($"[接口] 模型 {modelName} 运行失败（进程已退出）");
            await SendJsonAsync(response, 500, BuildRunResponseJson(false, $"模型运行失败: {modelName}（进程已退出）"));
        }
    }

    private string BuildCurrentModelJson()
    {
        if (_currentModelPath == null || !File.Exists(_currentModelPath))
        {
            return "{\"model\":null,\"status\":\"stopped\"}";
        }

        var modelName = Path.GetFileName(_currentModelPath);
        var isRunning = _llamaProcess != null && !_llamaProcess.HasExited;
        var status = isRunning ? "running" : "stopped";

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("model", modelName);
        writer.WriteString("status", status);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private string BuildModelListJson()
    {
        if (string.IsNullOrEmpty(_modelDir) || !Directory.Exists(_modelDir))
        {
            return "[]";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();

            var modelFiles = Directory.GetFiles(_modelDir, "*.gguf", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = path,
                    FileName = Path.GetFileName(path),
                    Size = new FileInfo(path).Length
                })
                .OrderBy(m => GetModelGroupKey(m.FileName), StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Size)
                .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var model in modelFiles)
            {
                var fileName = Path.GetFileName(model.Path);

                writer.WriteStartObject();
                writer.WriteString("name", fileName);
                writer.WriteString("path", model.Path);
                writer.WriteNumber("size", model.Size);
                writer.WriteString("sizeDisplay", FormatFileSize(model.Size));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 生成 OpenAI 兼容的 /v1/models 响应，供 VS Code 等客户端探测可用的模型。
    /// 返回格式与 OpenAI /v1/models 一致：{ object: "list", data: [ { id, object, created, owned_by } ] }
    /// </summary>
    private string BuildV1ModelsJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("object", "list");

            writer.WritePropertyName("data");
            writer.WriteStartArray();

            if (!string.IsNullOrEmpty(_modelDir) && Directory.Exists(_modelDir))
            {
                var modelFiles = Directory.GetFiles(_modelDir, "*.gguf", SearchOption.AllDirectories)
                    .Select(path => new
                    {
                        Path = path,
                        FileName = Path.GetFileName(path),
                        Size = new FileInfo(path).Length
                    })
                    .OrderBy(m => GetModelGroupKey(m.FileName), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Size)
                    .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var model in modelFiles)
                {
                    var modelId = Path.GetFileNameWithoutExtension(Path.GetFileName(model.Path));

                    writer.WriteStartObject();
                    writer.WriteString("id", modelId);
                    writer.WriteString("object", "model");
                    writer.WriteNumber("created", 0);
                    writer.WriteString("owned_by", "mmblai");
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void AutoDetectDirectories()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;

        // Check for llama directory
        var llamaDir = Path.Combine(appDir, "llama");
        if (Directory.Exists(llamaDir))
        {
            _llamaDir = llamaDir;
            txtLlamaDir.Text = llamaDir;
            ValidateLlamaDir();
        }

        // Check for model directory
        var modelDir = Path.Combine(appDir, "model");
        if (Directory.Exists(modelDir))
        {
            _modelDir = modelDir;
            txtModelDir.Text = modelDir;
            ScanModels();
        }
    }

    private void PositionLeftCenter()
    {
        var primaryScreen = Screen.PrimaryScreen;
        if (primaryScreen == null) return;
        var screen = primaryScreen.WorkingArea;
        int x = 20;
        int y = (screen.Height - this.Height) / 2;
        this.Location = new Point(x, Math.Max(0, y));
    }

    #endregion

    #region Directory Selection

    private void txtLlamaDir_Click(object? sender, EventArgs e)
    {
        OpenDirectoryInExplorer(_llamaDir);
    }

    private void txtModelDir_Click(object? sender, EventArgs e)
    {
        OpenDirectoryInExplorer(_modelDir);
    }

    private void OpenDirectoryInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Log("[提示] 尚未选择目录");
            return;
        }

        if (!Directory.Exists(path))
        {
            Log($"[提示] 目录不存在: {path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"[错误] 打开资源管理器失败: {ex.Message}");
        }
    }

    private void btnLlamaDir_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 llama.cpp 工具目录",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrEmpty(_llamaDir) && Directory.Exists(_llamaDir))
        {
            dialog.InitialDirectory = _llamaDir;
        }
        else if (Directory.Exists("D:\\"))
        {
            dialog.InitialDirectory = "D:\\";
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _llamaDir = dialog.SelectedPath;
            txtLlamaDir.Text = _llamaDir;
            ValidateLlamaDir();
        }
    }

    private void btnModelDir_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择模型文件目录",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrEmpty(_modelDir) && Directory.Exists(_modelDir))
        {
            dialog.InitialDirectory = _modelDir;
        }
        else if (Directory.Exists("D:\\"))
        {
            dialog.InitialDirectory = "D:\\";
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _modelDir = dialog.SelectedPath;
            txtModelDir.Text = _modelDir;
            ScanModels();
        }
    }

    private void ValidateLlamaDir()
    {
        if (string.IsNullOrEmpty(_llamaDir)) return;
        var serverExe = FindLlamaServer(_llamaDir);
        if (serverExe == null)
        {
            Log($"[警告] 未找到llama-server.exe");
        }
    }

    private string? FindLlamaServer(string dir)
    {
        var files = Directory.GetFiles(dir, "llama-server*.exe", SearchOption.TopDirectoryOnly);
        return files.FirstOrDefault();
    }

    #endregion

    #region Model Scanning

    private void LblModels_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_modelDir) || !Directory.Exists(_modelDir))
        {
            return;
        }

        _modelSortMode = _modelSortMode == ModelSortMode.Name
            ? ModelSortMode.Size
            : ModelSortMode.Name;

        ScanModels();
    }

    private void ScanModels()
    {
        lstModels.Items.Clear();
        lblModels.Text = "模型列表（0）";

        if (string.IsNullOrEmpty(_modelDir) || !Directory.Exists(_modelDir))
        {
            return;
        }

        var models = Directory.GetFiles(_modelDir, "*.gguf", SearchOption.AllDirectories);

        if (models.Length == 0)
        {
            return;
        }

        if (_modelSortMode == ModelSortMode.Size)
        {
            // 1. All models by file size, from small to large.
            Array.Sort(models, (a, b) =>
            {
                int cmp = new FileInfo(a).Length.CompareTo(new FileInfo(b).Length);
                if (cmp != 0) return cmp;

                return StringComparer.OrdinalIgnoreCase.Compare(
                    Path.GetFileName(a),
                    Path.GetFileName(b));
            });
        }
        else
        {
            // 2. Model name/type, from small to large. Same model family is
            //    grouped together, then sorted by size.
            Array.Sort(models, (a, b) =>
            {
                int cmp = StringComparer.OrdinalIgnoreCase.Compare(
                    GetModelGroupKey(Path.GetFileName(a)),
                    GetModelGroupKey(Path.GetFileName(b)));
                if (cmp != 0) return cmp;

                cmp = new FileInfo(a).Length.CompareTo(new FileInfo(b).Length);
                if (cmp != 0) return cmp;

                return StringComparer.OrdinalIgnoreCase.Compare(
                    Path.GetFileName(a),
                    Path.GetFileName(b));
            });
        }

        foreach (var modelPath in models)
        {
            string sizeDisplay = FormatFileSizeShort(new FileInfo(modelPath).Length);
            lstModels.Items.Add($"{sizeDisplay}  {Path.GetFileName(modelPath)}");
        }

        lblModels.Text = $"模型列表（{lstModels.Items.Count}）";

        // Reset running model index after re-scan
        if (_currentModelPath != null)
        {
            var runningFileName = Path.GetFileName(_currentModelPath);
            _runningModelIndex = -1;
            for (int i = 0; i < lstModels.Items.Count; i++)
            {
                var itemText = lstModels.Items[i]?.ToString();
                if (itemText != null && itemText.Contains(runningFileName))
                {
                    _runningModelIndex = i;
                    break;
                }
            }
            lstModels.Invalidate();
        }
    }

    private static string GetModelGroupKey(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);

        // Common quantization suffixes look like:
        //   Model-Q4_K_M.gguf, Model.Q5_K_M.gguf, Model-Q8_0.gguf
        // Group by the base model name so variants sort together, then by size.
        var match = Regex.Match(
            stem,
            @"(?<=[-.])(Q\d(?:[._-][A-Za-z0-9_]+)*)$",
            RegexOptions.IgnoreCase);

        return match.Success
            ? stem.Substring(0, match.Index - 1)
            : stem;
    }

    private static string FormatFileSizeShort(long bytes)
    {
        string size;
        if (bytes >= 1L << 30) size = $"{bytes / (double)(1L << 30):F0}G";
        else if (bytes >= 1L << 20) size = $"{bytes / (double)(1L << 20):F0}M";
        else if (bytes >= 1L << 10) size = $"{bytes / (double)(1L << 10):F0}K";
        else size = $"{bytes}B";
        
        return size.PadLeft(5);  // Fixed width for alignment
    }

    private int CountModels()
    {
        if (string.IsNullOrEmpty(_modelDir) || !Directory.Exists(_modelDir))
        {
            return 0;
        }

        return Directory.GetFiles(_modelDir, "*.gguf", SearchOption.AllDirectories).Length;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1} KB";
        return $"{bytes} B";
    }

    #endregion

    #region Model Selection & Launch

    private void lstModels_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Store new selected index for reference, no need to force redraw
        // The listbox automatically updates selection visuals
        _lastSelectedIndex = lstModels.SelectedIndex;
    }

    private void lstModels_DoubleClick(object? sender, EventArgs e)
    {
        if (lstModels.SelectedIndex < 0) return;

        if (_llamaProcess != null && !_llamaProcess.HasExited)
        {
            Log("[警告] 请先终止当前模型再启动新模型");
            return;
        }

        var selectedItem = lstModels.SelectedItem?.ToString();
        if (selectedItem == null || string.IsNullOrEmpty(_modelDir)) return;

        // New format: "   4G  xxx.gguf" - extract filename after the last "  " separator
        var modelName = selectedItem;
        int separatorIdx = selectedItem.LastIndexOf("  ");
        if (separatorIdx >= 0)
        {
            modelName = selectedItem.Substring(separatorIdx + 2);
        }

        var modelPath = Path.Combine(_modelDir, modelName);

        if (!File.Exists(modelPath))
        {
            modelPath = FindModelPath(_modelDir, modelName);
        }

        if (modelPath == null)
        {
            Log($"[错误] 找不到模型文件: {modelName}");
            return;
        }

        LaunchModel(modelPath);
    }

    private string? FindModelPath(string baseDir, string fileName)
    {
        var files = Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories);
        return files.FirstOrDefault();
    }

    private void LaunchModel(string modelPath, string? extraArgs = null)
    {
        // Clear log before launching new model
        txtLog.Clear();
        _taggedRawByLine.Clear();
        _trimmedLinesOffset = 0;
        _logLineCount = 0;

        if (string.IsNullOrEmpty(_llamaDir))
        {
            Log("[错误] 请先选择llama.cpp目录");
            return;
        }

        var serverExe = FindLlamaServer(_llamaDir);
        if (serverExe == null)
        {
            Log("[错误] 在llama.cpp目录中未找到llama-server.exe");
            return;
        }

        EnsureProbeServer();

        StopReservedLlamaListener();

        int llamaPort = _reservedLlamaPort;
        if (llamaPort <= 0 || !IsPortAvailable(llamaPort))
        {
            llamaPort = FindAvailablePort(_apiPort + 1);
        }

        if (llamaPort <= 0)
        {
            Log("[错误] 未找到可用的内部模型端口，无法启动模型");
            return;
        }

        if (!IsPortAvailable(llamaPort))
        {
            Log($"[错误] 端口 {llamaPort} 已被占用，无法启动模型");
            return;
        }

        _llamaPort = llamaPort;
        UpdatePortTooltip();
        _currentModelPath = modelPath;
        var modelSizeGB = new FileInfo(modelPath).Length / (double)(1L << 30);
        var modelName = Path.GetFileName(modelPath);
        bool isEmbeddingModel = modelName.Contains("Embedding", StringComparison.OrdinalIgnoreCase);
        bool isRerankerModel = modelName.Contains("Reranker", StringComparison.OrdinalIgnoreCase);

        // Re-detect GPU memory before launching model for accurate available memory
        GetGpuMemoryInfo();

        int gpuLayers;
        if (!string.IsNullOrWhiteSpace(txtGpuLayers.Text) && int.TryParse(txtGpuLayers.Text, out int userLayers) && userLayers >= 0)
        {
            gpuLayers = userLayers;
        }
        else
        {
            gpuLayers = CalculateGpuLayers(modelSizeGB);
        }

        double neededRamGB = modelSizeGB * 1.1;
        var availableRamGB = GetAvailableRamGB();

        string modelType;
        if (isRerankerModel)
            modelType = "Reranker重排序";
        else if (isEmbeddingModel)
            modelType = "Embedding";
        else
            modelType = "聊天";
        Log($"[启动模型] {modelName} ({modelSizeGB:F1} GB) - {modelType}模型");
        if (_gpuTotalMemoryGB > 0)
        {
            double estimatedVramGB = modelSizeGB * 1.2;
            string gpuMode;
            if (gpuLayers == -1)
                gpuMode = "显存充足，模型完全加载到显存";
            else if (gpuLayers == 0)
                gpuMode = _gpuFreeMemoryGB < 1.5 ? "可用显存不足1.5G，使用纯CPU运行" : "显存不足以放下足够层数，使用纯CPU运行更稳定";
            else
                gpuMode = $"显存不足，仅前{gpuLayers}层放GPU，其余放内存";

            var gpuLog = $"[GPU显存] {BuildGpuMemoryDisplay(true)}，模型需{estimatedVramGB:F1}G 含KV缓存，{gpuMode}";
            if (gpuLayers != 0)
            {
                gpuLog += $"：GPU层数: {gpuLayers}";
            }
            Log(gpuLog);
        }
        else
        {
            Log($"[配置] GPU层数: {gpuLayers} ({(gpuLayers == -1 ? "全部" : gpuLayers == 0 ? "纯CPU" : $"前{gpuLayers}层")})");
        }

        if (availableRamGB > 0 && neededRamGB > availableRamGB)
        {
            Log($"[警告] 内存不足（需要 {neededRamGB:F0}GB，可用 {availableRamGB:F0}GB）");
        }

        var combinedExtraArgs = CombineExtraArgs(txtOtherArgs.Text, extraArgs);
        var arguments = BuildArguments(modelPath, gpuLayers, isEmbeddingModel, isRerankerModel, combinedExtraArgs);
        UpdateCtxFromRunArgs(extraArgs);
        Log($"[命令] {arguments}");

        Process? newProcess = null;

        try
        {
            newProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _llamaDir,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };

            newProcess.OutputDataReceived += LlamaProcess_OutputDataReceived;
            newProcess.ErrorDataReceived += LlamaProcess_ErrorDataReceived;
            newProcess.Exited += LlamaProcess_Exited;

            _currentModelName = modelName;
            newProcess.Start();
            newProcess.BeginOutputReadLine();
            newProcess.BeginErrorReadLine();

            _llamaProcess = newProcess;
            newProcess = null;

            UpdateStopButtonState(true);
            btnLlamaDir.Enabled = false;
            btnModelDir.Enabled = false;
            txtPort.Enabled = false;
            btnSetPort.Enabled = false;
            txtGpuLayers.Enabled = false;
            txtCtx.Enabled = false;
            txtOtherArgs.Enabled = false;
            lblRunningModel.Text = $"正在运行【{modelName}】";
            
            // Find and remember the index of the running model in the list
            _runningModelIndex = -1;
            for (int i = 0; i < lstModels.Items.Count; i++)
            {
                var itemText = lstModels.Items[i]?.ToString();
                if (itemText != null && itemText.Contains(modelName))
                {
                    _runningModelIndex = i;
                    break;
                }
            }
            
            // Refresh the list to update the visual highlight
            lstModels.Invalidate();
            UpdateTrayText();
            
        }
        catch (Exception ex)
        {
            newProcess?.Dispose();
            Log($"[错误] 启动失败: {ex.Message}");
            _currentModelPath = null;
            _currentModelName = null;
            _llamaPort = 0;
            UpdatePortTooltip();
            EnsureProbeServer();
            StartReservedLlamaListener();
            UpdateTrayText();
        }
    }

    private void UpdateTrayText()
    {
        if (notifyIcon1 == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentModelName))
        {
            notifyIcon1.Text = "";
            return;
        }

        string modelType;
        if (_currentModelName.Contains("Reranker", StringComparison.OrdinalIgnoreCase))
        {
            modelType = "Reranker";
        }
        else if (_currentModelName.Contains("Embedding", StringComparison.OrdinalIgnoreCase))
        {
            modelType = "Embedding";
        }
        else
        {
            modelType = "聊天";
        }

        notifyIcon1.Text = $"{modelType}模型运行中";
    }

    private int CalculateGpuLayers(double modelSizeGB)
    {
        // No GPU detected or no free memory, use CPU only
        if (_gpuFreeMemoryGB <= 0) return 0;

        // Estimate VRAM needed: model size + 20% overhead for context and KV cache
        double estimatedVramGB = modelSizeGB * 1.2;
        double usableVramGB = _gpuFreeMemoryGB - 0.3; // Leave 0.3GB buffer

        // If model fits entirely in available GPU memory
        if (estimatedVramGB < usableVramGB)
        {
            return -1; // Use all layers on GPU
        }

        // If available VRAM is too small (< 1.5GB), partial offloading won't help much, use CPU only
        if (usableVramGB < 1.5)
        {
            return 0;
        }

        // Calculate ratio of GPU memory available vs needed
        double availableRatio = usableVramGB / estimatedVramGB;

        // Typical modern LLM has 32-80 layers, estimate layers based on memory ratio
        int estimatedTotalLayers = modelSizeGB switch
        {
            < 4 => 32,
            < 8 => 40,
            < 16 => 48,
            < 32 => 60,
            _ => 80
        };

        int gpuLayers = (int)(estimatedTotalLayers * availableRatio);

        // If less than 8 layers can fit on GPU, just use CPU for better stability
        if (gpuLayers < 8)
        {
            return 0;
        }

        // Clamp to reasonable range (leave at least 10% layers on CPU if partial)
        int maxPartialLayers = (int)(estimatedTotalLayers * 0.95);
        return Math.Min(gpuLayers, maxPartialLayers);
    }

    private static Dictionary<string, string?> ParseCommandArguments(string args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(args))
            return result;

        var parts = TokenizeCommandArguments(args);
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (!IsOptionToken(part))
                continue;

            var key = part;
            string? value = null;

            // 支持 --key=value、-k=value 写法。
            var eqIndex = part.IndexOf('=');
            if (eqIndex > 0)
            {
                key = part.Substring(0, eqIndex);
                value = TrimMatchingQuotes(part.Substring(eqIndex + 1));
            }
            else if (i + 1 < parts.Count && !IsOptionToken(parts[i + 1]))
            {
                // 支持 --key value、-k value 写法。
                value = TrimMatchingQuotes(parts[i + 1]);
                i++;
            }

            result[key] = value;
        }

        return result;
    }

    private static List<string> TokenizeCommandArguments(string args)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quoteChar = null;

        foreach (var ch in args)
        {
            if (quoteChar.HasValue)
            {
                if (ch == quoteChar.Value)
                {
                    quoteChar = null;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"' || ch == '\'')
            {
                quoteChar = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static bool IsOptionToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token[0] != '-' || token.Length == 1)
        {
            return false;
        }

        // -1、-99 这类负数值应作为上一个选项的值，而不是新选项。
        return !char.IsDigit(token[1]);
    }

    private static string CanonicalizeArgumentKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return key.ToLowerInvariant() switch
        {
            "-m" or "--model" => "-m",
            "-ngl" or "--n-gpu-layers" or "--gpu-layers" => "--gpu-layers",
            "-c" or "--ctx-size" or "--context-size" => "--ctx-size",
            "-t" or "--threads" => "--threads",
            "-tb" or "--threads-batch" => "--threads-batch",
            "-b" or "--batch-size" => "--batch-size",
            "-ub" or "--ubatch-size" => "--ubatch-size",
            "-fa" or "--flash-attn" => "--flash-attn",
            _ => key
        };
    }

    private static string? TrimMatchingQuotes(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length >= 2 &&
            ((value[0] == '"' && value[value.Length - 1] == '"') ||
             (value[0] == '\'' && value[value.Length - 1] == '\'')))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private static string? CombineExtraArgs(string? uiArgs, string? runArgs)
    {
        var parts = new[] { uiArgs?.Trim(), runArgs?.Trim() }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private static string BuildAggregatedOutputJson(
        string? id,
        string? model,
        string? objectName,
        string? systemFingerprint,
        string? finishReason,
        long created,
        string content,
        string reasoningContent = "",
        List<(int Index, string? Id, string? Type, string? Name, string Arguments)>? toolCalls = null)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        if (!string.IsNullOrEmpty(id))
        {
            writer.WriteString("id", id);
        }
        if (!string.IsNullOrEmpty(objectName))
        {
            writer.WriteString("object", objectName);
        }
        if (created != 0)
        {
            writer.WriteNumber("created", created);
        }
        if (!string.IsNullOrEmpty(model))
        {
            writer.WriteString("model", model);
        }
        if (!string.IsNullOrEmpty(systemFingerprint))
        {
            writer.WriteString("system_fingerprint", systemFingerprint);
        }

        writer.WritePropertyName("choices");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteNumber("index", 0);
        writer.WritePropertyName("message");
        writer.WriteStartObject();
        writer.WriteString("role", "assistant");

        if (!string.IsNullOrEmpty(reasoningContent))
        {
            writer.WriteString("reasoning_content", reasoningContent);
        }

        writer.WriteString("content", content);

        if (toolCalls is { Count: > 0 })
        {
            writer.WritePropertyName("tool_calls");
            writer.WriteStartArray();

            foreach (var toolCall in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", toolCall.Index);
                if (!string.IsNullOrEmpty(toolCall.Id))
                {
                    writer.WriteString("id", toolCall.Id);
                }
                if (!string.IsNullOrEmpty(toolCall.Type))
                {
                    writer.WriteString("type", toolCall.Type);
                }

                writer.WritePropertyName("function");
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(toolCall.Name))
                {
                    writer.WriteString("name", toolCall.Name);
                }
                writer.WriteString("arguments", toolCall.Arguments);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();

        if (!string.IsNullOrEmpty(finishReason))
        {
            writer.WriteString("finish_reason", finishReason);
        }

        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? TryFormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var text = json.Trim('\uFEFF', ' ', '\t', '\r', '\n', '\0');
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var builder = new StringBuilder();
            WriteJsonElement(doc.RootElement, builder, 0);
            return builder.ToString().Replace("\n", Environment.NewLine);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteJsonElement(JsonElement element, StringBuilder builder, int indent)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteJsonObject(element, builder, indent);
                break;
            case JsonValueKind.Array:
                WriteJsonArray(element, builder, indent);
                break;
            case JsonValueKind.String:
                WriteJsonString(element.GetString() ?? string.Empty, builder);
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static void WriteJsonObject(JsonElement element, StringBuilder builder, int indent)
    {
        builder.Append('{');

        bool first = true;
        foreach (var property in element.EnumerateObject())
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append('\n');
            AppendJsonIndent(builder, indent + 1);
            WriteJsonString(property.Name, builder);
            builder.Append(": ");
            WriteJsonElement(property.Value, builder, indent + 1);
        }

        if (!first)
        {
            builder.Append('\n');
            AppendJsonIndent(builder, indent);
        }

        builder.Append('}');
    }

    private static void WriteJsonArray(JsonElement element, StringBuilder builder, int indent)
    {
        builder.Append('[');

        bool first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append('\n');
            AppendJsonIndent(builder, indent + 1);
            WriteJsonElement(item, builder, indent + 1);
        }

        if (!first)
        {
            builder.Append('\n');
            AppendJsonIndent(builder, indent);
        }

        builder.Append(']');
    }

    private static void AppendJsonIndent(StringBuilder builder, int indent)
    {
        builder.Append(' ', indent * 2);
    }

    private static void WriteJsonString(string value, StringBuilder builder)
    {
        builder.Append('"');

        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        builder.Append('"');
    }

    private static string ExpandEscapedNewlinesInJson(string jsonText)
    {
        if (string.IsNullOrEmpty(jsonText))
        {
            return jsonText;
        }

        var builder = new StringBuilder(jsonText.Length);
        bool inString = false;

        for (int i = 0; i < jsonText.Length; i++)
        {
            char c = jsonText[i];

            if (!inString)
            {
                builder.Append(c);
                if (c == '"')
                {
                    inString = true;
                }
                continue;
            }

            if (c == '"')
            {
                inString = false;
                builder.Append(c);
                continue;
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (i + 1 >= jsonText.Length)
            {
                builder.Append(c);
                continue;
            }

            char next = jsonText[i + 1];
            if (next == 'n')
            {
                builder.Append(Environment.NewLine);
                i++;
            }
            else if (next == 'r')
            {
                if (i + 3 < jsonText.Length &&
                    jsonText[i + 2] == '\\' &&
                    jsonText[i + 3] == 'n')
                {
                    builder.Append(Environment.NewLine);
                    i += 3;
                }
                else
                {
                    builder.Append(Environment.NewLine);
                    i++;
                }
            }
            else
            {
                builder.Append(c);
                builder.Append(next);
                i++;
            }
        }

        return builder.ToString();
    }

    private static string WrapText(string text, int maxCharsPerLine)
    {
        if (string.IsNullOrEmpty(text) || maxCharsPerLine <= 0)
        {
            return text;
        }

        var wrapped = new StringBuilder();
        for (int i = 0; i < text.Length; i += maxCharsPerLine)
        {
            if (i > 0)
            {
                wrapped.Append(Environment.NewLine);
            }

            int length = Math.Min(maxCharsPerLine, text.Length - i);
            wrapped.Append(text, i, length);
        }

        return wrapped.ToString();
    }

    /// <summary>
    /// 当 /run 接口通过 extraArgs 传入 --ctx-size 时，将最终生效的 ctx 值回填到界面输入框。
    /// </summary>
    private void UpdateCtxFromRunArgs(string? extraArgs)
    {
        if (string.IsNullOrWhiteSpace(extraArgs))
        {
            return;
        }

        var dict = ParseCommandArguments(extraArgs);
        if ((dict.TryGetValue("--ctx-size", out var value) || dict.TryGetValue("-c", out value)) &&
            !string.IsNullOrWhiteSpace(value) &&
            int.TryParse(value, out int ctx) &&
            ctx > 0)
        {
            txtCtx.Text = ctx.ToString();
        }
    }

    private string BuildArguments(string modelPath, int gpuLayers, bool isEmbeddingModel, bool isRerankerModel, string? extraArgs = null)
    {
        var args = new Dictionary<string, string?>();

        args["-m"] = $"\"{modelPath}\"";
        args["--port"] = (_llamaPort > 0 ? _llamaPort : _port).ToString();
        args["--host"] = "0.0.0.0";

        if (gpuLayers == -1)
        {
            args["--gpu-layers"] = "-1";
        }
        else if (gpuLayers == 0)
        {
            args["--gpu-layers"] = "0";
        }
        else
        {
            args["--gpu-layers"] = gpuLayers.ToString();
        }

        int ctxSize = isRerankerModel
            ? 16384
            : isEmbeddingModel
                ? 8192
                : (_gpuTotalMemoryGB >= 16 ? 8192 : 4096);

        // 手动填写的 ctx 值优先于程序自动计算值（但 /run 的 --ctx-size 会在后面再覆盖它）
        if (!string.IsNullOrWhiteSpace(txtCtx.Text) && int.TryParse(txtCtx.Text, out int manualCtx) && manualCtx > 0)
        {
            ctxSize = manualCtx;
        }

        args["--ctx-size"] = ctxSize.ToString();

        if (isRerankerModel)
        {
            args["--reranking"] = null;
            args["--cont-batching"] = null;
        }
        else if (isEmbeddingModel)
        {
            args["--embedding"] = null;
            args["--pooling"] = "last";
            args["--embd-normalize"] = "1";
            args["--cont-batching"] = null;
        }
        else
        {
            args["--cont-batching"] = null;
        }

        if (!string.IsNullOrWhiteSpace(extraArgs))
        {
            var extraArgsDict = ParseCommandArguments(extraArgs);
            foreach (var kvp in extraArgsDict)
            {
                var canonicalKey = CanonicalizeArgumentKey(kvp.Key);

                // 模型文件由 /run 的 model 参数决定，不允许通过额外参数替换。
                if (canonicalKey.Equals("-m", StringComparison.OrdinalIgnoreCase))
                {
                    Log("[参数] 已忽略额外参数中的模型名参数，模型仍由 model 参数指定");
                    continue;
                }

                if (canonicalKey.Equals("--port", StringComparison.OrdinalIgnoreCase) ||
                    canonicalKey.Equals("--host", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"[参数] 已忽略额外参数中的 {kvp.Key}，内部模型端口和监听地址由程序管理");
                    continue;
                }

                args[canonicalKey] = kvp.Value;
            }
        }

        var sb = new StringBuilder();
        foreach (var kvp in args)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(kvp.Key);
            if (kvp.Value != null)
            {
                sb.Append(' ');
                sb.Append(kvp.Value);
            }
        }

        return sb.ToString();
    }

    #endregion

    #region Stop Model

    private void LlamaProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        LogLlamaOutput(e.Data);
    }

    private void LlamaProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        LogLlamaOutput(e.Data);
    }

    private void LogLlamaOutput(string? line)
    {
        if (line == null || this.IsDisposed)
        {
            return;
        }

        if (line.Contains("llama_server: listening on", StringComparison.OrdinalIgnoreCase))
        {
            var apiLog = BuildCurrentApiLog();
            try { this.BeginInvoke(() => Log($"模型运行成功，{apiLog}")); } catch { }
            return;
        }

        if (line.Contains("llama_server: more info:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("github.com/ggml-org/llama.cpp/pull/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try { this.BeginInvoke(() => Log(AppendTokensPerSecond(line))); } catch { }
    }

    private static string AppendTokensPerSecond(string line)
    {
        if (string.IsNullOrWhiteSpace(line) ||
            !line.Contains("print_timing", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("tokens per second", StringComparison.OrdinalIgnoreCase))
        {
            return line;
        }

        var match = Regex.Match(
            line,
            @"time\s*=\s*([0-9]+(?:\.[0-9]+)?)\s*ms\s*/\s*([0-9]+)\s*tokens",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return line;
        }

        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double milliseconds) ||
            !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tokens) ||
            milliseconds <= 0)
        {
            return line;
        }

        var tokensPerSecond = tokens / (milliseconds / 1000.0);
        return $"{line}，{tokensPerSecond:0.#}token/s";
    }

    private string BuildGpuMemoryDisplay(bool includeAllocatable = false)
    {
        var text = $"显存: {_gpuTotalMemoryGB:F0}G";
        if (includeAllocatable)
        {
            text += $"，可用: {_gpuFreeMemoryGB:F1}G";
        }

        return text;
    }

    private string BuildCurrentApiLog()
    {
        var modelName = _currentModelName;
        if (!string.IsNullOrEmpty(modelName))
        {
            if (modelName.Contains("Reranker", StringComparison.OrdinalIgnoreCase))
            {
                return $"Reranker接口: http://127.0.0.1:{_port}/v1/rerank";
            }

            if (modelName.Contains("Embedding", StringComparison.OrdinalIgnoreCase))
            {
                return $"Embedding接口: http://127.0.0.1:{_port}/v1/embeddings";
            }
        }

        return $"API接口: http://127.0.0.1:{_port}/v1/chat/completions";
    }

    private void LlamaProcess_Exited(object? sender, EventArgs e)
    {
        if (this.IsDisposed) return;
        try
        {
            this.BeginInvoke(() =>
            {
                Log("[进程] 模型进程已退出");
                UpdateStopButtonState(false);
                btnLlamaDir.Enabled = true;
                btnModelDir.Enabled = true;
                txtPort.Enabled = true;
                btnSetPort.Enabled = true;
                txtGpuLayers.Enabled = true;
                txtCtx.Enabled = true;
                txtOtherArgs.Enabled = true;
                _runningModelIndex = -1;
                _currentModelName = null;
                lblRunningModel.Text = "";
                lstModels.Invalidate();  // Refresh list to remove running model highlight
                _llamaPort = 0;
                UpdatePortTooltip();
                EnsureProbeServer();     // Keep the model-discovery listener on the public port
                StartReservedLlamaListener();
                UpdateTrayText();
            });
        }
        catch { }
    }

    private void btnStop_Click(object? sender, EventArgs e)
    {
        StopCurrentModel();
    }

    private void StopCurrentModel()
    {
        if (_llamaProcess == null) return;

        var process = _llamaProcess;
        _llamaProcess = null;

        ResetUIAfterStop();
        Log("[终止] 正在终止模型进程...");

        Task.Run(() =>
        {
            try
            {
                process.OutputDataReceived -= LlamaProcess_OutputDataReceived;
                process.ErrorDataReceived -= LlamaProcess_ErrorDataReceived;
                process.Exited -= LlamaProcess_Exited;

                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        process.Kill();
                    }

                    process.WaitForExit(5000);
                    this.BeginInvoke(new Action(() => Log("[终止] 模型进程已终止")));
                }

                try { this.BeginInvoke(new Action(() => { EnsureProbeServer(); StartReservedLlamaListener(); })); } catch { }
            }
            catch (Exception ex)
            {
                this.BeginInvoke(new Action(() => Log($"[错误] 终止失败: {ex.Message}")));
            }
            finally
            {
                process.Dispose();
            }
        });
    }

    /// <summary>
    /// 可等待的停止当前模型方法，供 HTTP /run 接口使用。
    /// 在 UI 线程清理状态后，后台等待进程真正退出。
    /// </summary>
    private async Task StopCurrentModelAsync()
    {
        Process? process;
        if (this.InvokeRequired)
        {
            process = (Process?)this.Invoke(new Func<Process?>(() =>
            {
                var p = _llamaProcess;
                _llamaProcess = null;
                if (p != null) ResetUIAfterStop();
                return p;
            }));
        }
        else
        {
            process = _llamaProcess;
            _llamaProcess = null;
            if (process != null) ResetUIAfterStop();
        }

        if (process == null) return;

        Log("[终止] 正在终止模型进程...");

        await Task.Run(() =>
        {
            try
            {
                process.OutputDataReceived -= LlamaProcess_OutputDataReceived;
                process.ErrorDataReceived -= LlamaProcess_ErrorDataReceived;
                process.Exited -= LlamaProcess_Exited;

                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        process.Kill();
                    }

                    process.WaitForExit(5000);
                    try { this.BeginInvoke(new Action(() => Log("[终止] 模型进程已终止"))); } catch { }
                }

                try { this.BeginInvoke(new Action(() => { EnsureProbeServer(); StartReservedLlamaListener(); })); } catch { }
            }
            catch (Exception ex)
            {
                try { this.BeginInvoke(new Action(() => Log($"[错误] 终止失败: {ex.Message}"))); } catch { }
            }
            finally
            {
                process.Dispose();
            }
        });
    }

    private void ResetUIAfterStop()
    {
        UpdateStopButtonState(false);
        btnLlamaDir.Enabled = true;
        btnModelDir.Enabled = true;
        txtPort.Enabled = true;
        btnSetPort.Enabled = true;
        txtGpuLayers.Enabled = true;
        txtCtx.Enabled = true;
        txtOtherArgs.Enabled = true;
        _currentModelPath = null;
        _currentModelName = null;
        _llamaPort = 0;
        UpdatePortTooltip();
        _runningModelIndex = -1;
        lblRunningModel.Text = "";
        lstModels.Invalidate();  // Refresh list to remove running model highlight
        UpdateTrayText();
    }

    private void UpdateStopButtonState(bool enabled)
    {
        btnStop.Enabled = enabled;
        if (enabled)
        {
            btnStop.BackColor = Color.FromArgb(204, 83, 83);
            btnStop.ForeColor = Color.White;
        }
        else
        {
            btnStop.BackColor = Color.FromArgb(80, 80, 80);
            btnStop.ForeColor = Color.FromArgb(120, 120, 120);
        }
    }

    private void btnStop_MouseEnter(object? sender, EventArgs e)
    {
        if (btnStop.Enabled)
        {
            btnStop.ForeColor = Color.FromArgb(255, 200, 200);
        }
    }

    private void btnStop_MouseLeave(object? sender, EventArgs e)
    {
        if (btnStop.Enabled)
        {
            btnStop.ForeColor = Color.White;
        }
    }

    private void btnClearLog_Click(object? sender, EventArgs e)
    {
        txtLog.Clear();
        _taggedRawByLine.Clear();
        _trimmedLinesOffset = 0;
        _logLineCount = 0;
    }

    private void chkLogInput_CheckedChanged(object? sender, EventArgs e)
    {
        _logChatInput = chkLogInput.Checked;
    }

    private void chkLogOutput_CheckedChanged(object? sender, EventArgs e)
    {
        _logChatOutput = chkLogOutput.Checked;
    }

    private void btnSetPort_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtPort.Text) || !int.TryParse(txtPort.Text, out int newPort) || newPort < 1 || newPort > 65535)
        {
            Log("[错误] 请输入有效的端口号 (1-65535)");
            txtPort.Text = _port.ToString();
            return;
        }

        if (newPort == _port) return;

        StopHttpServer();

        if (!IsPortAvailable(newPort))
        {
            Log($"[警告] 端口 {newPort} 已被占用，设置后启动模型可能会失败");
        }

        _port = newPort;
        _apiPort = 0;
        _reservedLlamaPort = 0;
        StopReservedLlamaListener();
        StartHttpServer();
        UpdatePortTooltip();
    }

    private void btnCopyApi_Click(object? sender, EventArgs e)
    {
        var apiUrl = $"http://127.0.0.1:{_port}/v1/chat/completions";
        Clipboard.SetText(apiUrl);
        Log($"已复制API地址: {apiUrl}");
    }

    private void lblRunningModel_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentModelName))
        {
            Clipboard.SetText(_currentModelName);
        }
    }

    private void txtGpuLayers_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        // Allow minus sign only at the beginning (for -1 value)
        if (e.KeyChar == '-' && txtGpuLayers.SelectionStart == 0 && !txtGpuLayers.Text.Contains('-')) return;
        if (!char.IsDigit(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    private void txtCtx_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        if (!char.IsDigit(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    private void txtPort_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        if (!char.IsDigit(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    private void txtOtherArgs_TextChanged(object? sender, EventArgs e)
    {
        AdjustOtherArgsHeight();
    }

    private void AdjustOtherArgsHeight()
    {
        if (txtOtherArgs == null || btnStop == null || lblModels == null || lstModels == null)
        {
            return;
        }

        int minHeight = Math.Max(28, _otherArgsBaseHeight);
        const int maxLines = 4;

        int oldHeight = txtOtherArgs.Height;
        int lineHeight = GetOtherArgsLineHeight();
        int textLines = CountOtherArgsLines(txtOtherArgs.Text);
        int visibleLines = Math.Clamp(textLines, 1, maxLines);
        int desiredHeight = Math.Max(minHeight, visibleLines * lineHeight + 8);

        txtOtherArgs.ScrollBars = textLines > maxLines
            ? ScrollBars.Vertical
            : ScrollBars.None;

        if (desiredHeight == oldHeight)
        {
            UpdateOtherArgsTextRect();
            return;
        }

        int delta = desiredHeight - oldHeight;
        txtOtherArgs.Height = desiredHeight;

        lblModels.Top += delta;
        btnStop.Top += delta;
        lstModels.Top += delta;
        lstModels.Height -= delta;

        UpdateOtherArgsTextRect();
    }

    private int GetOtherArgsLineHeight()
    {
        return Math.Max(1, TextRenderer.MeasureText("Ayg", txtOtherArgs.Font).Height);
    }

    private int CountOtherArgsLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var flags = TextFormatFlags.WordBreak |
                    TextFormatFlags.TextBoxControl |
                    TextFormatFlags.Left |
                    TextFormatFlags.NoPrefix;
        var availableSize = new Size(Math.Max(1, txtOtherArgs.ClientSize.Width), int.MaxValue);
        var size = TextRenderer.MeasureText(text, txtOtherArgs.Font, availableSize, flags);
        int lineHeight = GetOtherArgsLineHeight();
        int lineCount = Math.Max(1, (int)Math.Ceiling(size.Height / (double)lineHeight));

        if (txtOtherArgs.IsHandleCreated)
        {
            int actualLineCount = txtOtherArgs.GetLineFromCharIndex(txtOtherArgs.TextLength) + 1;
            lineCount = Math.Max(lineCount, actualLineCount);
        }

        return lineCount;
    }

    #endregion

    #region System Tray

    private void notifyIcon1_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        ShowConsole();
    }

    private void showConsoleToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        ShowConsole();
    }

    private void exitToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        _isClosing = true;
        this.Close();
    }

    private void contextMenuStrip1_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        showConsoleToolStripMenuItem.Enabled = !this.Visible;
    }

    private void ShowConsole()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.Activate();
        this.BringToFront();
        this.ShowInTaskbar = true;
    }

    #endregion

    #region Logging

    private void Log(string message)
    {
        if (this.IsDisposed) return;

        if (this.InvokeRequired)
        {
            try { this.BeginInvoke(new Action(() => Log(message))); } catch { }
            return;
        }

        if (txtLog.IsDisposed) return;

        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var timestampText = $"[{timestamp}] ";
            var messageText = $"{message}{Environment.NewLine}";
            var messageColor = message.StartsWith("[启动模型]", StringComparison.Ordinal)
                ? Color.FromArgb(46, 204, 113)
                : Color.FromArgb(169, 183, 198);

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = Color.FromArgb(128, 128, 128);
            txtLog.AppendText(timestampText);

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = messageColor;
            txtLog.AppendText(messageText);
            _logLineCount += CountNewlines(messageText);

            txtLog.SelectionColor = txtLog.ForeColor;

            TrimLogPrefixIfNeeded();

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }
        catch { }
    }

    private void LogTagged(string tag, string content)
    {
        if (this.IsDisposed) return;

        if (this.InvokeRequired)
        {
            try { this.BeginInvoke(new Action(() => LogTagged(tag, content))); } catch { }
            return;
        }

        if (txtLog.IsDisposed) return;

        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var timestampText = $"[{timestamp}] ";

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = Color.FromArgb(128, 128, 128);
            txtLog.AppendText(timestampText);

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = Color.FromArgb(86, 156, 214);
            txtLog.AppendText(tag);
            txtLog.AppendText(" ");

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = Color.FromArgb(169, 183, 198);
            txtLog.AppendText(content);
            txtLog.AppendText(Environment.NewLine);
            _logLineCount += CountNewlines(content) + 1;

            txtLog.SelectionColor = txtLog.ForeColor;

            TrimLogPrefixIfNeeded();

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }
        catch { }
    }

    private void TrimLogPrefixIfNeeded()
    {
        // 日志不设上限：保留完整内容，不再裁剪日志前缀。
    }

    private void LogOutputLine(string rawLine)
    {
        if (this.IsDisposed) return;

        if (this.InvokeRequired)
        {
            try { this.BeginInvoke(new Action(() => LogOutputLine(rawLine))); } catch { }
            return;
        }

        if (txtLog.IsDisposed) return;

        try
        {
            var displayLine = RemoveModelFieldForLog(rawLine);
            int absoluteLine = _logLineCount;
            _taggedRawByLine[absoluteLine] = rawLine;

            LogTagged("[输出]", displayLine);
        }
        catch { }
    }

    private void LogClickableRaw(string tag, string rawContent, string? displaySuffix = null)
    {
        if (this.IsDisposed) return;

        if (this.InvokeRequired)
        {
            try { this.BeginInvoke(new Action(() => LogClickableRaw(tag, rawContent, displaySuffix))); } catch { }
            return;
        }

        if (txtLog.IsDisposed) return;

        try
        {
            int absoluteLine = _logLineCount;
            _taggedRawByLine[absoluteLine] = rawContent;

            var content = string.IsNullOrEmpty(displaySuffix) ? string.Empty : $" {displaySuffix}";
            LogTagged(tag, content);
        }
        catch { }
    }

    private int CountNewlinesBefore(int charIndex)
    {
        var text = txtLog.Text;
        int limit = Math.Min(charIndex, text.Length);
        int count = 0;
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }
        return count;
    }

    private static int CountNewlines(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                count++;
            }
        }
        return count;
    }

    private int GetLineStartCharIndex(int lineIndex)
    {
        if (lineIndex <= 0)
        {
            return 0;
        }

        var text = txtLog.Text;
        int newlineCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                newlineCount++;
                if (newlineCount == lineIndex)
                {
                    return i + 1;
                }
            }
        }

        return text.Length;
    }

    private void LogEmbeddingInput(string rawInput)
    {
        LogClickableRaw("[Embedding输入]", rawInput);
    }

    private void LogEmbeddingOutput(string rawOutput)
    {
        LogClickableRaw("[Embedding输出]", rawOutput, BuildInlineRawDisplay(rawOutput));
    }

    private void LogRerankerInput(string rawInput)
    {
        LogClickableRaw("[Reranker输入]", rawInput);
    }

    private void LogRerankerOutput(string rawOutput)
    {
        LogClickableRaw("[Reranker输出]", rawOutput, BuildInlineRawDisplay(rawOutput));
    }

    private static string BuildInlineRawDisplay(string raw)
    {
        const int maxInlineLength = 6000;
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var singleLine = raw
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        if (singleLine.Length <= maxInlineLength)
        {
            return singleLine;
        }

        return singleLine.Substring(0, maxInlineLength) + "… [内容过长，点击标签查看完整 JSON]";
    }

    private static string RemoveModelFieldForLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        const string modelValue = "\"model\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"";

        var result = Regex.Replace(line, ",\\s*" + modelValue, string.Empty);
        result = Regex.Replace(result, modelValue + "\\s*,?", string.Empty);
        return result;
    }

    private void TxtLog_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        try
        {
            int charIndex = txtLog.GetCharIndexFromPosition(e.Location);
            if (charIndex < 0)
            {
                return;
            }

            int lineIndex = CountNewlinesBefore(charIndex);
            if (lineIndex < 0 || lineIndex >= txtLog.Lines.Length)
            {
                return;
            }

            string lineText = txtLog.Lines[lineIndex];
            int lineStart = GetLineStartCharIndex(lineIndex);
            int positionInLine = charIndex - lineStart;

            foreach (var tag in new[]
                     {
                         "[显示输入]", "[输出]", "[聚合输出]",
                         "[Embedding输入]", "[Embedding输出]",
                         "[Reranker输入]", "[Reranker输出]"
                     })
            {
                int tagIndex = lineText.IndexOf(tag, StringComparison.Ordinal);
                if (tagIndex < 0 || positionInLine < tagIndex || positionInLine >= tagIndex + tag.Length)
                {
                    continue;
                }

                int absoluteLine = lineIndex + _trimmedLinesOffset;
                string rawContent = _taggedRawByLine.TryGetValue(absoluteLine, out var taggedRaw)
                    ? taggedRaw
                    : string.Empty;

                if (!string.IsNullOrEmpty(rawContent))
                {
                    ShowJsonViewer(tag, rawContent);
                }

                return;
            }
        }
        catch
        {
            // 日志控件尚未完全初始化或坐标不可用时，忽略本次点击。
        }
    }

    private void ShowJsonViewer(string tag, string rawContent)
    {
        string candidate = rawContent;
        if (tag == "[输出]" &&
            rawContent.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = rawContent.Substring(5).TrimStart();
        }

        var formattedText = TryFormatJson(candidate) ?? rawContent;
        bool beautifyEnabled = true;
        bool wrapEnabled = false;

        var viewer = new Form
        {
            Text = tag switch
            {
                "[显示输入]" => "输入 JSON",
                "[聚合输出]" => "聚合输出 JSON",
                "[Embedding输入]" => "Embedding 输入 JSON",
                "[Embedding输出]" => "Embedding 输出 JSON",
                "[Reranker输入]" => "Reranker 输入 JSON",
                "[Reranker输出]" => "Reranker 输出 JSON",
                _ => "输出 JSON"
            },
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(
                Math.Max(620, _defaultWindowSize.Width - 20),
                Math.Max(380, _defaultWindowSize.Height - 20)),
            MinimumSize = new Size(620, 380),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(169, 183, 198),
            ShowInTaskbar = false
        };

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(30, 30, 30)
        };

        var btnBeautify = new RoundedButton
        {
            Text = "美化",
            Size = new Size(70, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Microsoft YaHei UI", 8F)
        };
        btnBeautify.BorderColor = Color.FromArgb(120, 125, 122);
        btnBeautify.BorderThickness = 1;
        topPanel.Controls.Add(btnBeautify);

        var btnWrap = new RoundedButton
        {
            Text = "换行",
            Size = new Size(70, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Microsoft YaHei UI", 8F)
        };
        btnWrap.BorderColor = Color.FromArgb(120, 125, 122);
        btnWrap.BorderThickness = 1;
        topPanel.Controls.Add(btnWrap);

        var btnCopy = new RoundedButton
        {
            Text = "复制",
            Size = new Size(70, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(170, 170, 170),
            Font = new Font("Microsoft YaHei UI", 8F)
        };
        btnCopy.BorderColor = Color.FromArgb(95, 95, 95);
        btnCopy.BorderThickness = 1;
        topPanel.Controls.Add(btnCopy);

        var txtViewer = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 8, 0),
            Multiline = true,
            ReadOnly = true,
            HideSelection = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(169, 183, 198),
            Font = new Font("Consolas", 9F),
            WordWrap = false,
            ScrollBars = ScrollBars.None
        };
        string GetViewerText()
        {
            string baseText = beautifyEnabled ? formattedText : rawContent;
            return wrapEnabled ? ExpandEscapedNewlinesInJson(baseText) : baseText;
        }

        void ApplyBeautifyButtonStyle()
        {
            if (beautifyEnabled)
            {
                btnBeautify.BorderColor = Color.FromArgb(120, 125, 122);
                btnBeautify.ForeColor = Color.FromArgb(180, 180, 180);
            }
            else
            {
                btnBeautify.BorderColor = Color.FromArgb(95, 95, 95);
                btnBeautify.ForeColor = Color.FromArgb(120, 120, 120);
            }

            btnBeautify.Invalidate();
        }

        void ApplyWrapButtonStyle()
        {
            if (wrapEnabled)
            {
                btnWrap.BorderColor = Color.FromArgb(120, 125, 122);
                btnWrap.ForeColor = Color.FromArgb(180, 180, 180);
            }
            else
            {
                btnWrap.BorderColor = Color.FromArgb(95, 95, 95);
                btnWrap.ForeColor = Color.FromArgb(120, 120, 120);
            }

            btnWrap.Invalidate();
        }

        txtViewer.Text = GetViewerText();
        ApplyBeautifyButtonStyle();
        ApplyWrapButtonStyle();

        viewer.Controls.Add(txtViewer);
        viewer.Controls.Add(topPanel);

        viewer.Load += (_, _) =>
        {
            btnBeautify.Location = new Point(8, 8);
            btnWrap.Location = new Point(btnBeautify.Right + 8, 8);
            btnCopy.Location = new Point(topPanel.ClientSize.Width - btnCopy.Width - 8, 8);
            viewer.ActiveControl = btnBeautify;
            UpdateJsonScrollBars(txtViewer);
        };

        viewer.Shown += (_, _) =>
        {
            txtViewer.SelectionStart = 0;
            txtViewer.SelectionLength = 0;
            UpdateJsonScrollBars(txtViewer);
            try
            {
                SetWindowTheme(txtViewer.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // 主题 API 不可用时保持默认滚动条。
            }
        };

        btnBeautify.Click += (_, _) =>
        {
            beautifyEnabled = !beautifyEnabled;
            txtViewer.Text = GetViewerText();
            UpdateJsonScrollBars(txtViewer);
            ApplyBeautifyButtonStyle();
        };

        btnWrap.Click += (_, _) =>
        {
            wrapEnabled = !wrapEnabled;
            txtViewer.Text = GetViewerText();
            UpdateJsonScrollBars(txtViewer);
            ApplyWrapButtonStyle();
        };

        btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(txtViewer.Text);
                btnCopy.Text = "已复制";
            }
            catch
            {
                btnCopy.Text = "复制失败";
            }
        };

        viewer.ShowDialog(this);
    }

    #endregion

    #region Dark ScrollBar Styling

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref RECT lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref PARAFORMAT2 lParam);

    private const int EM_SETRECT = 0xB3;
    private const int WM_USER = 0x0400;
    private const int EM_SETPARAFORMAT = WM_USER + 71;
    private const uint PFM_LINESPACING = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PARAFORMAT2
    {
        public int cbSize;
        public uint dwMask;
        public short wNumbering;
        public short wReserved;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public short wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public short sStyle;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public short wShadingWeight;
        public short wShadingStyle;
        public short wNumberingStart;
        public short wNumberingStyle;
        public short wNumberingTab;
        public short wBorderSpace;
        public short wBorderWidth;
        public short wBorders;
    }

    private void CenterTextBoxText()
    {
        CenterTextInTextBox(txtLlamaDir);
        CenterTextInTextBox(txtModelDir);
        CenterTextInTextBox(txtPort);
        CenterTextInTextBox(txtGpuLayers);
        CenterTextInTextBox(txtCtx);
        UpdateOtherArgsTextRect();
    }

    private void RecenterTextBoxes()
    {
        if (this.IsDisposed || !this.IsHandleCreated)
        {
            return;
        }

        try
        {
            CenterTextBoxText();
        }
        catch
        {
            // DPI/缩放过程中控件可能尚未准备好，忽略本次重新居中。
        }
    }

    private void CenterTextInTextBox(TextBox textBox)
    {
        if (!textBox.IsHandleCreated)
        {
            return;
        }

        using var g = textBox.CreateGraphics();
        var fontSize = g.MeasureString("Ayg", textBox.Font);
        int textHeight = (int)Math.Ceiling(fontSize.Height);
        int offset = Math.Max(0, (textBox.Height - textHeight) / 2);

        var rect = new RECT
        {
            Left = 3,
            Top = offset,
            Right = textBox.Width - 3,
            Bottom = textBox.Height - offset
        };

        SendMessage(textBox.Handle, EM_SETRECT, IntPtr.Zero, ref rect);
    }

    private void SetLogLineSpacing(int twips)
    {
        if (txtLog == null || !txtLog.IsHandleCreated)
        {
            return;
        }

        var format = new PARAFORMAT2
        {
            cbSize = Marshal.SizeOf(typeof(PARAFORMAT2)),
            dwMask = PFM_LINESPACING,
            bLineSpacingRule = 4,
            dyLineSpacing = twips,
            rgxTabs = new int[32]
        };

        SendMessage(txtLog.Handle, EM_SETPARAFORMAT, IntPtr.Zero, ref format);
    }

    private void UpdateOtherArgsTextRect()
    {
        if (txtOtherArgs == null || !txtOtherArgs.IsHandleCreated)
        {
            return;
        }

        const int horizontalPadding = 5;
        int textLines = CountOtherArgsLines(txtOtherArgs.Text);
        int top = 0;
        int bottom = txtOtherArgs.Height;

        if (textLines <= 1)
        {
            using var g = txtOtherArgs.CreateGraphics();
            int textHeight = (int)Math.Ceiling(g.MeasureString("Ayg", txtOtherArgs.Font).Height);
            int offset = Math.Max(0, (txtOtherArgs.Height - textHeight) / 2);
            top = offset;
            bottom = txtOtherArgs.Height - offset;
        }
        else
        {
            top = 1;
            bottom = txtOtherArgs.Height - 1;
        }

        var rect = new RECT
        {
            Left = horizontalPadding,
            Top = top,
            Right = Math.Max(horizontalPadding + 1, txtOtherArgs.Width - horizontalPadding),
            Bottom = bottom
        };

        SendMessage(txtOtherArgs.Handle, EM_SETRECT, IntPtr.Zero, ref rect);
    }

    private void InitCustomScrollBars()
    {
        lstModels.DrawItem += LstModels_DrawItem;
        lstModels.DrawMode = DrawMode.OwnerDrawFixed;
        lstModels.ItemHeight = 32;  // 每行高度：文字高度 + 上下padding

        // Apply dark theme to scrollbars
        try
        {
            SetWindowTheme(lstModels.Handle, "DarkMode_Explorer", null);
            SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null);
        }
        catch { }

        // Add padding to log textbox
        SetTextBoxPadding(txtLog, 4);
    }

    private void SetTextBoxPadding(RichTextBox textBox, int padding)
    {
        var rect = new RECT
        {
            Left = padding,
            Top = padding,
            Right = textBox.Width - padding,
            Bottom = textBox.Height - padding
        };
        SendMessage(textBox.Handle, EM_SETRECT, IntPtr.Zero, ref rect);
    }

    private void UpdateJsonScrollBars(TextBox textBox)
    {
        if (textBox == null || !textBox.IsHandleCreated)
        {
            return;
        }

        int horizontalPadding = 28;
        int verticalPadding = 26;
        int usableWidth = Math.Max(0, textBox.ClientSize.Width - horizontalPadding);
        int usableHeight = Math.Max(0, textBox.ClientSize.Height - verticalPadding);

        string[] lines = textBox.Lines;
        int lineHeight = textBox.Font.Height;
        int contentHeight = lines.Length * lineHeight;
        int maxLineWidth = 0;

        foreach (string line in lines)
        {
            int lineWidth = TextRenderer.MeasureText(line, textBox.Font).Width;
            if (lineWidth > maxLineWidth)
            {
                maxLineWidth = lineWidth;
            }
        }

        bool needVertical = contentHeight > usableHeight;
        bool needHorizontal = maxLineWidth > usableWidth;

        textBox.ScrollBars = needVertical && needHorizontal
            ? ScrollBars.Both
            : needVertical
                ? ScrollBars.Vertical
                : needHorizontal
                    ? ScrollBars.Horizontal
                    : ScrollBars.None;
    }

    private void LstModels_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        e.DrawBackground();

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool isRunningModel = (e.Index == _runningModelIndex && _runningModelIndex >= 0);

        // Add padding: left/right 4px, top/bottom 7px
        var textBounds = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 7, e.Bounds.Width - 8, e.Bounds.Height - 14);

        // Determine background color
        Color bgColor;
        if (isSelected)
        {
            bgColor = Color.FromArgb(60, 65, 70);
        }
        else if (isRunningModel)
        {
            bgColor = Color.FromArgb(50, 55, 60);  // Slightly different background for running model
        }
        else
        {
            bgColor = Color.FromArgb(43, 43, 43);
        }

        // Determine text color: running model always shows orange
        Color textColor;
        if (isRunningModel)
        {
            textColor = Color.FromArgb(255, 180, 120);  // Orange for running model
        }
        else if (isSelected)
        {
            textColor = Color.FromArgb(169, 183, 198);  // Light gray for selected non-running
        }
        else
        {
            textColor = Color.FromArgb(169, 183, 198);  // Default light gray
        }

        using (var brush = new SolidBrush(bgColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }
        using (var textBrush = new SolidBrush(textColor))
        using (var sf = new StringFormat
        {
            LineAlignment = StringAlignment.Center
        })
        {
            e.Graphics.DrawString(lstModels.Items[e.Index].ToString(), e.Font!, textBrush, textBounds, sf);
        }

        e.DrawFocusRectangle();
    }

    #endregion

    #region NVML - GPU Memory Detection

    [DllImport("nvml.dll")]
    private static extern int nvmlInit();

    [DllImport("nvml.dll")]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetCount(ref int count);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetHandleByIndex(int index, ref IntPtr handle);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr handle, ref NvmlMemory memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    private void GetGpuMemoryInfo()
    {
        try
        {
            _gpuTotalMemoryGB = 0;
            _gpuFreeMemoryGB = 0;
            _gpuUsedMemoryGB = 0;

            if (nvmlInit() != 0) return;

            int count = 0;
            if (nvmlDeviceGetCount(ref count) != 0 || count == 0)
            {
                nvmlShutdown();
                return;
            }

            // Get memory of first GPU
            IntPtr handle = IntPtr.Zero;
            if (nvmlDeviceGetHandleByIndex(0, ref handle) != 0)
            {
                nvmlShutdown();
                return;
            }

            var memory = new NvmlMemory();
            if (nvmlDeviceGetMemoryInfo(handle, ref memory) != 0)
            {
                nvmlShutdown();
                return;
            }

            nvmlShutdown();

            // Convert bytes to GB
            _gpuTotalMemoryGB = memory.Total / (1024.0 * 1024.0 * 1024.0);
            _gpuFreeMemoryGB = memory.Free / (1024.0 * 1024.0 * 1024.0);
            _gpuUsedMemoryGB = memory.Used / (1024.0 * 1024.0 * 1024.0);
        }
        catch
        {
            _gpuTotalMemoryGB = 0;
            _gpuFreeMemoryGB = 0;
            _gpuUsedMemoryGB = 0;
        }
    }

    #endregion

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private static double GetAvailableRamGB()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
            };

            if (GlobalMemoryStatusEx(ref memStatus))
            {
                return memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            }
        }
        catch { }
        return -1;
    }

    #region Silence - suppress all system beep sounds

    /// <summary>
    /// NativeWindow that subclasses a control to suppress beep-causing messages.
    /// When a read-only or restricted control receives a keystroke it cannot handle,
    /// Windows calls MessageBeep() which produces the annoying "ding" sound.
    /// This wrapper swallows WM_CHAR/WM_SYSCHAR for unhandled keys to prevent it.
    /// </summary>
    private sealed class SilentControlNativeWindow : NativeWindow
    {
        private readonly Control _owner;
        private readonly bool _swallowAllChars;

        public SilentControlNativeWindow(Control owner, bool swallowAllChars = false)
        {
            _owner = owner;
            _swallowAllChars = swallowAllChars;
            AssignHandle(owner.Handle);
            owner.HandleDestroyed += (s, e) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_CHAR:
                case WM_SYSCHAR:
                    // Swallow character messages that would cause beeps
                    if (_swallowAllChars)
                    {
                        return;
                    }
                    // Allow Ctrl+C (copy), Ctrl+A (select all), Ctrl+V, backspace, etc.
                    // but block regular typing on read-only controls
                    if (_owner is RichTextBox rtb && rtb.ReadOnly)
                    {
                        Keys key = (Keys)m.WParam & Keys.KeyCode;
                        // Allow Ctrl shortcuts: Ctrl+C (3), Ctrl+V (22), Ctrl+X (24), Ctrl+A (1)
                        // Backspace (8), Tab (9), Ctrl+Z (26), Enter (13), Esc (27)
                        bool allowed = key == (Keys)3 || key == (Keys)22 || key == (Keys)24 ||
                                       key == (Keys)1 || key == (Keys)8 || key == (Keys)9 ||
                                       key == (Keys)26 || key == (Keys)13 || key == (Keys)27 ||
                                       key == (Keys)3  /* Ctrl+C */ ||
                                       ((Control.ModifierKeys & Keys.Control) != 0);
                        if (!allowed)
                        {
                            return; // suppress the char (and the beep)
                        }
                    }
                    break;
            }
            base.WndProc(ref m);
        }
    }

    private SilentControlNativeWindow? _silentLogWindow;

    private void InitSilentControls()
    {
        // Make the log RichTextBox completely silent - swallow all character input
        _silentLogWindow = new SilentControlNativeWindow(txtLog, swallowAllChars: true);
    }

    #endregion
}

internal sealed class RoundedButton : Button
{
    private const int CornerRadius = 0;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.FromArgb(90, 90, 90);

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BorderThickness { get; set; } = 1;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // 使用自绘背景，避免系统绘制破坏圆角。
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var backgroundBrush = new SolidBrush(Parent?.BackColor ?? Color.FromArgb(30, 30, 30));
        e.Graphics.FillRectangle(backgroundBrush, 0, 0, Width, Height);

        using var path = CreateRoundedRectangle(rect, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);

        if (BorderThickness > 0)
        {
            using var borderPen = new Pen(BorderColor, BorderThickness);
            e.Graphics.DrawPath(borderPen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            ForeColor,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int diameter = radius * 2;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}

public sealed class LogCheckBox : CheckBox
{
    private const int BoxSize = 14;
    private static readonly Color UncheckedBorder = Color.FromArgb(120, 120, 120);
    private static readonly Color CheckColor = Color.FromArgb(0, 175, 255);
    private static readonly Color TransparentBackground = Color.FromArgb(43, 43, 43);

    public LogCheckBox()
    {
        DoubleBuffered = true;
        AutoSize = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(TransparentBackground);

        int boxTop = Math.Max(0, (Height - BoxSize) / 2) + 1;
        var boxRect = new Rectangle(0, boxTop, BoxSize, BoxSize);

        using (var borderPen = new Pen(UncheckedBorder))
        {
            e.Graphics.DrawRectangle(borderPen, boxRect.X, boxRect.Y, boxRect.Width - 1, boxRect.Height - 1);
        }

        if (Checked)
        {
            using var checkPen = new Pen(CheckColor, 2F);
            var points = new[]
            {
                new Point(boxRect.Left + 2, boxRect.Top + 7),
                new Point(boxRect.Left + 5, boxRect.Top + 10),
                new Point(boxRect.Right - 2, boxRect.Top + 2)
            };
            e.Graphics.DrawLines(checkPen, points);
        }

        var textRect = new Rectangle(
            boxRect.Right + 3,
            0,
            Math.Max(0, Width - boxRect.Right - 3),
            Height);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textRect,
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
