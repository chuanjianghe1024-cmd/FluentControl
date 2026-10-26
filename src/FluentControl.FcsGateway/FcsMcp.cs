using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

// Optional local MCP stdio adapter. The actual Gateway remains the single
// hardware owner; MCP processes only communicate over the same-user pipe.
// No HTTP listener or unsolicited cloud connections are started.
internal static class FcsMcp
{
    private const string PipeName = "FluentControl.FcsGateway.v1";
    private const string Protocol = "2025-06-18";
    private static readonly JsonSerializerOptions JsonOptions = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static bool CanWrite => Environment.GetEnvironmentVariable("FCS_MCP_ENABLE_WRITES") == "1";

    private static string Field(JsonElement element, string key, bool required = true)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            if (!required) return "";
            throw new InvalidDataException("Missing or invalid " + key);
        }
        var value = prop.GetString() ?? "";
        if (value.Length is < 1 or > 100 || value.Any(char.IsControl))
            throw new InvalidDataException("Invalid " + key);
        return value;
    }

    private static JsonElement Parameters(JsonElement frame)
    {
        if (!frame.TryGetProperty("params", out var par) || par.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing tool parameters.");
        if (!par.TryGetProperty("arguments", out var args)) return JsonSerializer.SerializeToElement(new { });
        if (args.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Tool arguments must be an object.");
        return args;
    }

    private static object Input(params string[] required)
    {
        var fields = required.ToDictionary(x => x, x => (object)new { type = "string" });
        return new { type = "object", properties = fields, required,
            additionalProperties = false };
    }

    private static object[] Tools()
    {
        var tools = new List<object>
        {
            new { name = "fcs_list_devices", description = "List FCS devices and last verified samples; never performs a management write.",
                inputSchema = Input() },
            new { name = "fcs_device_capabilities", description = "Read one device's sensor and signal inventory. USB legacy firmware exposes a flat signal adapter.",
                inputSchema = Input("deviceId") },
            new { name = "fcs_read_signal", description = "Read an individual sensor signal by stable deviceId, sensorId and signalId. Current firmware supports illuminance.",
                inputSchema = Input("deviceId", "sensorId", "signalId") },
            new { name = "fcs_device_status", description = "Read non-secret device settings and connection state over trusted USB. Wi-Fi credentials and binding keys are excluded.",
                inputSchema = Input("deviceId") },
            new { name = "fcs_cloud_status", description = "Return local-only cloud integration status; no uploads are configured or performed.",
                inputSchema = Input() }
        };
        if (CanWrite)
        {
            tools.Add(new { name = "fcs_set_device_setting",
                description = "Change an explicitly allowlisted device setting over physical USB only. Requires local opt-in and confirmed=true. No reset, Wi-Fi credentials, firmware or arbitrary commands.",
                inputSchema = new { type = "object",
                    properties = new { deviceId = new { type = "string" }, setting = new { type = "string",
                        @enum = new[] { "wireless.enable", "wireless.disable", "ble.enable", "ble.disable", "ap.start", "ap.stop" } },
                        confirmed = new { type = "boolean" } },
                    required = new[] { "deviceId", "setting", "confirmed" }, additionalProperties = false } });
        }
        return tools.ToArray();
    }

    private static async Task<JsonElement> QueryGatewayAsync(object command, CancellationToken cancel)
    {
        string request = JsonSerializer.Serialize(command);
        if (Encoding.UTF8.GetByteCount(request) > 3072)
            throw new InvalidDataException("Gateway request too large.");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(2000, cancel);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                await writer.WriteLineAsync(request);
                var line = await reader.ReadLineAsync(cancel) ?? throw new IOException("Gateway disconnected.");
                if (line.Length > 65536) throw new InvalidDataException("Gateway reply too large.");
                using var frame = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 12 });
                if (frame.RootElement.GetProperty("protocol").GetString() != "fcs-gateway/1")
                    throw new InvalidDataException("Incompatible FCS Gateway.");
                return frame.RootElement.Clone();
            }
            catch (Exception ex) when (attempt == 0 && ex is TimeoutException or IOException or OperationCanceledException)
            {
                cancel.ThrowIfCancellationRequested();
                // The MCP host may start without FC. The background Gateway is
                // a separate process and uses the same mutex as bundled FC.
                var exe = Environment.ProcessPath ?? throw new IOException("Gateway executable missing.");
                Process.Start(new ProcessStartInfo(exe, "--serve")
                { UseShellExecute = false, CreateNoWindow = true,
                  WorkingDirectory = AppContext.BaseDirectory });
            }
        }
        throw new IOException("FCS Gateway unavailable.");
    }

    private static object GatewayRequest(string operation, string id = "none", string port = "AUTO",
        string? command = null) =>
        new { operation, deviceId = id, port, command, arguments = new { } };

    private static object Result(object response, bool isError = false) =>
        new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(response, JsonOptions) } },
            isError };

    private static async Task<object> CallAsync(JsonElement parameters, CancellationToken cancel)
    {
        var tool = Field(parameters, "name");
        var args = Parameters(new JsonElementWrapper(parameters).WithArguments());
        string id = "";
        if (tool is "fcs_device_capabilities" or "fcs_read_signal" or "fcs_device_status" or "fcs_set_device_setting")
        {
            id = Field(args, "deviceId");
            if (!GatewayRegistry.ValidId(id)) throw new InvalidDataException("Invalid device ID.");
        }
        string op = tool switch
        {
            "fcs_list_devices" => "list",
            "fcs_device_capabilities" => "device.capabilities",
            "fcs_read_signal" => "signal.read",
            "fcs_device_status" => "device.status",
            "fcs_cloud_status" => "cloud.capabilities",
            "fcs_set_device_setting" when CanWrite => "usb.manage",
            _ => throw new InvalidDataException("Unsupported MCP tool.")
        };
        string? setting = null;
        if (tool == "fcs_set_device_setting")
        {
            setting = Field(args, "setting");
            if (setting is not ("wireless.enable" or "wireless.disable" or
                "ble.enable" or "ble.disable" or "ap.start" or "ap.stop") ||
                !args.TryGetProperty("confirmed", out var confirm) || confirm.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Operation not allowlisted or not explicitly confirmed.");
        }
        object request = tool == "fcs_read_signal"
            ? new { operation = op, deviceId = id, sensorId = Field(args, "sensorId"),
                signalId = Field(args, "signalId"), port = "AUTO" }
            : GatewayRequest(op, id, "AUTO", setting);
        var answer = await QueryGatewayAsync(request, cancel);
        if (answer.TryGetProperty("error", out var error))
            return Result(new { error = error.GetString() }, true);
        // Raw USB management replies can contain bindingKey/setupPassword.
        // Never return them from an MCP tool, including a successful write.
        if (tool == "fcs_set_device_setting")
            return Result(new { deviceId = id, setting, accepted = true,
                note = "Read status separately to verify the final device state." });
        return Result(answer);
    }

    public static async Task RunAsync()
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync();
            if (line is null) return;
            if (line.Length > 32768) continue;
            JsonElement id = default;
            try
            {
                using var request = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 12 });
                var root = request.RootElement;
                if (!root.TryGetProperty("id", out var value) ||
                    value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    continue; // JSON-RPC notifications do not receive responses.
                id = value.Clone();
                if (root.GetProperty("jsonrpc").GetString() != "2.0")
                    throw new InvalidDataException("Invalid JSON-RPC version.");
                var method = Field(root, "method");
                object response;
                if (method == "initialize")
                    response = new { protocolVersion = Protocol,
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "fluentcontrol-sensors", version = "0.1.0-dev" } };
                else if (method == "ping") response = new { };
                else if (method == "tools/list") response = new { tools = Tools() };
                else if (method == "tools/call")
                {
                    if (!root.TryGetProperty("params", out var par) || par.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("Missing MCP params.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                    response = await CallAsync(par, timeout.Token);
                }
                else throw new InvalidDataException("Unsupported MCP method.");
                Console.Out.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = response }));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or
                InvalidOperationException or JsonException or TimeoutException or OperationCanceledException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                if (id.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                Console.Out.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id,
                    error = new { code = -32603, message = "FCS request failed: " + ex.GetType().Name } }));
            }
        }
    }

    // Deliberately unused by clients: tool arguments remain the sole parsed input.
    private readonly struct JsonElementWrapper
    {
        private readonly JsonElement input;
        public JsonElementWrapper(JsonElement input) => this.input = input;
        public JsonElement WithArguments() => input;
    }

    public static void SelfTest()
    {
        if (Tools().Length < 5 || Tools().OfType<object>().Any(x => x is null))
            throw new InvalidOperationException("MCP tool catalog missing.");
        if (CanWrite && Tools().Length != 6 || !CanWrite && Tools().Length != 5)
            throw new InvalidOperationException("MCP write gate invalid.");
    }
}
