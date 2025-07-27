namespace Velo.Utils;

public static class PathUtils
{
    /// <summary>
    /// 基於輸入字串生成 8 碼 HashCode
    /// </summary>
    public static string GenerateHashCode(string input)
    {
        return string.IsNullOrEmpty(input) ? "00000000" : Math.Abs(input.GetHashCode()).ToString("x8");
    }

    /// <summary>
    /// 處理檔案名稱：空白轉 -，英文小寫，並清理特殊字符
    /// </summary>
    public static string ProcessFileName(string fileName)
    {
        return string.IsNullOrEmpty(fileName) ? "untitled" : Sanitize(fileName.ToLower());
    }

    /// <summary>
    /// 將檔名或資料夾名稱中的無效字元轉成 '-'
    /// </summary>
    private static string Sanitize(string path) =>
        path.Replace(" ", "-")
            .Replace("　", "-") // 全形空白
            .Replace("/", "-")
            .Replace("\\", "-")
            .Replace(":", "-")
            .Replace("*", "-")
            .Replace("?", "-")
            .Replace("\"", "-")
            .Replace("<", "-")
            .Replace(">", "-")
            .Replace("|", "-")
            .Replace(".", "-")
            .Replace(",", "-")
            .Replace(";", "-")
            .Replace("!", "-")
            .Replace("@", "-")
            .Replace("#", "-")
            .Replace("$", "-")
            .Replace("%", "-")
            .Replace("^", "-")
            .Replace("&", "-")
            .Replace("(", "-")
            .Replace(")", "-")
            .Replace("+", "-")
            .Replace("=", "-")
            .Replace("[", "-")
            .Replace("]", "-")
            .Replace("{", "-")
            .Replace("}", "-")
            .Replace("~", "-")
            .Replace("`", "-");
}