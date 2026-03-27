using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.addons.mega_text;

namespace AiEvent;

public static class AiEventMainMenuIntegration
{
    private const string ButtonName = "AiEventCacheManagerButton";
    private const string OverlayName = "AiEventCacheManagerOverlay";

    public static void InstallButton(NMainMenu mainMenu)
    {
        Control buttonContainer = mainMenu.GetNode<Control>("%MainMenuTextButtons");
        if (buttonContainer.GetNodeOrNull<NMainMenuTextButton>(ButtonName) != null)
        {
            return;
        }

        NMainMenuTextButton template = buttonContainer.GetNode<NMainMenuTextButton>("QuitButton");
        NMainMenuTextButton button = template.Duplicate() as NMainMenuTextButton
            ?? throw new InvalidOperationException("Failed to duplicate main menu button for ai-event.");

        button.Name = ButtonName;
        button.SetLocalization("AI_EVENT_CACHE_MANAGER");
        DisconnectAllReleasedSignals(button);
        buttonContainer.AddChild(button);
        buttonContainer.MoveChild(button, template.GetIndex());
    }

    public static void FinalizeMenu(NMainMenu mainMenu)
    {
        NMainMenuTextButton? button = mainMenu.GetNodeOrNull<NMainMenuTextButton>($"%MainMenuTextButtons/{ButtonName}");
        if (button == null)
        {
            return;
        }

        if (!(button.GetMeta("ai_event_connected", false).AsBool()))
        {
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => OpenOverlay(mainMenu)));
            button.SetMeta("ai_event_connected", true);
        }

        ApplyVisibleText(button, "AI事件缓存管理");

        if (mainMenu.GetNodeOrNull<AiEventCacheManagerOverlay>(OverlayName) == null)
        {
            AiEventCacheManagerOverlay overlay = AiEventCacheManagerOverlay.Create();
            overlay.Name = OverlayName;
            mainMenu.AddChild(overlay);
        }
    }

    public static void OpenOverlay(NMainMenu mainMenu)
    {
        AiEventCacheManagerOverlay? overlay = mainMenu.GetNodeOrNull<AiEventCacheManagerOverlay>(OverlayName);
        overlay?.Open();
    }

    private static void ApplyVisibleText(NMainMenuTextButton button, string text)
    {
        MegaLabel? label = button.label ?? button.GetChildOrNull<MegaLabel>(0);
        if (label == null)
        {
            return;
        }

        label.Text = text;
        Callable.From(() => label.PivotOffset = label.Size * 0.5f).CallDeferred();
    }

    private static void DisconnectAllReleasedSignals(NMainMenuTextButton button)
    {
        foreach (Godot.Collections.Dictionary connection in button.GetSignalConnectionList(NClickableControl.SignalName.Released))
        {
            Callable callable = (Callable)connection["callable"];
            if (button.IsConnected(NClickableControl.SignalName.Released, callable))
            {
                button.Disconnect(NClickableControl.SignalName.Released, callable);
            }
        }
    }
}
