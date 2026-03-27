using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AiEvent;

public static class AiEventMarkup
{
    private static readonly Regex TagRegex = new(@"\[(?<closing>/)?(?<body>[^\[\]]+)\]", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "gold",
        "red",
        "green",
        "blue",
        "purple",
        "orange",
        "aqua",
        "pink",
        "sine",
        "jitter",
        "rainbow",
        "thinky_dots",
        "b",
        "i",
        "center",
        "shake",
        "font_size",
        "{color}",
    };

    private static readonly HashSet<string> TagsAllowingArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "font_size",
        "rainbow",
    };

    public static bool TryValidateText(string? text, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        Stack<string> stack = new();
        foreach (Match match in TagRegex.Matches(text))
        {
            string fullTag = match.Value;
            bool isClosing = match.Groups["closing"].Success;
            string body = match.Groups["body"].Value.Trim();
            string tagName = GetTagName(body);

            if (!AllowedTagNames.Contains(tagName))
            {
                error = $"Unsupported markup tag `{fullTag}`. Use only vanilla STS2 tags.";
                return false;
            }

            if (!IsTagFormAllowed(tagName, body, isClosing))
            {
                error = $"Unsupported markup syntax `{fullTag}`. Use the vanilla STS2 tag form only.";
                return false;
            }

            if (isClosing)
            {
                if (stack.Count == 0 || !string.Equals(stack.Peek(), tagName, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Markup tag `{fullTag}` is mismatched or closes in the wrong order.";
                    return false;
                }

                stack.Pop();
                continue;
            }

            stack.Push(tagName);
        }

        if (stack.Count > 0)
        {
            error = $"Markup tag `[/{stack.Peek()}]` is missing.";
            return false;
        }

        return true;
    }

    public static string SanitizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        StringBuilder builder = new();
        int lastIndex = 0;
        Stack<string> stack = new();

        foreach (Match match in TagRegex.Matches(text))
        {
            builder.Append(text, lastIndex, match.Index - lastIndex);

            string body = match.Groups["body"].Value.Trim();
            bool isClosing = match.Groups["closing"].Success;
            string tagName = GetTagName(body);

            if (AllowedTagNames.Contains(tagName) && IsTagFormAllowed(tagName, body, isClosing))
            {
                if (isClosing)
                {
                    if (stack.Count > 0 && string.Equals(stack.Peek(), tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        stack.Pop();
                        builder.Append(match.Value);
                    }
                }
                else
                {
                    stack.Push(tagName);
                    builder.Append(match.Value);
                }
            }

            lastIndex = match.Index + match.Length;
        }

        builder.Append(text, lastIndex, text.Length - lastIndex);
        return builder.ToString();
    }

    private static string GetTagName(string body)
    {
        int separatorIndex = body.IndexOfAny(new[] { ' ', '=' });
        return separatorIndex >= 0 ? body[..separatorIndex] : body;
    }

    private static bool IsTagFormAllowed(string tagName, string body, bool isClosing)
    {
        if (isClosing)
        {
            return string.Equals(tagName, body, StringComparison.OrdinalIgnoreCase);
        }

        bool hasArguments = !string.Equals(tagName, body, StringComparison.OrdinalIgnoreCase);
        return !hasArguments || TagsAllowingArguments.Contains(tagName);
    }
}
