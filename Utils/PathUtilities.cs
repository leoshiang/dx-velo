namespace Velo.Utils;

public static class PathUtils
{
    /// <summary>
    /// 基於輸入字串生成 8 碼 HashCode
    /// </summary>
    public static string GenerateHashCode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "00000000";

        return Math.Abs(input.GetHashCode()).ToString("x8");
    }

    /// <summary>
    /// 處理檔案名稱：空白轉 -，英文小寫，並清理特殊字符
    /// </summary>
    public static string ProcessFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "untitled";

        // 轉換為小寫並清理路徑
        return Sanitize(fileName.ToLower());
    }

    /// <summary>
    /// 將檔名或資料夾名稱中的無效字元轉成 '-'
    /// </summary>
    public static string Sanitize(string path) =>
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