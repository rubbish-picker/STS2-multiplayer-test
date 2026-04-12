using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BetaDirectConnect;

public static class BetaDirectConnectUi
{
    public const string JoinPanelName = "BetaDirectConnectJoinPanel";
    public const string HostPanelName = "BetaDirectConnectHostPanel";
    public const string LoadPanelName = "BetaDirectConnectLoadPanel";

    public static void EnsureJoinPanel(NJoinFriendScreen screen)
    {
        if (screen.GetNodeOrNull<Control>(JoinPanelName) != null)
        {
            return;
        }

        Control root = BuildJoinPanel(screen);
        screen.AddChild(root);
        root.Owner = screen;
        MainFile.Logger.Info("Created direct-connect join panel.");
    }

    public static void EnsureHostPanel(NMultiplayerHostSubmenu screen)
    {
        if (screen.GetNodeOrNull<Control>(HostPanelName) != null)
        {
            return;
        }

        Control root = BuildHostPanel();
        screen.AddChild(root);
        root.Owner = screen;
    }

    public static void EnsureLoadPanel(NMultiplayerSubmenu screen)
    {
        if (screen.GetNodeOrNull<Control>(LoadPanelName) != null)
        {
            return;
        }

        Control root = BuildLoadPanel();
        screen.AddChild(root);
        root.Owner = screen;
    }

    public static void ShowJoinPanelOnly(NJoinFriendScreen screen)
    {
        screen.GetNode<Control>("%ButtonContainer").Visible = false;
        screen.GetNode<Control>("%LoadingIndicator").Visible = false;
        screen.GetNode<Control>("%NoFriendsText").Visible = false;
        screen.GetNode<Control>("%RefreshButton").Visible = false;

        EnsureJoinPanel(screen);
        if (screen.GetNodeOrNull<Control>(JoinPanelName) is Control panel)
        {
            panel.Visible = true;
        }

        MainFile.Logger.Info("Showing direct-connect join panel.");
    }

    public static int GetConfiguredHostPort(NMultiplayerHostSubmenu screen)
    {
        if (screen.GetNodeOrNull<LineEdit>($"{HostPanelName}/Column/PortInput") is not LineEdit portInput)
        {
            return BetaDirectConnectConfigService.Current.HostPort;
        }

        if (!int.TryParse(portInput.Text.Trim(), out int port))
        {
            port = BetaDirectConnectConfigService.Current.HostPort;
        }

        port = BetaDirectConnectConfigService.NormalizePort(port);
        portInput.Text = port.ToString();
        return port;
    }

    public static string GetConfiguredHostDisplayId(NMultiplayerHostSubmenu screen)
    {
        return GetNormalizedText(screen.GetNodeOrNull<LineEdit>($"{HostPanelName}/Column/DisplayIdInput"), BetaDirectConnectConfigService.Current.DisplayId);
    }

    public static ulong? GetConfiguredHostNetIdOverride(NMultiplayerHostSubmenu screen)
    {
        return GetConfiguredOptionalNetId(screen.GetNodeOrNull<LineEdit>($"{HostPanelName}/Column/NetIdOverrideInput"));
    }

    public static int GetConfiguredLoadPort(NMultiplayerSubmenu screen)
    {
        if (screen.GetNodeOrNull<LineEdit>($"{LoadPanelName}/Column/PortInput") is not LineEdit portInput)
        {
            return BetaDirectConnectConfigService.Current.HostPort;
        }

        if (!int.TryParse(portInput.Text.Trim(), out int port))
        {
            port = BetaDirectConnectConfigService.Current.HostPort;
        }

        port = BetaDirectConnectConfigService.NormalizePort(port);
        portInput.Text = port.ToString();
        return port;
    }

    public static string GetConfiguredLoadDisplayId(NMultiplayerSubmenu screen)
    {
        return GetNormalizedText(screen.GetNodeOrNull<LineEdit>($"{LoadPanelName}/Column/DisplayIdInput"), BetaDirectConnectConfigService.Current.DisplayId);
    }

