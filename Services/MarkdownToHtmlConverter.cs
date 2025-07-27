using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;

namespace Velo.Services;

public class MarkdownToHtmlConverter(
    IBlogService blogService,
    ITemplateService templateService,
    IFileService fileService,
    IConfiguration configuration,
    ILogger<MarkdownToHtmlConverter> logger)
    : IMarkdownToHtmlService
{
    private static readonly Regex ImageRegex = new(@"!\[.*?\]\((.*?)\)", RegexOptions.Compiled);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseGenericAttributes()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseBootstrap()
        .UseMediaLinks()
        .Build();

    private readonly IBlogService _blogService = blogService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IFileService _fileService = fileService;

    private readonly Dictionary<string, string> _imageMapping = new();
    private readonly ILogger<MarkdownToHtmlConverter> _logger = logger;
    private readonly ITemplateService _templateService = templateService;

    public async Task ConvertAndSaveAllPostsAsync()
    {
        _logger.LogInformation("開始轉換所有文章...");

        _imageMapping.Clear();

        var posts = (await _blogService.GetAllPostsAsync()).ToList();
        var categoryTree = await _blogService.GetCategoryTreeAsync();

        _logger.LogInformation("找到 {Count} 篇文章", posts.Count);

        var outputPath = _configuration["BlogSettings:HtmlOutputPath"]!;
        var imageOutputPath = _configuration["BlogSettings:ImageOutputPath"]!;

        _fileService.EnsureDirectoryExists(outputPath);
        _fileService.EnsureDirectoryExists(imageOutputPath);

        // 轉換文章
        var conversionTasks = posts.Select(ConvertAndSavePostAsync).ToArray();
        await Task.WhenAll(conversionTasks);

        // 複製圖片
        await CopyImagesAsync();

        // 生成首頁
        await GenerateIndexPageAsync(posts, categoryTree);

        _logger.LogInformation("轉換完成，文章 {Count} 篇，圖片 {ImageCount} 張",
            posts.Count, _imageMapping.Count);
    }

    public Task<string> ConvertToHtmlAsync(string markdown, string? sourceFilePath = null)
    {
        try
        {
            _logger.LogDebug("開始轉換 Markdown，來源檔案: {SourceFile}", sourceFilePath);

            // 先處理圖片路徑
            var processedMarkdown = ProcessImagePaths(markdown, sourceFilePath);

            _logger.LogDebug("處理後的 Markdown 內容預覽: {Preview}",
                processedMarkdown.Length > 200 ? processedMarkdown.Substring(0, 200) + "..." : processedMarkdown);

            // 轉換為 HTML
            var html = Markdown.ToHtml(processedMarkdown, Pipeline);

            _logger.LogDebug("轉換完成的 HTML 內容預覽: {Preview}",
                html.Length > 200 ? html.Substring(0, 200) + "..." : html);

            return Task.FromResult(html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Markdown 轉 HTML 失敗: {FilePath}", sourceFilePath);
            throw;
        }
    }

    private string CleanFileProtocolPaths(string html)
    {
        // 更全面的清理 file:// 路徑
        var patterns = new[]
        {
            // 處理 src 屬性中的 file:// 路徑
            (@"src=[""']file:///([^""']*)[""']", "src=\"images/{0}\""),

            // 處理 data-src 屬性中的 file:// 路徑  
            (@"data-src=[""']file:///([^""']*)[""']", "data-src=\"images/{0}\""),

            // 處理任何屬性中的 file:// 路徑
            (@"=[""']file:///([^""']*)[""']", "=\"images/{0}\"")
        };

        var cleanedHtml = html;

        foreach (var (pattern, replacement) in patterns)
        {
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            cleanedHtml = regex.Replace(cleanedHtml, match =>
            {
                var encodedPath = match.Groups[1].Value;
                string fileName;

                try
                {
                    // URL 解碼路徑
                    var decodedPath = Uri.UnescapeDataString(encodedPath);
                    fileName = Path.GetFileName(decodedPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "URL 解碼失敗: {Path}", encodedPath);
                    fileName = Path.GetFileName(encodedPath);
                }

                _logger.LogWarning("清理 file:// 路徑: {OriginalPath} -> images/{FileName}",
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

            // 如果是網路圖片或已經有 images/ 前綴，跳過
            if (imagePath.StartsWith("http") || imagePath.StartsWith("images/") || imagePath.StartsWith("/"))
            {
                return match.Value;
            }

            // 為本地圖片添加 images/ 前綴
            return $"src=\"images/{imagePath}\"";
        });

        return cleanedHtml;
    }

    private async Task ConvertAndSavePostAsync(BlogPost post)
    {
        try
        {
            _logger.LogDebug("開始處理文章: {Title}", post.Title);

            // 清空之前的圖片路徑
            post.ClearImages();

            // 轉換 Markdown 為 HTML
            var htmlContent = await ConvertToHtmlAsync(post.ContentHtml, post.SourceFilePath);

            // 多次清理 - 確保徹底清除 file:// 路徑
            htmlContent = CleanFileProtocolPaths(htmlContent);

            // 提取圖片路徑
            ExtractImagePathsFromHtml(post, htmlContent);

            // 使用模板渲染
            var finalHtml = await _templateService.RenderPostAsync(post, htmlContent);

            // 多層清理 - 處理模板可能生成的 file:// 路徑
            for (int i = 0; i < 3; i++) // 執行 3 次清理
            {
                var beforeClean = finalHtml;
                finalHtml = CleanFileProtocolPaths(finalHtml);

                // 如果沒有變化，跳出循環
                if (beforeClean == finalHtml) break;
            }

            // 儲存 HTML 檔案
            var outputPath = Path.Combine(_configuration["BlogSettings:HtmlOutputPath"]!, post.HtmlFilePath);
            await _fileService.WriteFileAsync(outputPath, finalHtml);

            _logger.LogDebug("已轉換: {Title}，圖片數量: {ImageCount}", post.Title, post.ImagePaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "轉換文章失敗: {Title}", post.Title);
            throw;
        }
    }

    private async Task CopyImagesAsync()
    {
        _logger.LogInformation("開始複製圖片...");

        var imageOutputPath = _configuration["BlogSettings:ImageOutputPath"]!;
        _fileService.EnsureDirectoryExists(imageOutputPath);

        var copyTasks = _imageMapping.Select(async kvp =>
        {
            var sourcePath = kvp.Key;
            var fileName = kvp.Value; // 現在只是檔名，不包含路徑
            var destinationPath = Path.Combine(imageOutputPath, fileName);

            try
            {
                await _fileService.CopyFileAsync(sourcePath, destinationPath);
                _logger.LogDebug("圖片複製成功: {Source} -> {Destination}", sourcePath, destinationPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "圖片複製失敗: {Source} -> {Destination}", sourcePath, destinationPath);
            }
        });

        await Task.WhenAll(copyTasks);
        _logger.LogInformation("圖片複製完成");
    }

    private void ExtractImagePathsFromHtml(BlogPost post, string htmlContent)
    {
        var imgPattern = @"<img[^>]+src=[""']([^""']+)[""'][^>]*>";
        var regex = new Regex(imgPattern, RegexOptions.IgnoreCase);
        var matches = regex.Matches(htmlContent);

        // 清空之前的圖片路徑
        post.ClearImages();

        foreach (Match match in matches)
        {
            var imageSrc = match.Groups[1].Value;

            // 跳過 file:// 路徑（已經被清理掉了，但保留檢查）
            if (imageSrc.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("跳過 file:// 路徑: {ImagePath}", imageSrc);
                continue;
            }

            // 處理網路圖片
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
                _logger.LogWarning("忽略完整路徑: {ImagePath}", imageSrc);
            }
        }

        _logger.LogDebug("文章 {Title} 找到 {Count} 張有效圖片", post.Title, post.ImagePaths.Count);
    }

    private string ForceCleanAllFilePaths(string html)
    {
        // 強制清理所有可能的 file:// 路徑格式
        var allFilePathRegex = new Regex(
            @"file:///[^""'\s>]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        return allFilePathRegex.Replace(html, match =>
        {
            var fullPath = Uri.UnescapeDataString(match.Value.Substring(8)); // 移除 "file:///"
            var fileName = Path.GetFileName(fullPath);

            _logger.LogWarning("強制清理殘留的 file:// 路徑: {OriginalPath} -> images/{FileName}",
                match.Value, fileName);

            return $"images/{fileName}";
        });
    }

    private async Task GenerateIndexPageAsync(List<BlogPost> posts, CategoryNode categoryTree)
    {
        _logger.LogInformation("開始生成首頁...");

        var indexHtml = await _templateService.RenderIndexAsync(posts, categoryTree);

        // 多層清理首頁中可能的 file:// 路徑
        for (int i = 0; i < 3; i++)
        {
            var beforeClean = indexHtml;
            indexHtml = CleanFileProtocolPaths(indexHtml);

            if (beforeClean == indexHtml) break;
        }

        var outputPath = Path.Combine(_configuration["BlogSettings:HtmlOutputPath"]!, "index.html");
        await _fileService.WriteFileAsync(outputPath, indexHtml);

        _logger.LogInformation("首頁生成完成");
    }

    private List<string> GetAlternativeImagePaths(string imagePath)
    {
        var alternatives = new List<string>();
        var blogContentPath = _configuration["BlogContentPath"]!;

        // 如果是絕對路徑，嘗試不同的基礎目錄
        if (Path.IsPathFullyQualified(imagePath))
        {
            // 嘗試將路徑中的 "部落格" 替換為 "攝影部落格"
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
            // 相對路徑，嘗試在 BlogContentPath 的各個子目錄中尋找
            var fileName = Path.GetFileName(imagePath);
            var directories = Directory.GetDirectories(blogContentPath, "*", SearchOption.AllDirectories);

            foreach (var dir in directories)
            {
                var possiblePath = Path.Combine(dir, fileName);
                alternatives.Add(possiblePath);

                // 也嘗試在 .assets 子目錄中尋找
                var assetsDir = Path.Combine(dir,
                    Path.GetFileNameWithoutExtension(Directory.GetParent(dir)?.Name ?? "") + ".assets");
                if (Directory.Exists(assetsDir))
                {
                    alternatives.Add(Path.Combine(assetsDir, fileName));
                }
            }
        }

        return alternatives.Distinct().ToList();
    }

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

    private string ProcessImagePaths(string markdown, string? sourceFilePath)
    {
        return ImageRegex.Replace(markdown, match =>
        {
            var originalSrc = match.Groups[1].Value;
            var processedSrc = ProcessSingleImagePath(originalSrc, sourceFilePath);
            return match.Value.Replace(originalSrc, processedSrc);
        });
    }

    private string ProcessSingleImagePath(string imageSrc, string? sourceFilePath)
    {
        try
        {
            _logger.LogDebug("處理圖片路徑: {ImageSrc}", imageSrc);

            // 處理網路圖片
            if (imageSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return imageSrc;
            }

            var originalSrc = imageSrc;

            // 處理 file:// 協定的路徑
            if (imageSrc.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                imageSrc = imageSrc.Substring(7); // 移除 "file://"
                _logger.LogDebug("移除 file:// 前綴: {Path}", imageSrc);
            }

            // URL 解碼（處理中文路徑）
            try
            {
                imageSrc = Uri.UnescapeDataString(imageSrc);
                _logger.LogDebug("URL 解碼後: {Path}", imageSrc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "URL 解碼失敗，使用原始路徑: {Path}", imageSrc);
            }

            // 確定絕對路徑
            string absoluteImagePath;
            if (Path.IsPathFullyQualified(imageSrc))
            {
                absoluteImagePath = Path.GetFullPath(imageSrc);
            }
            else
            {
                // 相對路徑，需要結合源文件目錄
                var sourceDir = string.IsNullOrEmpty(sourceFilePath)
                    ? _configuration["BlogContentPath"]!
                    : Path.GetDirectoryName(sourceFilePath)!;

                absoluteImagePath = Path.GetFullPath(Path.Combine(sourceDir, imageSrc));
            }

            _logger.LogDebug("檢查圖片檔案存在性: {Path}", absoluteImagePath);

            if (File.Exists(absoluteImagePath))
            {
                var fileName = Path.GetFileName(absoluteImagePath);

                // 處理重複檔名
                var uniqueFileName = GetUniqueFileName(fileName);

                // 修正：返回包含 images/ 前綴的網頁路徑
                var newImagePath = $"images/{uniqueFileName}";

                // 在映射表中只存檔名，用於檔案複製
                _imageMapping[absoluteImagePath] = uniqueFileName;

                _logger.LogInformation("圖片路徑映射成功: {Original} -> {New}", originalSrc, newImagePath);
                return newImagePath;
            }

            _logger.LogWarning("圖片檔案不存在: {ImagePath}", absoluteImagePath);

            // 嘗試在不同的可能目錄中尋找圖片
            var alternativePaths = GetAlternativeImagePaths(imageSrc);
            foreach (var altPath in alternativePaths)
            {
                _logger.LogDebug("嘗試替代路徑: {Path}", altPath);
                if (File.Exists(altPath))
                {
                    var fileName = Path.GetFileName(altPath);
                    var uniqueFileName = GetUniqueFileName(fileName);
                    var newImagePath = $"images/{uniqueFileName}";

                    _imageMapping[altPath] = uniqueFileName;

                    _logger.LogInformation("在替代路徑找到圖片: {Original} -> {New}", altPath, newImagePath);
                    return newImagePath;
                }
            }

            // 如果找不到圖片，返回一個預設的路徑
            var missingFileName = Path.GetFileName(imageSrc);
            return $"images/missing-{missingFileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "處理圖片路徑失敗: {ImageSrc}", imageSrc);
            return imageSrc;
        }
    }
}