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
            // 統一放在根目錄，避免相對路徑圖片問題
            return $"{Slug}.html";
        }
    }

    public DateTime PublishedDate { get; set; }
    public required string Slug { get; set; }
    public string? SourceFilePath { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string Title { get; set; }

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