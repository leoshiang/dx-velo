namespace Velo.Models;

/// <summary>
/// 代表一篇知識庫文章的資料模型。
/// </summary>
public class BlogPost
{
    public string? Author { get; set; }
    public List<string> Categories { get; set; } = [];
    public required string ContentHtml { get; set; }

    public string HtmlFileId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 根據原檔案名稱生成 HTML 檔案路徑（不建目錄）
    /// </summary>
    public string HtmlFilePath
    {
        get
        {
            // 取得原檔案名稱（不含副檔名）
            var originalFileName = string.IsNullOrEmpty(SourceFilePath)
                ? Slug
                : Path.GetFileNameWithoutExtension(SourceFilePath);

            // 轉換檔案名稱：空白轉 -，英文小寫
            var processedFileName = ProcessFileName(originalFileName);

            // 生成 8 碼 hash code
            var hashCode = GetFileHashCode();

            // 組合最終檔案名稱：原檔案名稱-8碼hash.html
            return $"{processedFileName}-{hashCode}.html";
        }
    }

    public DateTime PublishedDate { get; set; }
    public required string Slug { get; set; }
    public string? SourceFilePath { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string Title { get; set; }

    /// <summary>
    /// 處理檔案名稱：空白轉 -，英文小寫
    /// </summary>
    private static string ProcessFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "untitled";

        // 轉換為小寫
        var processed = fileName.ToLower();

        // 空白轉換成 -
        processed = processed.Replace(" ", "-");

        // 全型空格也轉換成 -
        processed = processed.Replace("　", "-");

        // 移除或替換其他不適合的字符
        processed = SanitizePath(processed);

        return processed;
    }

    /// <summary>
    /// 基於來源檔案路徑生成 8 碼 HashCode
    /// </summary>
    private string GetFileHashCode()
    {
        if (string.IsNullOrEmpty(SourceFilePath))
        {
            // 如果沒有來源檔案路徑，使用 Title 和 HtmlFileId 來生成 hash
            return Math.Abs((Title + HtmlFileId).GetHashCode()).ToString("x8");
        }

        // 使用來源檔案路徑生成 8 位16進制 hash
        return Math.Abs(SourceFilePath.GetHashCode()).ToString("x8");
    }

    /// <summary>
    /// 清理路徑中的特殊字符
    /// </summary>
    private static string SanitizePath(string path)
    {
        return path.Replace("/", "-")
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
}