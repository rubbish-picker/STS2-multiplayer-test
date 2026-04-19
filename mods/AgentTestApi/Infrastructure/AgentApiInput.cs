using Godot;
using MegaCrit.Sts2.Core.ControllerInput;

namespace AgentTestApi.Infrastructure;

internal static class AgentApiInput
{
    private static readonly IReadOnlyDictionary<string, string> ActionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["up"] = MegaInput.up.ToString(),
            ["ui_up"] = MegaInput.up.ToString(),
            ["down"] = MegaInput.down.ToString(),
            ["ui_down"] = MegaInput.down.ToString(),
            ["left"] = MegaInput.left.ToString(),
            ["ui_left"] = MegaInput.left.ToString(),
            ["right"] = MegaInput.right.ToString(),
            ["ui_right"] = MegaInput.right.ToString(),
            ["accept"] = MegaInput.accept.ToString(),
            ["ui_accept"] = MegaInput.accept.ToString(),
            ["select"] = MegaInput.select.ToString(),
            ["ui_select"] = MegaInput.select.ToString(),
            ["cancel"] = MegaInput.cancel.ToString(),
            ["ui_cancel"] = MegaInput.cancel.ToString(),
            ["top_panel"] = MegaInput.topPanel.ToString(),
            ["mega_top_panel"] = MegaInput.topPanel.ToString(),
            ["view_map"] = MegaInput.viewMap.ToString(),
            ["mega_view_map"] = MegaInput.viewMap.ToString(),
            ["view_draw_pile"] = MegaInput.viewDrawPile.ToString(),
            ["mega_view_draw_pile"] = MegaInput.viewDrawPile.ToString(),
            ["view_discard_pile"] = MegaInput.viewDiscardPile.ToString(),
            ["mega_view_discard_pile"] = MegaInput.viewDiscardPile.ToString(),
            ["view_deck_tab_left"] = MegaInput.viewDeckAndTabLeft.ToString(),
            ["mega_view_deck_and_tab_left"] = MegaInput.viewDeckAndTabLeft.ToString(),
            ["view_exhaust_tab_right"] = MegaInput.viewExhaustPileAndTabRight.ToString(),
            ["mega_view_exhaust_pile_and_tab_right"] = MegaInput.viewExhaustPileAndTabRight.ToString(),
            ["pause_back"] = MegaInput.pauseAndBack.ToString(),
            ["mega_pause_and_back"] = MegaInput.pauseAndBack.ToString(),
            ["back"] = MegaInput.back.ToString(),
            ["mega_back"] = MegaInput.back.ToString(),
            ["peek"] = MegaInput.peek.ToString(),
            ["mega_peek"] = MegaInput.peek.ToString(),
            ["release_card"] = MegaInput.releaseCard.ToString(),
            ["mega_release_card"] = MegaInput.releaseCard.ToString(),
            ["select_card_1"] = MegaInput.selectCard1.ToString(),
            ["mega_select_card_1"] = MegaInput.selectCard1.ToString(),
            ["select_card_2"] = MegaInput.selectCard2.ToString(),
            ["mega_select_card_2"] = MegaInput.selectCard2.ToString(),
            ["select_card_3"] = MegaInput.selectCard3.ToString(),
            ["mega_select_card_3"] = MegaInput.selectCard3.ToString(),
            ["select_card_4"] = MegaInput.selectCard4.ToString(),
            ["mega_select_card_4"] = MegaInput.selectCard4.ToString(),
            ["select_card_5"] = MegaInput.selectCard5.ToString(),
            ["mega_select_card_5"] = MegaInput.selectCard5.ToString(),
            ["select_card_6"] = MegaInput.selectCard6.ToString(),
            ["mega_select_card_6"] = MegaInput.selectCard6.ToString(),
            ["select_card_7"] = MegaInput.selectCard7.ToString(),
            ["mega_select_card_7"] = MegaInput.selectCard7.ToString(),
            ["select_card_8"] = MegaInput.selectCard8.ToString(),
            ["mega_select_card_8"] = MegaInput.selectCard8.ToString(),
            ["select_card_9"] = MegaInput.selectCard9.ToString(),
            ["mega_select_card_9"] = MegaInput.selectCard9.ToString(),
            ["select_card_10"] = MegaInput.selectCard10.ToString(),
            ["mega_select_card_10"] = MegaInput.selectCard10.ToString()
        };

    public static IReadOnlyDictionary<string, string> GetActionAliases()
    {
        return ActionAliases;
    }

    public static string ResolveActionName(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("action is required.", nameof(action));
        }

        string trimmed = action.Trim();
        return ActionAliases.TryGetValue(trimmed, out string? mappedAction)
            ? mappedAction
            : trimmed;
    }

    public static string NormalizeMode(string? mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode) ? "tap" : mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "tap" or "press" or "release" => normalized,
            _ => throw new ArgumentException($"Unsupported input mode '{mode}'. Expected tap, press, or release.")
        };
    }

    public static Key ParseKey(string? keyName, int? numericKeycode, string fieldName)
    {
        if (numericKeycode.HasValue)
        {
            return (Key)numericKeycode.Value;
        }

        if (string.IsNullOrWhiteSpace(keyName))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }

        if (Enum.TryParse(keyName.Trim(), ignoreCase: true, out Key parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Unknown key '{keyName}'.");
    }

    public static Key? ParseOptionalKey(string? keyName, int? numericKeycode)
    {
        if (!numericKeycode.HasValue && string.IsNullOrWhiteSpace(keyName))
        {
            return null;
        }

        return ParseKey(keyName, numericKeycode, nameof(keyName));
    }
}