    public static ulong? GetConfiguredLoadNetIdOverride(NMultiplayerSubmenu screen)
    {
        return GetConfiguredOptionalNetId(screen.GetNodeOrNull<LineEdit>($"{LoadPanelName}/Column/NetIdOverrideInput"));
    }

    private static Control BuildJoinPanel(NJoinFriendScreen screen)
    {
        PanelContainer panel = new()
        {
            Name = JoinPanelName,
            CustomMinimumSize = new Vector2(860, 420),
            Position = new Vector2(530, 280),
            Size = new Vector2(860, 420),
        };

        VBoxContainer column = new()
        {
            Name = "Column",
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            OffsetLeft = 24,
            OffsetTop = 24,
            OffsetRight = -24,
            OffsetBottom = -24,
        };
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        Label title = CreateLabel("Direct Connect / 直连加入", 34);
        Label desc = CreateLabel(
            "Fill in host address, port, display name, and optionally override the in-room Net ID. Leave Net ID blank to let the host assign one.",
            22);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        NMegaLineEdit ipInput = CreateLineEdit("IpInput", BetaDirectConnectConfigService.Current.JoinIp, "Host IP or domain / 房主 IP 或域名");
        NMegaLineEdit portInput = CreateLineEdit("PortInput", BetaDirectConnectConfigService.Current.JoinPort.ToString(), "Port / 端口，默认 33771");
        NMegaLineEdit displayIdInput = CreateLineEdit("DisplayIdInput", BetaDirectConnectConfigService.Current.DisplayId, "Display Name / 显示名称");
        NMegaLineEdit netIdOverrideInput = CreateLineEdit("NetIdOverrideInput", BetaDirectConnectConfigService.Current.NetIdOverrideText, "Optional Net ID Override / 可选联机身份 ID");
        Button connectButton = new()
        {
            Name = "ConnectButton",
            Text = "Connect / 连接",
            CustomMinimumSize = new Vector2(0, 54),
            FocusMode = Control.FocusModeEnum.All,
        };
        Label hint = CreateLabel(
            "Display Name only affects UI. Net ID is the actual multiplayer identity. Leave Net ID blank for normal host-assigned play; only fill it when you need to reclaim or swap an in-run identity.",
            18);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Modulate = new Color(0.85f, 0.85f, 0.8f);

        connectButton.Pressed += () =>
        {
            MainFile.Logger.Info("Connect button pressed.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, displayIdInput, netIdOverrideInput);
        };
        displayIdInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("Display ID submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, displayIdInput, netIdOverrideInput);
        };
        netIdOverrideInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("Net ID override submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, displayIdInput, netIdOverrideInput);
        };
        portInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("Port submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, displayIdInput, netIdOverrideInput);
        };
        ipInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("IP submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, displayIdInput, netIdOverrideInput);
        };

        column.AddChild(title);
        column.AddChild(desc);
        column.AddChild(CreateFieldBlock("Host Address / 房主地址", ipInput));
        column.AddChild(CreateFieldBlock("Port / 端口", portInput));
        column.AddChild(CreateFieldBlock("Display Name / 显示名称", displayIdInput));
        column.AddChild(CreateFieldBlock("Net ID Override / 联机身份 ID 覆盖", netIdOverrideInput));
        column.AddChild(connectButton);
        column.AddChild(hint);

        return panel;
    }

    private static Control BuildHostPanel()
    {
        PanelContainer panel = new()
        {
            Name = HostPanelName,
            CustomMinimumSize = new Vector2(760, 170),
            Position = new Vector2(580, 820),
            Size = new Vector2(760, 170),
        };

        VBoxContainer column = new()
        {
            Name = "Column",
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            OffsetLeft = 24,
            OffsetTop = 18,
            OffsetRight = -24,
            OffsetBottom = -18,
        };
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        Label title = CreateLabel("Beta Direct Connect / Beta 直连房间", 26);
        Label desc = CreateLabel(
            "Direct-connect hosting opens an ENet room on this port. Set the display name shown to others and optionally pin your own multiplayer Net ID.",
            18);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.Modulate = new Color(0.85f, 0.85f, 0.8f);
        NMegaLineEdit portInput = CreateLineEdit("PortInput", BetaDirectConnectConfigService.Current.HostPort.ToString(), "Host port / 房主端口");
        NMegaLineEdit displayIdInput = CreateLineEdit("DisplayIdInput", BetaDirectConnectConfigService.Current.DisplayId, "Display Name / 显示名称");
        NMegaLineEdit netIdOverrideInput = CreateLineEdit("NetIdOverrideInput", BetaDirectConnectConfigService.Current.NetIdOverrideText, "Optional Net ID Override / 可选联机身份 ID");

        column.AddChild(title);
        column.AddChild(desc);
        column.AddChild(CreateFieldBlock("Host Port / 房主端口", portInput));
        column.AddChild(CreateFieldBlock("Display Name / 显示名称", displayIdInput));
        column.AddChild(CreateFieldBlock("Net ID Override / 联机身份 ID 覆盖", netIdOverrideInput));

        return panel;
    }

    private static Control BuildLoadPanel()
    {
        PanelContainer panel = new()
        {
            Name = LoadPanelName,
            CustomMinimumSize = new Vector2(760, 150),
            Position = new Vector2(580, 820),
            Size = new Vector2(760, 150),
        };

        VBoxContainer column = new()
        {
            Name = "Column",
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            OffsetLeft = 24,
            OffsetTop = 18,
            OffsetRight = -24,
            OffsetBottom = -18,
        };
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        Label title = CreateLabel("Load Direct Connect / 读档直连开房", 24);
        Label desc = CreateLabel(
            "When reopening a saved multiplayer run, the host will bind this port before entering the load lobby. You can also reclaim a specific saved Net ID here.",
            18);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.Modulate = new Color(0.85f, 0.85f, 0.8f);
        NMegaLineEdit portInput = CreateLineEdit("PortInput", BetaDirectConnectConfigService.Current.HostPort.ToString(), "Host port / 房主端口");
        NMegaLineEdit displayIdInput = CreateLineEdit("DisplayIdInput", BetaDirectConnectConfigService.Current.DisplayId, "Display Name / 显示名称");
        NMegaLineEdit netIdOverrideInput = CreateLineEdit("NetIdOverrideInput", BetaDirectConnectConfigService.Current.NetIdOverrideText, "Optional Net ID Override / 可选联机身份 ID");

        column.AddChild(title);
        column.AddChild(desc);
        column.AddChild(CreateFieldBlock("Load Host Port / 读档房主端口", portInput));
        column.AddChild(CreateFieldBlock("Display Name / 显示名称", displayIdInput));
        column.AddChild(CreateFieldBlock("Net ID Override / 联机身份 ID 覆盖", netIdOverrideInput));

        return panel;
    }

    private static ulong? GetConfiguredOptionalNetId(LineEdit? input)
    {
        if (input == null)
        {
            return BetaDirectConnectConfigService.Current.NetIdOverride;
        }

        string text = BetaDirectConnectConfigService.NormalizeNetIdOverrideText(input.Text);
        input.Text = text;
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return BetaDirectConnectConfigService.ParseNetIdOverrideInput(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string GetNormalizedText(LineEdit? input, string fallback)
    {
        string text = BetaDirectConnectConfigService.NormalizeDisplayId(input?.Text ?? fallback);
        if (input != null)
        {
            input.Text = text;
        }

        return text;
    }

    private static Control CreateFieldBlock(string labelText, Control input)
    {
        VBoxContainer box = new();
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(CreateLabel(labelText, 18));
        box.AddChild(input);
        return box;
    }

    private static Label CreateLabel(string text, int fontSize)
    {
        Label label = new()
        {
            Text = text,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static NMegaLineEdit CreateLineEdit(string name, string text, string placeholder)
    {
        return new NMegaLineEdit
        {
            Name = name,
            Text = text,
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(0, 48),
            FocusMode = Control.FocusModeEnum.All,
        };
    }
}
