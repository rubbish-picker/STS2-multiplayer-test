using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace BetaDirectConnect;

public static class BetaDirectConnectUi
{
    public const string JoinPanelName = "BetaDirectConnectJoinPanel";
    public const string HostPanelName = "BetaDirectConnectHostPanel";

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
            "Fill in host address, port, and player ID. Player ID supports any text input; numeric IDs are used directly, while text IDs are converted into a stable internal ulong.",
            22);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;

        NMegaLineEdit ipInput = CreateLineEdit("IpInput", BetaDirectConnectConfigService.Current.JoinIp, "Host IP or domain / 房主 IP 或域名");
        NMegaLineEdit portInput = CreateLineEdit("PortInput", BetaDirectConnectConfigService.Current.JoinPort.ToString(), "Port / 端口，默认 33771");
        NMegaLineEdit playerIdInput = CreateLineEdit("PlayerIdInput", BetaDirectConnectConfigService.EffectivePlayerIdText, "Player ID / 玩家 ID，可输入任意字符串");
        Button connectButton = new()
        {
            Name = "ConnectButton",
            Text = "Connect / 连接",
            CustomMinimumSize = new Vector2(0, 54),
            FocusMode = Control.FocusModeEnum.All,
        };
        Label hint = CreateLabel(
            "Numeric IDs are exact. Text IDs are converted to a stable internal ulong for the current protocol. Please avoid using very similar test IDs across many players if you want the lowest collision risk.",
            18);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.Modulate = new Color(0.85f, 0.85f, 0.8f);

        connectButton.Pressed += () =>
        {
            MainFile.Logger.Info("Connect button pressed.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, playerIdInput);
        };
        playerIdInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("Player ID submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, playerIdInput);
        };
        portInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("Port submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, playerIdInput);
        };
        ipInput.TextSubmitted += _ =>
        {
            MainFile.Logger.Info("IP submit triggered direct join.");
            BetaDirectConnectPatches.TryJoinFromPanel(screen, ipInput, portInput, playerIdInput);
        };

        column.AddChild(title);
        column.AddChild(desc);
        column.AddChild(CreateFieldBlock("Host Address / 房主地址", ipInput));
        column.AddChild(CreateFieldBlock("Port / 端口", portInput));
        column.AddChild(CreateFieldBlock("Player ID / 玩家 ID", playerIdInput));
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
            "Direct-connect hosting opens an ENet room on this port. Share your IP/domain and port with testers.",
            18);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.Modulate = new Color(0.85f, 0.85f, 0.8f);
        NMegaLineEdit portInput = CreateLineEdit("PortInput", BetaDirectConnectConfigService.Current.HostPort.ToString(), "Host port / 房主端口");

        column.AddChild(title);
        column.AddChild(desc);
        column.AddChild(CreateFieldBlock("Host Port / 房主端口", portInput));

        return panel;
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
