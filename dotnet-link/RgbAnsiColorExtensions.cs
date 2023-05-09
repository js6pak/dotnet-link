using System.Drawing;

namespace DotNetLink;

public static class RgbAnsiColorExtensions
{
    public static string Color(this string text, Color color) => $"\u001B[38;2;{color.R};{color.G};{color.B}m" + text + "\u001B[39m";
}
