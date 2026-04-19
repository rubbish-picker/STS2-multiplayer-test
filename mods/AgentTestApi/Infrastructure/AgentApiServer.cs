using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace AgentTestApi.Infrastructure;

internal sealed class AgentApiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AgentTestApiNode _owner;
    private readonly AgentApiOptions _options;
    private readonly CancellationTokenSource _stopCts = new();
    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private bool _disposed;

    public AgentApiServer(AgentTestApiNode owner, AgentApiOptions options)
    {
        _owner = owner;
        _options = options;
    }

    public void Start()
    {
        if (_listener != null)
        {
            return;
        }

        _listener = new TcpListener(_options.ListenAddress, _options.Port);
        _listener.Server.NoDelay = true;
        _listener.Start();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_stopCts.Token));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopCts.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch
        {
        }

        try
        {
            _acceptLoopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _stopCts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                MainFile.Logger.Error($"[AgentTestApi] Accept loop error: {ex}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using NetworkStream stream = client.GetStream();
        using (client)
        {
            try
            {
                AgentHttpRequest request = await ReadRequestAsync(stream, cancellationToken);
                AgentHttpResponse response = await RouteRequestAsync(request, cancellationToken);
                await WriteResponseAsync(stream, response, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"[AgentTestApi] Request handling error: {ex}");
                AgentHttpResponse response = CreateJsonErrorResponse(
                    statusCode: 500,
                    reasonPhrase: "Internal Server Error",
                    message: ex.Message,
                    details: ex.ToString());

                try
                {
                    await WriteResponseAsync(stream, response, cancellationToken);
                }
                catch
                {
                }
            }
        }
    }

    private async Task<AgentHttpResponse> RouteRequestAsync(AgentHttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return (request.Method, request.Path) switch
            {
                ("GET", "/") => CreateJsonOkResponse(new
                {
                    mod = MainFile.ModId,
                    baseUrl = _options.BaseUrl,
                    endpoints = new[]
                    {
                        "GET /health",
                        "GET /state",
                        "GET /screenshot",
                        "POST /screenshot",
                        "POST /run/start",
                        "POST /run/reset",
                        "POST /fight",
                        "POST /cards/pile",
                        "POST /cards/spawn",
                        "POST /cards/draw",
                        "POST /cards/play",
                        "POST /combat/end-turn",
                        "POST /console",
                        "POST /input/action",
                        "POST /input/key",
                        "POST /input/focus-default"
                    },
                    actionAliases = AgentApiInput.GetActionAliases()
                }),
                ("GET", "/health") => CreateJsonOkResponse(_owner.BuildHealthSnapshot()),
                ("GET", "/state") => CreateJsonOkResponse(await _owner.RunOnMainThreadAsync(_owner.BuildStateSnapshot)),
                ("GET", "/screenshot") => CreateBinaryResponse(
                    contentType: "image/png",
                    bodyBytes: await _owner.RunOnMainThreadAsync(_owner.CaptureScreenshotBytes)),
                ("POST", "/screenshot") => await HandleScreenshotSaveAsync(request),
                ("POST", "/run/start") => await HandleRunStartAsync(request),
                ("POST", "/run/reset") => await HandleRunResetAsync(),
                ("POST", "/fight") => await HandleFightAsync(request, cancellationToken),
                ("POST", "/cards/pile") => await HandlePileQueryAsync(request),
                ("POST", "/cards/spawn") => await HandleSpawnCardsAsync(request),
                ("POST", "/cards/draw") => await HandleDrawCardsAsync(request),
                ("POST", "/cards/play") => await HandlePlayCardAsync(request, cancellationToken),
                ("POST", "/combat/end-turn") => await HandleEndTurnAsync(),
                ("POST", "/console") => await HandleConsoleCommandAsync(request, cancellationToken),
                ("POST", "/input/action") => await HandleActionInputAsync(request),
                ("POST", "/input/key") => await HandleKeyInputAsync(request),
                ("POST", "/input/focus-default") => CreateJsonOkResponse(await _owner.RunOnMainThreadAsync(_owner.FocusDefaultControl)),
                _ => CreateJsonErrorResponse(
                    statusCode: 404,
                    reasonPhrase: "Not Found",
                    message: $"Unknown endpoint '{request.Method} {request.Path}'.")
            };
        }
        catch (JsonException ex)
        {
            return CreateJsonErrorResponse(
                statusCode: 400,
                reasonPhrase: "Bad Request",
                message: "Invalid JSON body.",
                details: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CreateJsonErrorResponse(
                statusCode: 400,
                reasonPhrase: "Bad Request",
                message: ex.Message);
        }
        catch (TimeoutException ex)
        {
            return CreateJsonErrorResponse(
                statusCode: 408,
                reasonPhrase: "Request Timeout",
                message: ex.Message);
        }
    }

    private async Task<AgentHttpResponse> HandleRunStartAsync(AgentHttpRequest request)
    {
        AgentRunStartRequest runRequest = DeserializeBody<AgentRunStartRequest>(request);
        AgentRunStartResponse response = await _owner.RunOnMainThreadAsync(() => _owner.StartSingleplayerRunAsync(runRequest));
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleRunResetAsync()
    {
        AgentResetRunResponse response = await _owner.RunOnMainThreadAsync(_owner.ReturnToMainMenuAsync);
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleFightAsync(AgentHttpRequest request, CancellationToken cancellationToken)
    {
        AgentFightRequest fightRequest = DeserializeBody<AgentFightRequest>(request);
        string encounterId = await _owner.RunOnMainThreadAsync(() => _owner.EnterFightAsync(fightRequest));
        AgentCombatStateData combat = await WaitForCombatReadyAsync(timeoutMs: 15000, cancellationToken);

        return CreateJsonOkResponse(new AgentFightResponse
        {
            EncounterId = encounterId,
            Combat = combat
        });
    }

    private async Task<AgentHttpResponse> HandlePileQueryAsync(AgentHttpRequest request)
    {
        AgentPileQueryRequest pileRequest = DeserializeBody<AgentPileQueryRequest>(request);
        AgentPileCardsResponse response = await _owner.RunOnMainThreadAsync(() => _owner.BuildPileSnapshot(pileRequest));
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleSpawnCardsAsync(AgentHttpRequest request)
    {
        AgentSpawnCardsRequest spawnRequest = DeserializeBody<AgentSpawnCardsRequest>(request);
        AgentSpawnCardsResponse response = await _owner.RunOnMainThreadAsync(() => _owner.SpawnCardsAsync(spawnRequest));
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleDrawCardsAsync(AgentHttpRequest request)
    {
        AgentDrawCardsRequest drawRequest = DeserializeBody<AgentDrawCardsRequest>(request);
        AgentDrawCardsResponse response = await _owner.RunOnMainThreadAsync(() => _owner.DrawCardsAsync(drawRequest));
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandlePlayCardAsync(AgentHttpRequest request, CancellationToken cancellationToken)
    {
        AgentPlayCardRequest playRequest = DeserializeBody<AgentPlayCardRequest>(request);
        AgentCardOperationContext? context = null;
        try
        {
            context = await _owner.RunOnMainThreadAsync(() => _owner.BeginManualPlay(playRequest));

            if (playRequest.WaitForResolution)
            {
                int timeoutMs = playRequest.TimeoutMs > 0 ? playRequest.TimeoutMs : 10000;
                await WaitUntilAsync(
                    predicate: () => _owner.RunOnMainThreadAsync(() => _owner.IsCardOperationSettled(context)),
                    timeoutMs: timeoutMs,
                    pollIntervalMs: 50,
                    timeoutMessage: $"Card operation did not settle within {timeoutMs} ms.",
                    cancellationToken: cancellationToken);
            }

            AgentCardStateData? cardAfter = await _owner.RunOnMainThreadAsync(() => _owner.TryBuildCardState(context));
            AgentCombatStateData? combatAfter = await _owner.RunOnMainThreadAsync(_owner.TryBuildCombatState);

            return CreateJsonOkResponse(new AgentPlayCardResponse
            {
                Success = true,
                Message = playRequest.WaitForResolution ? "Card played and resolved." : "Card play was enqueued.",
                CardBefore = context.CardBefore,
                CardAfter = cardAfter,
                CombatBefore = context.CombatBefore,
                CombatAfter = combatAfter
            });
        }
        finally
        {
            if (context != null)
            {
                await _owner.RunOnMainThreadAsync(() =>
                {
                    _owner.CompleteCardOperation(context);
                    return true;
                });
            }
        }
    }

    private async Task<AgentHttpResponse> HandleEndTurnAsync()
    {
        AgentEndTurnResponse response = await _owner.RunOnMainThreadAsync(_owner.EndTurn);
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleConsoleCommandAsync(AgentHttpRequest request, CancellationToken cancellationToken)
    {
        AgentConsoleRequest commandRequest = DeserializeBody<AgentConsoleRequest>(request);
        AgentConsoleCommandResponse response = await _owner.RunOnMainThreadAsync(() => _owner.ExecuteConsoleCommand(commandRequest.Command ?? string.Empty));

        if (response.PendingTask != null)
        {
            await response.PendingTask.WaitAsync(cancellationToken);
        }

        return CreateJsonOkResponse(new
        {
            success = response.Success,
            message = response.Message,
            hasPendingTask = response.HasPendingTask
        });
    }

    private async Task<AgentHttpResponse> HandleScreenshotSaveAsync(AgentHttpRequest request)
    {
        AgentScreenshotRequest screenshotRequest = DeserializeBody<AgentScreenshotRequest>(request);
        byte[] pngBytes = await _owner.RunOnMainThreadAsync(_owner.CaptureScreenshotBytes);
        string path = ResolveScreenshotPath(screenshotRequest.Path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, pngBytes);

        return CreateJsonOkResponse(new AgentScreenshotSavedResponse
        {
            Path = path,
            ByteCount = pngBytes.Length
        });
    }

    private async Task<AgentHttpResponse> HandleActionInputAsync(AgentHttpRequest request)
    {
        AgentInputActionRequest inputRequest = DeserializeBody<AgentInputActionRequest>(request);
        AgentInputResponse response = await _owner.RunOnMainThreadAsync(() => _owner.InjectAction(inputRequest));
        return CreateJsonOkResponse(response);
    }

    private async Task<AgentHttpResponse> HandleKeyInputAsync(AgentHttpRequest request)
    {
        AgentInputKeyRequest inputRequest = DeserializeBody<AgentInputKeyRequest>(request);
        AgentInputResponse response = await _owner.RunOnMainThreadAsync(() => _owner.InjectKey(inputRequest));
        return CreateJsonOkResponse(response);
    }

    private static T DeserializeBody<T>(AgentHttpRequest request) where T : new()
    {
        if (request.BodyBytes.Length == 0)
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(request.BodyBytes, JsonOptions) ?? new T();
    }

    private static string ResolveScreenshotPath(string? requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            string userDir = ProjectSettings.GlobalizePath("user://agent_test_api");
            return Path.Combine(userDir, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        }

        string trimmed = requestedPath.Trim();
        if (trimmed.StartsWith("user://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(trimmed);
        }

        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        string userDirFallback = ProjectSettings.GlobalizePath("user://agent_test_api");
        return Path.Combine(userDirFallback, trimmed);
    }

    private static async Task<AgentHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        int totalRead = 0;
        int headerEndIndex = -1;

        while (headerEndIndex < 0)
        {
            if (totalRead == buffer.Length)
            {
                throw new InvalidOperationException("Request headers exceed 64 KB.");
            }

            int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Connection closed while reading request.");
            }

            totalRead += bytesRead;
            headerEndIndex = FindHeaderEnd(buffer, totalRead);
        }

        string headerText = Encoding.UTF8.GetString(buffer, 0, headerEndIndex);
        string[] headerLines = headerText.Split("\r\n", StringSplitOptions.None);
        if (headerLines.Length == 0)
        {
            throw new InvalidOperationException("Missing request line.");
        }

        string[] requestLineParts = headerLines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLineParts.Length < 3)
        {
            throw new InvalidOperationException("Malformed request line.");
        }

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in headerLines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            headers[key] = value;
        }

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out string? contentLengthValue) &&
            !int.TryParse(contentLengthValue, out contentLength))
        {
            throw new InvalidOperationException("Invalid Content-Length header.");
        }

        int bodyOffset = headerEndIndex + 4;
        byte[] bodyBytes = new byte[contentLength];
        int copiedBytes = Math.Max(0, Math.Min(totalRead - bodyOffset, contentLength));
        if (copiedBytes > 0)
        {
            Buffer.BlockCopy(buffer, bodyOffset, bodyBytes, 0, copiedBytes);
        }

        while (copiedBytes < contentLength)
        {
            int bytesRead = await stream.ReadAsync(bodyBytes.AsMemory(copiedBytes, contentLength - copiedBytes), cancellationToken);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Connection closed while reading request body.");
            }

            copiedBytes += bytesRead;
        }

        string rawTarget = requestLineParts[1];
        string path = rawTarget.Split('?', 2)[0];

        return new AgentHttpRequest
        {
            Method = requestLineParts[0].ToUpperInvariant(),
            Path = path,
            HttpVersion = requestLineParts[2],
            Headers = headers,
            BodyBytes = bodyBytes
        };
    }

    private static async Task WriteResponseAsync(NetworkStream stream, AgentHttpResponse response, CancellationToken cancellationToken)
    {
        StringBuilder headers = new();
        headers.Append("HTTP/1.1 ").Append(response.StatusCode).Append(' ').Append(response.ReasonPhrase).Append("\r\n");
        headers.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
        headers.Append("Content-Length: ").Append(response.BodyBytes.Length).Append("\r\n");
        headers.Append("Connection: close\r\n");
        headers.Append("\r\n");

        byte[] headerBytes = Encoding.UTF8.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes, cancellationToken);

        if (response.BodyBytes.Length > 0)
        {
            await stream.WriteAsync(response.BodyBytes, cancellationToken);
        }
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (int i = 0; i <= length - 4; i++)
        {
            if (buffer[i] == '\r' &&
                buffer[i + 1] == '\n' &&
                buffer[i + 2] == '\r' &&
                buffer[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static AgentHttpResponse CreateJsonOkResponse(object data)
    {
        AgentApiEnvelope envelope = new()
        {
            Ok = true,
            Data = data
        };

        return CreateJsonResponse(statusCode: 200, reasonPhrase: "OK", envelope);
    }

    private static AgentHttpResponse CreateJsonErrorResponse(int statusCode, string reasonPhrase, string message, string? details = null)
    {
        AgentApiEnvelope envelope = new()
        {
            Ok = false,
            Error = new AgentApiError
            {
                Message = message,
                Details = details
            }
        };

        return CreateJsonResponse(statusCode, reasonPhrase, envelope);
    }

    private static AgentHttpResponse CreateJsonResponse(int statusCode, string reasonPhrase, AgentApiEnvelope envelope)
    {
        byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        return new AgentHttpResponse
        {
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            ContentType = "application/json; charset=utf-8",
            BodyBytes = bodyBytes
        };
    }

    private static AgentHttpResponse CreateBinaryResponse(string contentType, byte[] bodyBytes)
    {
        return new AgentHttpResponse
        {
            StatusCode = 200,
            ReasonPhrase = "OK",
            ContentType = contentType,
            BodyBytes = bodyBytes
        };
    }

    private async Task<AgentCombatStateData> WaitForCombatReadyAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        AgentCombatStateData? combat = null;
        await WaitUntilAsync(
            predicate: async () =>
            {
                combat = await _owner.RunOnMainThreadAsync(_owner.TryBuildCombatState);
                return combat is { IsInProgress: true, PlayerActionsDisabled: false };
            },
            timeoutMs: timeoutMs,
            pollIntervalMs: 100,
            timeoutMessage: $"Combat did not become ready within {timeoutMs} ms.",
            cancellationToken: cancellationToken);

        return combat ?? throw new InvalidOperationException("Combat snapshot was unavailable after readiness wait.");
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> predicate,
        int timeoutMs,
        int pollIntervalMs,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            while (true)
            {
                if (await predicate())
                {
                    return;
                }

                await Task.Delay(pollIntervalMs, timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
