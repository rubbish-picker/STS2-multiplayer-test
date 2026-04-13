using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Platform;

namespace BetaDirectConnect;

public sealed class BetaDirectConnectRuntimeConfig
{
    [JsonPropertyName("host_port")]
    public int HostPort { get; set; } = 33771;

    [JsonPropertyName("join_ip")]
    public string JoinIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("join_port")]
    public int JoinPort { get; set; } = 33771;

    [JsonPropertyName("display_id")]
    public string DisplayId { get; set; } = "";

    [JsonPropertyName("net_id_override")]
    public ulong? NetIdOverride { get; set; }

    [JsonPropertyName("net_id_override_text")]
    public string NetIdOverrideText { get; set; } = "";

    [JsonPropertyName("last_assigned_net_id")]
    public ulong LastAssignedNetId { get; set; }
}

public static class BetaDirectConnectConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static BetaDirectConnectRuntimeConfig Current { get; private set; } = new();

    public static ulong EffectiveClientId => GetEffectiveClientId();

    public static string DefaultDisplayId => EffectiveClientId.ToString();

    public static string ConfigPath => Path.Combine(GetModDirectory(), "BetaDirectConnect.runtime.config");

    public static void Initialize()
    {
        Reload();
        MainFile.Logger.Info($"BetaDirectConnect effective client ID: {EffectiveClientId}");
    }

    public static void Reload()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Current = new BetaDirectConnectRuntimeConfig();
                Save();
                return;
            }

            string json = File.ReadAllText(ConfigPath);
            Current = JsonSerializer.Deserialize<BetaDirectConnectRuntimeConfig>(json, JsonOptions) ?? new BetaDirectConnectRuntimeConfig();
            NormalizeInPlace(Current);
        }
        catch (Exception ex)
        {
            Current = new BetaDirectConnectRuntimeConfig();
            MainFile.Logger.Error($"Failed to load BetaDirectConnect config: {ex}");
        }
    }

    public static void Save()
    {
        NormalizeInPlace(Current);
        Directory.CreateDirectory(GetModDirectory());
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public static void UpdateHostPort(int port)
    {
        Current.HostPort = NormalizePort(port);
        Save();
    }

    public static void UpdateJoinSettings(string ip, int port, string displayId, string netIdOverrideText, ulong? netIdOverride)
    {
        Current.JoinIp = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
        Current.JoinPort = NormalizePort(port);
        Current.DisplayId = NormalizeDisplayId(displayId);
        Current.NetIdOverride = NormalizeNetId(netIdOverride);
        Current.NetIdOverrideText = NormalizeNetIdOverrideText(netIdOverrideText);
        Save();
    }

    public static void UpdateIdentitySettings(string displayId, string netIdOverrideText, ulong? netIdOverride)
    {
        Current.DisplayId = NormalizeDisplayId(displayId);
        Current.NetIdOverride = NormalizeNetId(netIdOverride);
        Current.NetIdOverrideText = NormalizeNetIdOverrideText(netIdOverrideText);
        Save();
    }

    public static void UpdateLastAssignedNetId(ulong netId)
    {
        Current.LastAssignedNetId = NormalizeNetId(netId) ?? 0UL;
        Save();
    }

    public static int NormalizePort(int port)
    {
        return Math.Clamp(port, 1, 65535);
    }

    public static ulong? NormalizeNetId(ulong? playerId)
    {
        if (!playerId.HasValue || playerId.Value == 0UL)
        {
            return null;
        }

        return playerId.Value;
    }

    private static void NormalizeInPlace(BetaDirectConnectRuntimeConfig config)
    {
        config.HostPort = NormalizePort(config.HostPort);
        config.JoinPort = NormalizePort(config.JoinPort);
        config.JoinIp = string.IsNullOrWhiteSpace(config.JoinIp) ? "127.0.0.1" : config.JoinIp.Trim();
        config.DisplayId = NormalizeDisplayId(config.DisplayId);
        config.NetIdOverride = NormalizeNetId(config.NetIdOverride);
        config.NetIdOverrideText = NormalizeNetIdOverrideText(config.NetIdOverrideText);
        config.LastAssignedNetId = NormalizeNetId(config.LastAssignedNetId) ?? 0UL;
    }

    public static string NormalizeDisplayId(string input)
    {
        return string.IsNullOrWhiteSpace(input) ? DefaultDisplayId : input.Trim();
    }

    public static ulong? ParseNetIdOverrideInput(string input)
    {
        string normalized = NormalizeNetIdOverrideText(input);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (!ulong.TryParse(normalized, out ulong numericPlayerId))
        {
            throw new FormatException("Net ID override must be an unsigned integer.");
        }

        return NormalizeNetId(numericPlayerId);
    }

    public static string NormalizeNetIdOverrideText(string input)
    {
        return string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();
    }

    public static ulong CreateStableAutomaticNetIdSeed()
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"client:{EffectiveClientId}"));
        ulong hashedPlayerId = BitConverter.ToUInt64(bytes, 0);
        if (BitConverter.IsLittleEndian)
        {
            hashedPlayerId = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(hashedPlayerId);
        }

        hashedPlayerId &= 0x7FFFFFFFFFFFFFFFUL;
        if (hashedPlayerId <= 1UL)
        {
            hashedPlayerId += 2UL;
        }

        return hashedPlayerId;
    }

    public static ulong? GetRequestedNetId()
    {
        if (Current.NetIdOverride.HasValue)
        {
            return Current.NetIdOverride.Value;
        }

        return null;
    }

    private static ulong GetEffectiveClientId()
    {
        if (CommandLineHelper.TryGetValue("clientId", out string? rawValue)
            && ulong.TryParse(rawValue, out ulong parsedClientId)
            && parsedClientId > 0UL)
        {
            return parsedClientId;
        }

        ulong platformClientId = PlatformUtil.GetLocalPlayerId(PlatformType.None);
        return platformClientId > 0UL ? platformClientId : 1UL;
    }

    private static string GetModDirectory()
    {
        string? location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        return string.IsNullOrWhiteSpace(location) ? AppContext.BaseDirectory : location;
    }
}
