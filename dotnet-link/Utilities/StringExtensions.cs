// SPDX-License-Identifier: MIT
// SPDX-FileCopyrightText: 2022 js6pak

namespace DotNetLink.Utilities;

internal static class StringExtensions
{
    public static string TrimStart(this string text, string value)
    {
        return text.StartsWith(value) ? text[(value.Length + 1)..] : text;
    }

    public static string TrimCurrentDirectory(this string text)
    {
        return text.TrimStart(Directory.GetCurrentDirectory());
    }
}
