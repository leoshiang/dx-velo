using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;
using Velo.Services.Abstractions;

namespace Velo.Services;

/// <summary>
/// Markdown 到 HTML 轉換器服務
/// 負責將 Markdown 格式的部落格文章轉換為 HTML，並處理圖片資源的管理和複製
/// </summary>
/// <param name="blogService">部落格服務，用於獲取文章資料</param>
/// <param name="templateService">模板服務，用於套用 HTML 模板</param>
/// <param name="fileService">檔案服務，用於檔案讀寫操作</param>
/// <param name="configuration">應用程式設定</param>
/// <param name="logger">日誌記錄器</param>
public class MarkdownToHtmlConverter(
    IBlogService blogService,
    ITemplateService templateService,
    IFileService fileService,
    IConfiguration configuration,
    ILogger<MarkdownToHtmlConverter> logger)
    : IMarkdownToHtmlService
{
    #region 私有字段

    /// <summary>
    /// 圖片映射字典
    /// Key: 原始圖片的絕對路徑
    /// Value: 輸出目錄中的檔案名稱（不含路徑）
    /// 用於追蹤需要複製的圖片檔案
    /// </summary>
    private readonly Dictionary<string, string> _imageMapping = new();

    #endregion

    #region 私有方法 - HTML 清理

    /// <summary>
    /// 清理 HTML 中的 file:// 協定路徑
    /// 將本地檔案協定路徑轉換為適合網頁的相對路徑
    /// </summary>
    /// <param name="html">要清理的 HTML 內容</param>
    /// <returns>清理後的 HTML 內容</returns>
    private string CleanFileProtocolPaths(string html)
    {
        // 定義需要處理的 HTML 屬性模式
        // 每個元組包含：(正規表達式模式, 替換格式)
        var patterns = new[]
        {
            // 處理 src 屬性中的 file:// 路徑
            (@"src=[""']file:///([^""']*)[""']", "src=\"images/{0}\""),

            // 處理 data-src 屬性中的 file:// 路徑（用於延遲載入）
            (@"data-src=[""']file:///([^""']*)[""']", "data-src=\"images/{0}\""),

            // 處理任何其他屬性中的 file:// 路徑
            (@"=[""']file:///([^""']*)[""']", "=\"images/{0}\"")
        };

        var cleanedHtml = html;

        // 逐一處理每種模式
        foreach (var (pattern, replacement) in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            cleanedHtml = regex.Replace(cleanedHtml, match =>
            {
                var encodedPath = match.Groups[1].Value;
                string fileName;

                try
                {
                    // URL 解碼路徑（處理中文和特殊字元）
                    var decodedPath = Uri.UnescapeDataString(encodedPath);
                    fileName = Path.GetFileName(decodedPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "URL 解碼失敗: {Path}", encodedPath);
                    // 解碼失敗時直接使用原始路徑
                    fileName = Path.GetFileName(encodedPath);
                }

                logger.LogWarning("清理 file:// 路徑: {OriginalPath} -> images/{FileName}",
                    match.Value, fileName);

                return string.Format(replacement, fileName);
            });
        }

        // 額外處理：確保所有本地圖片路徑都有 images/ 前綴
        var localImageRegex = new Regex(@"src=[""']([^""']+\.(?:jpg|jpeg|png|gif|webp|svg))[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        cleanedHtml = localImageRegex.Replace(cleanedHtml, match =>
        {
            var imagePath = match.Groups[1].Value;

            // 跳過網路圖片或已經有正確前綴的圖片
            if (imagePath.StartsWith("http") || imagePath.StartsWith("images/") || imagePath.StartsWith("/"))
            {
                return match.Value;
            }

            // 為本地圖片添加 images/ 前綴
            return $"src=\"images/{imagePath}\"";
        });

        return cleanedHtml;
    }

    #endregion

    #region 私有方法 - 文章處理

    /// <summary>
    /// 轉換並儲存單一文章
    /// 執行完整的文章處理流程：轉換、清理、模板套用、儲存
    /// </summary>
    /// <param name="post">要處理的文章物件</param>
    private async Task ConvertAndSavePostAsync(BlogPost post)
    {
        try
        {
            logger.LogDebug("開始處理文章: {Title}", post.Title);

            // 步驟 1: 清空文章的圖片路徑記錄
            post.ClearImages();

            // 步驟 2: 將 Markdown 轉換為 HTML
            var htmlContent = await ConvertToHtmlAsync(post.ContentHtml, post.SourceFilePath);

            // 步驟 3: 清理 HTML 中的 file:// 路徑
            htmlContent = CleanFileProtocolPaths(htmlContent);

            // 步驟 4: 從 HTML 中提取圖片路徑並記錄到文章物件
            ExtractImagePathsFromHtml(post, htmlContent);

            // 步驟 5: 套用 HTML 模板
            var finalHtml = await templateService.RenderPostAsync(post, htmlContent);

            // 步驟 6: 多層清理，確保模板渲染後也沒有 file:// 路徑
            for (var i = 0; i < 3; i++) // 最多執行 3 次清理
            {
                var beforeClean = finalHtml;
                finalHtml = CleanFileProtocolPaths(finalHtml);

                // 如果內容沒有變化，表示清理完成
                if (beforeClean == finalHtml) break;
            }

            // 步驟 7: 儲存最終的 HTML 檔案
            var outputPath = Path.Combine(configuration["BlogSettings:HtmlOutputPath"]!, post.HtmlFilePath);
            await fileService.WriteAllTextAsync(outputPath, finalHtml);

            logger.LogDebug("已轉換: {Title}，圖片數量: {ImageCount}", post.Title, post.ImagePaths.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換文章失敗: {Title}", post.Title);
            throw;
        }
    }

    #endregion

    #region 私有方法 - 首頁生成

    /// <summary>
    /// 生成部落格首頁
    /// 整合所有文章資訊和分類樹，生成完整的首頁 HTML
    /// </summary>
    /// <param name="posts">所有文章清單</param>
    /// <param name="categoryTree">分類樹結構</param>
    private async Task GenerateIndexPageAsync(List<BlogPost> posts, CategoryNode categoryTree)
    {
        logger.LogInformation("開始生成首頁...");

        // 使用模板服務渲染首頁
        var indexHtml = await templateService.RenderIndexAsync(posts, categoryTree);

        // 多層清理首頁中可能存在的 file:// 路徑
        for (var i = 0; i < 3; i++)
        {
            var beforeClean = indexHtml;
            indexHtml = CleanFileProtocolPaths(indexHtml);

            // 如果沒有變化，表示清理完成
            if (beforeClean == indexHtml) break;
        }

        // 儲存首頁檔案
        var outputPath = Path.Combine(configuration["BlogSettings:HtmlOutputPath"]!, "index.html");
        await fileService.WriteAllTextAsync(outputPath, indexHtml);

        logger.LogInformation("首頁生成完成");
    }

    #endregion

    #region 靜態常數和設定

    /// <summary>
    /// 用於匹配 Markdown 中圖片語法的正規表達式
    /// 匹配格式：![alt text](image_path)
    /// </summary>
    private static readonly Regex ImageRegex = new(@"!\[.*?\]\((.*?)\)", RegexOptions.Compiled);

    /// <summary>
    /// Markdig 處理管線設定
    /// 包含所有常用的 Markdown 擴展功能
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions() // 高級擴展功能
        .UseEmojiAndSmiley() // 表情符號支援
        .UseGenericAttributes() // 通用屬性語法
        .UsePipeTables() // 表格支援
        .UseTaskLists() // 任務清單支援
        .UseAutoLinks() // 自動連結轉換
        .UseBootstrap() // Bootstrap CSS 類別
        .UseMediaLinks() // 媒體連結支援
        .Build();

    #endregion

    #region 公開方法

    /// <summary>
    /// 轉換並儲存所有文章
    /// 這是主要的批次處理方法，執行完整的部落格生成流程
    /// </summary>
    /// <returns>非同步任務</returns>
    public async Task ConvertAndSaveAllPostsAsync()
    {
        logger.LogInformation("開始轉換所有文章...");

        // 清空圖片映射表，準備新的轉換作業
        _imageMapping.Clear();

        // 取得所有文章和分類樹結構
        var posts = (await blogService.GetAllPostsAsync()).ToList();
        var categoryTree = await blogService.GetCategoryTreeAsync();

        logger.LogInformation("找到 {Count} 篇文章", posts.Count);

        // 從設定檔讀取輸出路徑
        var outputPath = configuration["BlogSettings:HtmlOutputPath"]!;
        var imageOutputPath = configuration["BlogSettings:ImageOutputPath"]!;

        // 確保輸出目錄存在
        fileService.EnsureDirectoryExists(outputPath);
        fileService.EnsureDirectoryExists(imageOutputPath);

        // 並行轉換所有文章
        var conversionTasks = posts.Select(ConvertAndSavePostAsync).ToArray();
        await Task.WhenAll(conversionTasks);

        // 複製所有收集到的圖片檔案
        await CopyImagesAsync();

        // 生成網站首頁
        await GenerateIndexPageAsync(posts, categoryTree);

        logger.LogInformation("轉換完成，文章 {Count} 篇，圖片 {ImageCount} 張",
            posts.Count, _imageMapping.Count);
    }

    /// <summary>
    /// 將 Markdown 文字轉換為 HTML
    /// 這是核心的轉換方法，處理 Markdown 語法並輸出 HTML
    /// </summary>
    /// <param name="markdown">要轉換的 Markdown 內容</param>
    /// <param name="sourceFilePath">來源檔案路徑，用於處理相對圖片路徑</param>
    /// <returns>轉換後的 HTML 內容</returns>
    public Task<string> ConvertToHtmlAsync(string markdown, string? sourceFilePath = null)
    {
        try
        {
            logger.LogDebug("開始轉換 Markdown，來源檔案: {SourceFile}", sourceFilePath);

            // 步驟 1: 預處理圖片路徑
            // 將 Markdown 中的圖片路徑轉換為適合網頁的格式
            var processedMarkdown = ProcessImagePaths(markdown, sourceFilePath);

            logger.LogDebug("處理後的 Markdown 內容預覽: {Preview}",
                processedMarkdown.Length > 200 ? processedMarkdown.Substring(0, 200) + "..." : processedMarkdown);

            // 步驟 2: 使用 Markdig 將 Markdown 轉換為 HTML
            var html = Markdown.ToHtml(processedMarkdown, Pipeline);

            logger.LogDebug("轉換完成的 HTML 內容預覽: {Preview}",
                html.Length > 200 ? string.Concat(html.AsSpan(0, 200), "...") : html);

            return Task.FromResult(html);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Markdown 轉 HTML 失敗: {FilePath}", sourceFilePath);
            throw;
        }
    }

    #endregion

    #region 私有方法 - 圖片處理

    /// <summary>
    /// 複製所有收集到的圖片檔案到輸出目錄
    /// 並行處理提高效能
    /// </summary>
    private async Task CopyImagesAsync()
    {
        logger.LogInformation("開始複製圖片...");

        var imageOutputPath = configuration["BlogSettings:ImageOutputPath"]!;
        fileService.EnsureDirectoryExists(imageOutputPath);

        // 建立並行複製任務
        var copyTasks = _imageMapping.Select(async kvp =>
        {
            var sourcePath = kvp.Key; // 原始檔案路徑
            var fileName = kvp.Value; // 目標檔案名稱
            var destinationPath = Path.Combine(imageOutputPath, fileName);

            try
            {
                await fileService.CopyFileAsync(sourcePath, destinationPath);
                logger.LogDebug("圖片複製成功: {Source} -> {Destination}", sourcePath, destinationPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "圖片複製失敗: {Source} -> {Destination}", sourcePath, destinationPath);
            }
        });

        // 等待所有複製任務完成
        await Task.WhenAll(copyTasks);
        logger.LogInformation("圖片複製完成");
    }

    /// <summary>
    /// 從 HTML 內容中提取圖片路徑並記錄到文章物件
    /// 同時更新圖片的網頁路徑格式
    /// </summary>
    /// <param name="post">文章物件</param>
    /// <param name="htmlContent">HTML 內容</param>
    private void ExtractImagePathsFromHtml(BlogPost post, string htmlContent)
    {
        // 用於匹配 HTML img 標籤的正規表達式
        var imgPattern = @"<img[^>]+src=[""']([^""']+)[""'][^>]*>";
        var regex = new Regex(imgPattern, RegexOptions.IgnoreCase);
        var matches = regex.Matches(htmlContent);

        // 清空之前的圖片路徑記錄
        post.ClearImages();

        foreach (Match match in matches)
        {
            var imageSrc = match.Groups[1].Value;

            // 跳過 file:// 路徑（理論上已經被清理，但保留檢查）
            if (imageSrc.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("跳過 file:// 路徑: {ImagePath}", imageSrc);
                continue;
            }

            // 處理網路圖片（http/https）
            if (imageSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                post.AddImagePath(imageSrc);
            }
            // 處理本地圖片
            else if (!Path.IsPathFullyQualified(imageSrc))
            {
                // 如果路徑已經包含 images/ 前綴，直接使用
                if (imageSrc.StartsWith("images/"))
                {
                    post.AddImagePath(imageSrc);
                }
                // 否則添加 images/ 前綴
                else
                {
                    var webImagePath = $"images/{imageSrc}";
                    post.AddImagePath(webImagePath);
                }
            }
            else
            {
                // 忽略完整路徑（絕對路徑）
                logger.LogWarning("忽略完整路徑: {ImagePath}", imageSrc);
            }
        }

        logger.LogDebug("文章 {Title} 找到 {Count} 張有效圖片", post.Title, post.ImagePaths.Count);
    }

    /// <summary>
    /// 處理 Markdown 中的圖片路徑
    /// 將各種格式的圖片路徑轉換為統一的網頁格式
    /// </summary>
    /// <param name="markdown">原始 Markdown 內容</param>
    /// <param name="sourceFilePath">來源檔案路徑</param>
    /// <returns>處理後的 Markdown 內容</returns>
    private string ProcessImagePaths(string markdown, string? sourceFilePath)
    {
        return ImageRegex.Replace(markdown, match =>
        {
            var originalSrc = match.Groups[1].Value;
            var processedSrc = ProcessSingleImagePath(originalSrc, sourceFilePath);
            return match.Value.Replace(originalSrc, processedSrc);
        });
    }

    /// <summary>
    /// 處理單一圖片路徑
    /// 這是圖片路徑處理的核心邏輯
    /// </summary>
    /// <param name="imageSrc">原始圖片路徑</param>
    /// <param name="sourceFilePath">來源檔案路徑</param>
    /// <returns>處理後的圖片路徑</returns>
    private string ProcessSingleImagePath(string imageSrc, string? sourceFilePath)
    {
        try
        {
            logger.LogDebug("處理圖片路徑: {ImageSrc}", imageSrc);

            // 跳過網路圖片
            if (imageSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return imageSrc;
            }

            var originalSrc = imageSrc;

            // 處理 file:// 協定的路徑
            if (imageSrc.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                imageSrc = imageSrc.Substring(7); // 移除 "file://" 前綴
                logger.LogDebug("移除 file:// 前綴: {Path}", imageSrc);
            }

            // URL 解碼（處理中文路徑和特殊字元）
            try
            {
                imageSrc = Uri.UnescapeDataString(imageSrc);
                logger.LogDebug("URL 解碼後: {Path}", imageSrc);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "URL 解碼失敗，使用原始路徑: {Path}", imageSrc);
            }

            // 確定圖片的絕對路徑
            string absoluteImagePath;
            if (Path.IsPathFullyQualified(imageSrc))
            {
                // 已經是絕對路徑
                absoluteImagePath = Path.GetFullPath(imageSrc);
            }
            else
            {
                // 相對路徑，需要結合源文件目錄
                var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                    ? configuration["BlogContentPath"]!
                    : Path.GetDirectoryName(sourceFilePath)!;

                absoluteImagePath = Path.GetFullPath(Path.Combine(sourceDir, imageSrc));
            }

            logger.LogDebug("檢查圖片檔案存在性: {Path}", absoluteImagePath);

            // 檢查檔案是否存在
            if (File.Exists(absoluteImagePath))
            {
                var fileName = Path.GetFileName(absoluteImagePath);

                // 處理重複檔名（生成唯一檔名）
                var uniqueFileName = GetUniqueFileName(fileName);

                // 返回網頁格式的路徑
                var newImagePath = $"images/{uniqueFileName}";

                // 記錄到映射表中，用於後續檔案複製
                _imageMapping[absoluteImagePath] = uniqueFileName;

                logger.LogInformation("圖片路徑映射成功: {Original} -> {New}", originalSrc, newImagePath);
                return newImagePath;
            }

            logger.LogWarning("圖片檔案不存在: {ImagePath}", absoluteImagePath);

            // 嘗試在其他可能的位置尋找圖片
            var alternativePaths = GetAlternativeImagePaths(imageSrc);
            foreach (var altPath in alternativePaths)
            {
                logger.LogDebug("嘗試替代路徑: {Path}", altPath);
                if (File.Exists(altPath))
                {
                    var fileName = Path.GetFileName(altPath);
                    var uniqueFileName = GetUniqueFileName(fileName);
                    var newImagePath = $"images/{uniqueFileName}";

                    _imageMapping[altPath] = uniqueFileName;

                    logger.LogInformation("在替代路徑找到圖片: {Original} -> {New}", altPath, newImagePath);
                    return newImagePath;
                }
            }

            // 如果找不到圖片，返回一個表示缺失的路徑
            var missingFileName = Path.GetFileName(imageSrc);
            return $"images/missing-{missingFileName}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "處理圖片路徑失敗: {ImageSrc}", imageSrc);
            return imageSrc;
        }
    }

    /// <summary>
    /// 生成唯一的檔案名稱
    /// 防止檔名衝突，當有重複檔名時自動加上數字後綴
    /// </summary>
    /// <param name="fileName">原始檔案名稱</param>
    /// <returns>唯一的檔案名稱</returns>
    private string GetUniqueFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var uniqueName = fileName;
        var counter = 1;

        // 檢查是否已經有相同的檔名映射
        while (_imageMapping.Values.Any(v => v.EndsWith(uniqueName)))
        {
            uniqueName = $"{name}_{counter}{extension}";
            counter++;
        }

        return uniqueName;
    }

    /// <summary>
    /// 取得圖片的替代路徑
    /// 當原始路徑找不到圖片時，嘗試在其他可能的位置尋找
    /// 這對於處理目錄結構變更或不同的部落格配置很有用
    /// </summary>
    /// <param name="imagePath">原始圖片路徑</param>
    /// <returns>可能的替代路徑清單</returns>
    private List<string> GetAlternativeImagePaths(string imagePath)
    {
        var alternatives = new List<string>();
        var blogContentPath = configuration["BlogContentPath"]!;

        // 處理絕對路徑的情況
        if (Path.IsPathFullyQualified(imagePath))
        {
            // 嘗試將路徑中的特定目錄名稱進行替換
            // 這些替換規則是基於常見的目錄命名慣例
            var modifiedPath1 = imagePath.Replace("\\部落格\\", "\\攝影部落格\\");
            var modifiedPath2 = imagePath.Replace("/部落格/", "/攝影部落格/");

            if (modifiedPath1 != imagePath) alternatives.Add(modifiedPath1);
            if (modifiedPath2 != imagePath) alternatives.Add(modifiedPath2);

            // 嘗試相反的替換
            var modifiedPath3 = imagePath.Replace("\\攝影部落格\\", "\\部落格\\");
            var modifiedPath4 = imagePath.Replace("/攝影部落格/", "/部落格/");

            if (modifiedPath3 != imagePath) alternatives.Add(modifiedPath3);
            if (modifiedPath4 != imagePath) alternatives.Add(modifiedPath4);
        }
        else
        {
            // 處理相對路徑的情況
            // 在 BlogContentPath 的所有子目錄中尋找同名檔案
            var fileName = Path.GetFileName(imagePath);
            var directories = Directory.GetDirectories(blogContentPath, "*", SearchOption.AllDirectories);

            foreach (var dir in directories)
            {
                var possiblePath = Path.Combine(dir, fileName);
                alternatives.Add(possiblePath);

                // 也嘗試在 .assets 資源目錄中尋找
                // 這是 Typora 等編輯器常用的資源目錄命名慣例
                var assetsDir = Path.Combine(dir,
                    Path.GetFileNameWithoutExtension(Directory.GetParent(dir)?.Name ?? "") + ".assets");
                if (Directory.Exists(assetsDir))
                {
                    alternatives.Add(Path.Combine(assetsDir, fileName));
                }
            }
        }

        // 移除重複的路徑並返回
        return alternatives.Distinct().ToList();
    }

    #endregion
}