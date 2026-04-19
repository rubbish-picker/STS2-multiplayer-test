using System.Net;
using MegaCrit.Sts2.Core.Helpers;

namespace AgentTestApi.Infrastructure;

internal sealed class AgentApiOptions
{
    public required IPAddress ListenAddress { get; init; }

    public required int Port { get; init; }

    public string BaseUrl => $"http://{ListenAddress}:{Port}";

    public static AgentApiOptions FromCommandLine()
    {
        return new AgentApiOptions
        {
            ListenAddress = ParseListenAddress(
                CommandLineHelper.GetValue("testapihost")
                ?? CommandLineHelper.GetValue("agenttestapihost")
                ?? "127.0.0.1"),
            Port = ParsePort(
                CommandLineHelper.GetValue("testapiport")
                ?? CommandLineHelper.GetValue("agenttestapiport"),
                51234)
        };
    }

    private static IPAddress ParseListenAddress(string rawValue)
    {
        if (rawValue.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(rawValue, out IPAddress? ipAddress))
        {
            return ipAddress;
        }

        MainFile.Logger.Warn($"[AgentTestApi] Invalid listen address '{rawValue}', falling back to 127.0.0.1.");
        return IPAddress.Loopback;
    }

    private static int ParsePort(string? rawValue, int fallback)
    {
        if (!string.IsNullOrWhiteSpace(rawValue) &&
            int.TryParse(rawValue, out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            return parsedPort;
        }

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            MainFile.Logger.Warn($"[AgentTestApi] Invalid port '{rawValue}', falling back to {fallback}.");
        }

        return fallback;
    }
}
