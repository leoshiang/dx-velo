namespace Velo.Utils;

public static class PathUtils
{
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
            .Replace("|", "-");
}