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
    /// 根據分類和 slug 生成 HTML 檔案路徑
    /// </summary>
    public string HtmlFilePath
    {
        get
        {
            // 使用檔案路徑的 HashCode 確保唯一性
            var hashCode = GetFileHashCode();
            return $"{hashCode}-{Slug}.html";
        }
    }

    public DateTime PublishedDate { get; set; }
    public required string Slug { get; set; }
    public string? SourceFilePath { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string Title { get; set; }

    /// <summary>
    /// 基於來源檔案路徑生成 HashCode
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
        return path.Replace(" ", "-")
            .Replace("　", "-") // 全型空格
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
}