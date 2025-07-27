using Velo.Utils;

namespace Velo.Models;

/// <summary>
/// 代表一篇知識庫文章的資料模型。
/// </summary>
public class BlogPost
{
    public string? Author { get; set; }
    public List<string> Categories { get; set; } = [];
    public required string ContentHtml { get; set; }

    /// <summary>
    /// 文章的第一張圖片網址，如果沒有圖片則為空字串
    /// </summary>
    public string FirstImageUrl { get; private set; } = string.Empty;

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

            // 使用統一的檔案名稱處理方法
            var processedFileName = PathUtils.ProcessFileName(originalFileName);

            // 生成 8 碼 hash code
            var hashCode = GetFileHashCode();

            // 組合最終檔案名稱：原檔案名稱-8碼hash.html
            return $"{processedFileName}-{hashCode}.html";
        }
    }

    /// <summary>
    /// 文章中的所有圖檔路徑陣列（處理後的網頁路徑）
    /// </summary>
    public List<string> ImagePaths { get; set; } = [];

    public DateTime PublishedDate { get; set; }
    public required string Slug { get; set; }
    public string? SourceFilePath { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string Title { get; set; }

    /// <summary>
    /// 添加單一圖片路徑
    /// </summary>
    /// <param name="imagePath">圖片路徑</param>
    public void AddImagePath(string imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath) && !ImagePaths.Contains(imagePath))
        {
            ImagePaths.Add(imagePath);

            // 如果這是第一張圖片，設定為首圖
            if (ImagePaths.Count == 1)
            {
                SetFirstImageUrl(imagePath);
            }
        }
    }

    /// <summary>
    /// 清除所有圖片相關資訊
    /// </summary>
    public void ClearImages()
    {
        ImagePaths.Clear();
        FirstImageUrl = string.Empty;
    }

    /// <summary>
    /// 基於來源檔案路徑生成 8 碼 HashCode
    /// </summary>
    private string GetFileHashCode()
    {
        if (string.IsNullOrEmpty(SourceFilePath))
        {
            // 如果沒有來源檔案路徑，使用 Title 和 HtmlFileId 來生成 hash
            return PathUtils.GenerateHashCode(Title + HtmlFileId);
        }

        // 使用來源檔案路徑生成 8 位16進制 hash
        return PathUtils.GenerateHashCode(SourceFilePath);
    }

    /// <summary>
    /// 設定第一張圖片的 URL
    /// </summary>
    /// <param name="imageUrl">圖片的網頁 URL（相對路徑或絕對路徑）</param>
    public void SetFirstImageUrl(string imageUrl)
    {
        FirstImageUrl = imageUrl ?? string.Empty;
    }

    /// <summary>
    /// 更新圖片路徑列表並自動設定第一張圖片
    /// </summary>
    /// <param name="imagePaths">處理後的圖片路徑列表</param>
    public void UpdateImagePaths(IEnumerable<string> imagePaths)
    {
        ImagePaths.Clear();
        ImagePaths.AddRange(imagePaths);

        // 自動設定第一張圖片
        if (ImagePaths.Count > 0 && string.IsNullOrEmpty(FirstImageUrl))
        {
            SetFirstImageUrl(ImagePaths[0]);
        }
    }
}