namespace InstantAIGate.Cli.Core;

using Spectre.Console;
using System;
using System.Text.RegularExpressions;

public static partial class CliMarkup
{
    [GeneratedRegex(@"\[(?!/?(?:bold|dim|italic|underline|invert|strikethrough|black|red|green|yellow|blue|magenta|cyan|white|grey|silver|#[0-9a-fA-F]{6}|link(?:=[^\]]+)?)\b)[^\]\r\n]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex UnrecognizedMarkupTagRegex();

    public static void Line(FormattableString formattable)
    {
        var rawArguments = formattable.GetArguments();
        var escapedArguments = new object?[rawArguments.Length];

        for (int i = 0; i < rawArguments.Length; i++)
        {
            escapedArguments[i] = rawArguments[i] switch
            {
                null => string.Empty,
                _ => Markup.Escape(rawArguments[i]!.ToString() ?? string.Empty)
            };
        }

        string safeMarkup = string.Format(formattable.Format, escapedArguments);
        PrintInternal(safeMarkup, isNewLine: true);
    }

    public static void Print(FormattableString formattable)
    {
        var rawArguments = formattable.GetArguments();
        var escapedArguments = new object?[rawArguments.Length];

        for (int i = 0; i < rawArguments.Length; i++)
        {
            escapedArguments[i] = rawArguments[i] switch
            {
                null => string.Empty,
                _ => Markup.Escape(rawArguments[i]!.ToString() ?? string.Empty)
            };
        }

        string safeMarkup = string.Format(formattable.Format, escapedArguments);
        PrintInternal(safeMarkup, isNewLine: false);
    }

    public static void Line(string text)
    {
        PrintInternal(text, isNewLine: true);
    }

    public static void Print(string text)
    {
        PrintInternal(text, isNewLine: false);
    }

    public static void TextLine(string plainText)
    {
        AnsiConsole.WriteLine(plainText);
    }

    public static void Text(string plainText)
    {
        AnsiConsole.Write(plainText);
    }

    private static void PrintInternal(string markup, bool isNewLine)
    {
        try
        {
            if (isNewLine)
            {
                AnsiConsole.MarkupLine(markup);
            }
            else
            {
                AnsiConsole.Markup(markup);
            }
        }
        catch (Exception)
        {
            string sanitized = SanitizeMalformedMarkup(markup);
            try
            {
                if (isNewLine)
                {
                    AnsiConsole.MarkupLine(sanitized);
                }
                else
                {
                    AnsiConsole.Markup(sanitized);
                }
            }
            catch (Exception)
            {
                string stripped = Markup.Remove(markup);
                if (isNewLine)
                {
                    AnsiConsole.WriteLine(stripped);
                }
                else
                {
                    AnsiConsole.Write(stripped);
                }
            }
        }
    }

    private static string SanitizeMalformedMarkup(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return UnrecognizedMarkupTagRegex().Replace(input, match =>
        {
            string value = match.Value;
            return $"[{value[..^1]}]]";
        });
    }
}