using System.Web;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;

namespace Velo.Services;

public class MarkdownToHtmlConverter(
    IBlogService blogService,
    IConfiguration configuration,
    ILogger<MarkdownToHtmlConverter> logger)
    : IMarkdownToHtmlService
{
    private readonly Dictionary<string, string> _imageMapping = new();
    private readonly Dictionary<string, string> _mermaidPlaceholders = new();

    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .UseGenericAttributes()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseBootstrap()
        .UseMediaLinks()
        .Build();

    public async Task ConvertAndSaveAllPostsAsync()
    {
        logger.LogInformation("開始轉換所有文章...");

        _imageMapping.Clear();

        var posts = (await blogService.GetAllPostsAsync()).ToList();
        logger.LogInformation("找到 {Count} 篇文章", posts.Count);

        var outputPath = configuration["BlogSettings:HtmlOutputPath"];
        var imageOutputPath = configuration["BlogSettings:ImageOutputPath"];
        Directory.CreateDirectory(outputPath!);
        Directory.CreateDirectory(imageOutputPath!);

        // 1. 文章轉換 - 改為單執行緒循序處理
        foreach (var post in posts)
        {
            try
            {
                var htmlContent = await ConvertToHtmlAsync(post);
                await SaveHtmlFileAsync(post, htmlContent);
                logger.LogInformation("已轉換: {Title}", post.Title);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "轉換文章失敗: {Title}", post.Title);
            }
        }

        // 2. 複製圖片 - 改為單執行緒處理
        await CopyMappedImagesAsync();

        // 3. 生成首頁 & 靜態資源
        await GenerateIndexPageAsync(posts);
        await CopyOtherStaticResourcesAsync();

        logger.LogInformation("全部完成，文章 {Count} 篇，圖片 {ImageCount} 張",
            posts.Count, _imageMapping.Count);
    }

    private string ConvertMarkdownToHtml(string markdown, string? markdownFilePath = null)
    {
        try
        {
            logger.LogDebug("開始轉換 Markdown，檔案路徑: {FilePath}", markdownFilePath);

            // 先處理 Mermaid 程式碼區塊，用占位符替換
            var processedMarkdown = ProcessMermaidCodeBlocks(markdown);

            // 處理圖片路徑
            processedMarkdown = ProcessImagePaths(processedMarkdown, markdownFilePath);

            logger.LogDebug("處理 Mermaid 和圖片後的 Markdown");

            // 轉換為 HTML
            var html = Markdown.ToHtml(processedMarkdown, _pipeline);

            // 恢復 Mermaid 占位符為實際 HTML
            html = RestoreMermaidPlaceholders(html);

            // 後處理：確保圖片標籤正確
            html = PostProcessImageTags(html);

            logger.LogDebug("轉換後的 HTML 長度: {Length}", html.Length);

            return html;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換 Markdown 為 HTML 時發生錯誤");
            throw;
        }
    }

    private async Task<string> ConvertToHtmlAsync(BlogPost post)
    {
        try
        {
            logger.LogInformation("開始轉換文章: {Title}", post.Title);

            var htmlContent = ConvertMarkdownToHtml(post.ContentHtml, post.SourceFilePath);

            // 檢查是否有自定義模板
            var templatePath = Path.Combine(configuration["BlogSettings:TemplatePath"] ?? "", "post.html");
            if (File.Exists(templatePath))
            {
                return await GenerateCustomPostHtml(post, htmlContent, templatePath);
            }

            // 使用預設模板
            return GenerateDefaultPostHtml(post, htmlContent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換文章 {Title} 時發生錯誤", post.Title);
            throw;
        }
    }

    private async Task CopyMappedImagesAsync()
    {
        logger.LogInformation("開始複製圖片...");

        var imageOutputPath = configuration["BlogSettings:ImageOutputPath"];
    
        // 改為單執行緒循序處理，避免 Task.Run 和 Task.WhenAll
        foreach (var (originalPath, targetFileName) in _imageMapping)
        {
            try
            {
                var targetPath = Path.Combine(imageOutputPath!, targetFileName);
            
                if (File.Exists(originalPath))
                {
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    await using var sourceStream = File.OpenRead(originalPath);
                    await using var targetStream = File.Create(targetPath);
                    await sourceStream.CopyToAsync(targetStream);
                
                    logger.LogDebug("已複製圖片: {OriginalPath} -> {TargetPath}", originalPath, targetPath);
                }
                else
                {
                    logger.LogWarning("圖片檔案不存在: {Path}", originalPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "複製圖片失敗: {OriginalPath}", originalPath);
            }
        }

        logger.LogInformation("圖片複製完成");
    }


    private Task CopyOtherStaticResourcesAsync()
    {
        try
        {
            logger.LogInformation("開始複製靜態資源...");

            var outputPath = configuration["BlogSettings:HtmlOutputPath"];
            var imageOutputPath = configuration["BlogSettings:ImageOutputPath"];
            Debug.Assert(outputPath != null, nameof(outputPath) + " != null");
            Debug.Assert(imageOutputPath != null, nameof(imageOutputPath) + " != null");

            // 確保圖片輸出目錄存在
            if (!Directory.Exists(imageOutputPath))
            {
                Directory.CreateDirectory(imageOutputPath);
            }

            // 複製圖片檔案到對應的分類目錄
            foreach (var (originalPath, relativePath) in _imageMapping)
            {
                var targetPath = Path.Combine(outputPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetPath);

                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (File.Exists(originalPath))
                {
                    File.Copy(originalPath, targetPath, overwrite: true);
                    logger.LogDebug("複製圖片: {Original} -> {Target}", originalPath, targetPath);
                }
            }

            logger.LogInformation("靜態資源複製完成，共複製 {Count} 個檔案", _imageMapping.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "複製靜態資源時發生錯誤");
            throw;
        }

        return Task.CompletedTask;
    }

    private string FindMappedImagePath(string originalSrc)
    {
        try
        {
            // 先解碼 URL 編碼
            var decodedSrc = Uri.UnescapeDataString(originalSrc);

            // 嘗試直接查找映射
            foreach (var (originalPath, mappedPath) in _imageMapping)
            {
                // 檢查原始路徑是否匹配
                if (originalPath.EndsWith(decodedSrc.Replace("./", "")) ||
                    originalPath.EndsWith(decodedSrc) ||
                    Path.GetFileName(originalPath) == Path.GetFileName(decodedSrc))
                {
                    return mappedPath;
                }
            }

            // 如果沒有找到直接匹配，嘗試模糊匹配
            var fileName = Path.GetFileName(decodedSrc);
            foreach (var (originalPath, mappedPath) in _imageMapping)
            {
                if (Path.GetFileName(originalPath) == fileName)
                {
                    return mappedPath;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "查找映射路徑時發生錯誤: {Src}", originalSrc);
            return null;
        }
    }

    /// <summary>
    /// 生成麵包屑導航
    /// </summary>
    private string GenerateBreadcrumb(List<string> categories, string backToIndexPath)
    {
        var breadcrumbs = new List<string>();

        // 首頁連結
        var indexPath = backToIndexPath;
        breadcrumbs.Add($"<a href=\"{indexPath}\">首頁</a>");

        // 分類層級
        var currentPath = "";
        var levelUp = categories.Count;

        for (int i = 0; i < categories.Count; i++)
        {
            currentPath += SanitizePath(categories[i]);
            levelUp--;

            if (i < categories.Count - 1)
            {
                // 中間層級，可點擊
                var categoryIndexPath = string.Join("", Enumerable.Repeat("../", levelUp)) + "index.html";
                breadcrumbs.Add($"<a href=\"{categoryIndexPath}\">{categories[i]}</a>");
            }
            else
            {
                // 最後一層，當前頁面，不可點擊
                breadcrumbs.Add($"<span>{categories[i]}</span>");
            }

            if (i < categories.Count - 1)
            {
                currentPath += "/";
            }
        }

        return string.Join(" > ", breadcrumbs);
    }

    private string GenerateCategoryNodeHtml(CategoryNode node, string parentPath)
    {
        var sb = new StringBuilder();

        foreach (var child in node.Children)
        {
            var currentPath = string.IsNullOrEmpty(parentPath) ? child.Name : $"{parentPath}/{child.Name}";
            var hasChildren = child.Children.Count > 0;

            sb.AppendLine("<div class=\"category-node\">");

            // 分類項目
            sb.AppendLine(
                $"  <div class=\"category-item\" data-category-path=\"{currentPath}\" data-category-name=\"{child.Name}\">");
            sb.AppendLine($"    <span class=\"category-name\">");

            // 如果有子分類，添加展開/收合按鈕
            if (hasChildren)
            {
                sb.AppendLine($"      <span class=\"category-toggle\">▶</span>");
            }

            // 根據是否有子分類添加圖示
            if (hasChildren)
            {
                sb.AppendLine($"      📁 {child.Name}");
            }
            else
            {
                sb.AppendLine($"      📄 {child.Name}");
            }

            sb.AppendLine("    </span>");

            // 只有當文章數量大於 0 時才顯示數字
            if (child.PostCount > 0)
            {
                sb.AppendLine($"    <span class=\"category-count\">{child.PostCount}</span>");
            }

            sb.AppendLine("  </div>");

            // 遞歸處理子分類
            if (hasChildren)
            {
                sb.AppendLine("  <div class=\"category-children\">");
                sb.AppendLine(GenerateCategoryNodeHtml(child, currentPath));
                sb.AppendLine("  </div>");
            }

            sb.AppendLine("</div>");
        }

        return sb.ToString();
    }

    private string GenerateCategoryTreeHtml(CategoryNode categoryTree)
    {
        try
        {
            if (categoryTree.Children.Count == 0)
            {
                return "<p class=\"no-categories\">📝 尚無分類</p>";
            }

            return GenerateCategoryNodeHtml(categoryTree, "");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "生成分類樹 HTML 時發生錯誤");
            return "<p class=\"error\">⚠️ 分類樹載入失敗</p>";
        }
    }

    private async Task GenerateCustomIndexPageAsync(List<BlogPost> posts, string templatePath)
    {
        try
        {
            var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
            logger.LogInformation("使用自訂模板生成首頁: {TemplatePath}", templatePath);

            // 取得分類樹
            var categoryTree = await blogService.GetCategoryTreeAsync();
            var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);

            // 處理模板語法
            var processedTemplate = ProcessEachPostsSyntax(template, posts);
            processedTemplate = ProcessConditionalSyntax(processedTemplate,
                posts.SelectMany(p => p.Tags).Distinct().ToList(),
                posts.SelectMany(p => p.Categories).Distinct().ToList());

            // 替換變數
            var htmlContent = processedTemplate
                .Replace("{{PostCount}}", posts.Count.ToString())
                .Replace("{{{CategoryTree}}}", categoryTreeHtml)
                .Replace("{{CategoryTree}}", categoryTreeHtml);

            var outputPath = Path.Combine(configuration["BlogSettings:HtmlOutputPath"]!, "index.html");
            await File.WriteAllTextAsync(outputPath, htmlContent, Encoding.UTF8);

            logger.LogInformation("自訂首頁已生成: {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "生成自訂首頁時發生錯誤");
            throw;
        }
    }

    private async Task<string> GenerateCustomPostHtml(BlogPost post, string htmlContent, string templatePath)
    {
        try
        {
            var template = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
            logger.LogInformation("使用自訂模板: {TemplatePath}", templatePath);

            var tagsHtml = string.Empty;
            if (post.Tags.Count > 0)
            {
                tagsHtml = string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"));
            }

            var categoriesHtml = string.Empty;
            if (post.Categories.Count > 0)
            {
                categoriesHtml = string.Join("",
                    post.Categories.Select(category => $"<span class=\"category\">{category}</span>"));
            }

            var processedTemplate = ProcessConditionalSyntax(template, post.Tags, post.Categories);

            return processedTemplate
                .Replace("{{Title}}", post.Title)
                .Replace("{{{Content}}}", htmlContent)
                .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
                .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
                .Replace("{{Slug}}", post.Slug)
                .Replace("{{{Tags}}}", tagsHtml)
                .Replace("{{TagsPlain}}", post.Tags.Count > 0 ? string.Join(", ", post.Tags) : "")
                .Replace("{{{Categories}}}", categoriesHtml)
                .Replace("{{CategoriesPlain}}", post.Categories.Count > 0 ? string.Join(", ", post.Categories) : "");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "生成自訂文章 HTML 時發生錯誤");
            throw;
        }
    }

    private async Task GenerateDefaultIndexPageAsync(IEnumerable<BlogPost> posts)
    {
        try
        {
            var postsList = posts.ToList();
            logger.LogInformation("生成預設首頁，文章數量: {Count}", postsList.Count);

            // 取得分類樹
            var categoryTree = await blogService.GetCategoryTreeAsync();
            var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);

            var postsHtml = string.Join("", postsList.Select(post =>
            {
                var tagsHtml = string.Empty;
                if (post.Tags.Count > 0)
                {
                    tagsHtml = string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"));
                }

                var categoriesHtml = string.Empty;
                if (post.Categories.Count > 0)
                {
                    categoriesHtml = string.Join("",
                        post.Categories.Select(category => $"<span class=\"category\">{category}</span>"));
                }

                var categoriesPlain = post.Categories.Count > 0 ? string.Join("/", post.Categories) : "";

                return $"""
                        <article class="post-item" data-categories="{categoriesPlain}">
                            <h2><a href="{post.HtmlFilePath}">{post.Title}</a></h2>
                            <div class="post-meta">
                                <span class="date">{post.PublishedDate:yyyy-MM-dd}</span>
                                {(post.Categories.Count > 0 ? $"<div class=\"categories\">{categoriesHtml}</div>" : "")}
                                {(post.Tags.Count > 0 ? $"<div class=\"tags\">{tagsHtml}</div>" : "")}
                            </div>
                        </article>
                        """;
            }));

            var defaultIndexHtml = $$"""
                                     <!DOCTYPE html>
                                     <html lang="zh-TW">
                                     <head>
                                         <meta charset="UTF-8">
                                         <meta name="viewport" content="width=device-width, initial-scale=1.0">
                                         <title>LEOSHIANG 的部落格</title>
                                         <style>
                                             body { 
                                                 font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; 
                                                 line-height: 1.6; 
                                                 margin: 0; 
                                                 padding: 20px; 
                                                 background-color: #f8f9fa; 
                                             }
                                             .container { 
                                                 max-width: 1200px; 
                                                 margin: 0 auto; 
                                                 display: grid; 
                                                 grid-template-columns: 1fr 300px; 
                                                 gap: 40px; 
                                             }
                                             .sidebar { 
                                                 background: white; 
                                                 padding: 20px; 
                                                 border-radius: 8px; 
                                                 box-shadow: 0 2px 10px rgba(0,0,0,0.1); 
                                                 height: fit-content; 
                                                 position: sticky; 
                                                 top: 20px; 
                                             }
                                             .main-content { 
                                                 background: white; 
                                                 padding: 30px; 
                                                 border-radius: 8px; 
                                                 box-shadow: 0 2px 10px rgba(0,0,0,0.1); 
                                             }
                                             .category-tree { 
                                                 margin-bottom: 30px; 
                                             }
                                             .category-tree h3 { 
                                                 margin-top: 0; 
                                                 margin-bottom: 15px; 
                                                 color: #333; 
                                                 font-size: 1.2rem; 
                                                 border-bottom: 2px solid #007bff; 
                                                 padding-bottom: 8px; 
                                             }
                                             .category-node { 
                                                 margin-bottom: 5px; 
                                             }
                                             .category-item { 
                                                 display: flex; 
                                                 align-items: center; 
                                                 justify-content: space-between; 
                                                 padding: 5px 0; 
                                                 color: #555; 
                                                 font-size: 0.9rem; 
                                                 cursor: pointer;
                                                 transition: background-color 0.2s ease;
                                                 border-radius: 4px;
                                                 padding: 8px 10px;
                                             }
                                             .category-item:hover {
                                                 background-color: #f8f9fa;
                                             }
                                             .category-item.active {
                                                 background-color: #007bff;
                                                 color: white;
                                             }
                                             .category-item.active .category-count {
                                                 background-color: white;
                                                 color: #007bff;
                                             }
                                             .category-name { 
                                                 font-weight: 500; 
                                                 display: flex;
                                                 align-items: center;
                                                 gap: 5px;
                                             }
                                             .category-toggle {
                                                 font-size: 0.8rem;
                                                 color: #6c757d;
                                                 cursor: pointer;
                                                 transition: transform 0.2s ease;
                                                 margin-right: 5px;
                                             }
                                             .category-toggle.expanded {
                                                 transform: rotate(90deg);
                                             }
                                             .category-count { 
                                                 background: #007bff; 
                                                 color: white; 
                                                 padding: 2px 6px; 
                                                 border-radius: 10px; 
                                                 font-size: 0.8rem; 
                                             }
                                             .category-children { 
                                                 margin-left: 20px; 
                                                 border-left: 2px solid #e9ecef; 
                                                 padding-left: 10px; 
                                                 display: none;
                                             }
                                             .category-children.expanded {
                                                 display: block;
                                             }
                                             .post-item { 
                                                 margin-bottom: 30px; 
                                                 padding-bottom: 20px; 
                                                 border-bottom: 1px solid #e9ecef; 
                                                 transition: opacity 0.3s ease, transform 0.3s ease;
                                             }
                                             .post-item:last-child { 
                                                 border-bottom: none; 
                                             }
                                             .post-item.hidden {
                                                 display: none;
                                             }
                                             .post-item h2 { 
                                                 margin-bottom: 10px; 
                                                 font-size: 1.5rem; 
                                             }
                                             .post-item h2 a { 
                                                 color: #333; 
                                                 text-decoration: none; 
                                                 transition: color 0.3s ease; 
                                             }
                                             .post-item h2 a:hover { 
                                                 color: #007bff; 
                                             }
                                             .post-meta { 
                                                 color: #666; 
                                                 font-size: 0.9rem; 
                                                 display: flex; 
                                                 flex-wrap: wrap; 
                                                 gap: 15px; 
                                                 align-items: center; 
                                             }
                                             .tag, .category { 
                                                 background: #007bff; 
                                                 color: white; 
                                                 padding: 3px 8px; 
                                                 border-radius: 12px; 
                                                 margin-right: 5px; 
                                                 font-size: 0.8rem; 
                                             }
                                             .category { 
                                                 background: #28a745; 
                                             }
                                             .blog-header { 
                                                 margin-bottom: 30px; 
                                                 text-align: center; 
                                             }
                                             .blog-header h1 { 
                                                 color: #333; 
                                                 margin-bottom: 10px; 
                                             }
                                             .blog-stats { 
                                                 color: #666; 
                                                 font-size: 0.9rem; 
                                                 display: flex;
                                                 justify-content: center;
                                                 gap: 20px;
                                                 align-items: center;
                                             }
                                             .clear-filter {
                                                 background: #6c757d;
                                                 color: white;
                                                 border: none;
                                                 padding: 5px 10px;
                                                 border-radius: 4px;
                                                 font-size: 0.8rem;
                                                 cursor: pointer;
                                                 transition: background-color 0.2s ease;
                                             }
                                             .clear-filter:hover {
                                                 background: #5a6268;
                                             }
                                             .clear-filter.hidden {
                                                 display: none;
                                             }
                                             .filter-info {
                                                 color: #007bff;
                                                 font-weight: bold;
                                                 margin-bottom: 20px;
                                                 padding: 10px;
                                                 background: #e7f3ff;
                                                 border-radius: 4px;
                                                 display: none;
                                             }
                                             .filter-info.active {
                                                 display: block;
                                             }
                                             @media (max-width: 768px) {
                                                 .container { 
                                                     grid-template-columns: 1fr; 
                                                     gap: 20px; 
                                                 }
                                                 .sidebar { 
                                                     position: static; 
                                                     order: -1; 
                                                 }
                                                 body { 
                                                     padding: 10px; 
                                                 }
                                             }
                                         </style>
                                     </head>
                                     <body>
                                         <div class="container">
                                             <main class="main-content">
                                                 <header class="blog-header">
                                                     <h1>LEOSHIANG 的部落格</h1>
                                                     <div class="blog-stats">
                                                         <span>📝 文章總數: <span id="total-count">{{postsList.Count}}</span></span>
                                                         <span>🔍 顯示: <span id="visible-count">{{postsList.Count}}</span> 篇</span>
                                                         <button class="clear-filter hidden" id="clear-filter">清除篩選</button>
                                                     </div>
                                                 </header>
                                                 
                                                 <div class="filter-info" id="filter-info">
                                                     正在顯示分類「<span id="filter-category"></span>」的文章
                                                 </div>

                                                 <div class="posts" id="posts-container">
                                                     {{postsHtml}}
                                                 </div>
                                             </main>
                                             
                                             <aside class="sidebar">
                                                 <div class="category-tree">
                                                     <h3>📁 分類目錄</h3>
                                                     {{categoryTreeHtml}}
                                                 </div>
                                             </aside>
                                         </div>

                                         <script>
                                             // 分類樹功能
                                             class CategoryTree {
                                                 constructor() {
                                                     this.currentFilter = null;
                                                     this.allPosts = [];
                                                     this.init();
                                                 }

                                                 init() {
                                                     // 收集所有文章
                                                     this.allPosts = Array.from(document.querySelectorAll('.post-item'));
                                                     
                                                     // 綁定分類項目點擊事件
                                                     this.bindCategoryEvents();
                                                     
                                                     // 綁定清除篩選按鈕
                                                     document.getElementById('clear-filter').addEventListener('click', () => {
                                                         this.clearFilter();
                                                     });
                                                 }

                                                 bindCategoryEvents() {
                                                     // 綁定展開/收合事件
                                                     document.querySelectorAll('.category-toggle').forEach(toggle => {
                                                         toggle.addEventListener('click', (e) => {
                                                             e.stopPropagation();
                                                             this.toggleCategory(toggle);
                                                         });
                                                     });

                                                     // 綁定分類篩選事件
                                                     document.querySelectorAll('.category-item').forEach(item => {
                                                         item.addEventListener('click', (e) => {
                                                             // 如果點擊的是展開按鈕，不執行篩選
                                                             if (e.target.classList.contains('category-toggle')) {
                                                                 return;
                                                             }
                                                             
                                                             const categoryPath = item.getAttribute('data-category-path');
                                                             const categoryName = item.getAttribute('data-category-name');
                                                             
                                                             if (categoryPath && categoryName) {
                                                                 this.filterByCategory(categoryPath, categoryName);
                                                             }
                                                         });
                                                     });
                                                 }

                                                 toggleCategory(toggle) {
                                                     const categoryNode = toggle.closest('.category-node');
                                                     const children = categoryNode.querySelector('.category-children');
                                                     
                                                     if (children) {
                                                         const isExpanded = children.classList.contains('expanded');
                                                         
                                                         if (isExpanded) {
                                                             children.classList.remove('expanded');
                                                             toggle.classList.remove('expanded');
                                                         } else {
                                                             children.classList.add('expanded');
                                                             toggle.classList.add('expanded');
                                                         }
                                                     }
                                                 }

                                                 filterByCategory(categoryPath, categoryName) {
                                                     this.currentFilter = categoryPath;
                                                     
                                                     // 移除之前的活動狀態
                                                     document.querySelectorAll('.category-item').forEach(item => {
                                                         item.classList.remove('active');
                                                     });
                                                     
                                                     // 添加當前活動狀態
                                                     document.querySelectorAll(`[data-category-path="${categoryPath}"]`).forEach(item => {
                                                         item.classList.add('active');
                                                     });

                                                     // 篩選文章
                                                     let visibleCount = 0;
                                                     this.allPosts.forEach(post => {
                                                         const postCategories = post.getAttribute('data-categories') || '';
                                                         const shouldShow = postCategories.includes(categoryPath);
                                                         
                                                         if (shouldShow) {
                                                             post.classList.remove('hidden');
                                                             visibleCount++;
                                                         } else {
                                                             post.classList.add('hidden');
                                                         }
                                                     });

                                                     // 更新統計信息
                                                     this.updateStats(visibleCount, categoryName);
                                                 }

                                                 clearFilter() {
                                                     this.currentFilter = null;
                                                     
                                                     // 移除所有活動狀態
                                                     document.querySelectorAll('.category-item').forEach(item => {
                                                         item.classList.remove('active');
                                                     });
                                                     
                                                     // 顯示所有文章
                                                     this.allPosts.forEach(post => {
                                                         post.classList.remove('hidden');
                                                     });
                                                     
                                                     // 更新統計信息
                                                     this.updateStats(this.allPosts.length, null);
                                                 }

                                                 updateStats(visibleCount, categoryName) {
                                                     const visibleCountElement = document.getElementById('visible-count');
                                                     const clearFilterButton = document.getElementById('clear-filter');
                                                     const filterInfo = document.getElementById('filter-info');
                                                     const filterCategory = document.getElementById('filter-category');
                                                     
                                                     visibleCountElement.textContent = visibleCount;
                                                     
                                                     if (categoryName) {
                                                         clearFilterButton.classList.remove('hidden');
                                                         filterInfo.classList.add('active');
                                                         filterCategory.textContent = categoryName;
                                                     } else {
                                                         clearFilterButton.classList.add('hidden');
                                                         filterInfo.classList.remove('active');
                                                     }
                                                 }
                                             }

                                             // 初始化分類樹
                                             document.addEventListener('DOMContentLoaded', () => {
                                                 new CategoryTree();
                                             });
                                         </script>
                                     </body>
                                     </html>
                                     """;

            var htmlOutputPath = configuration["BlogSettings:HtmlOutputPath"];
            Debug.Assert(htmlOutputPath != null, nameof(htmlOutputPath) + " != null");
            var outputPath = Path.Combine(htmlOutputPath, "index.html");
            await File.WriteAllTextAsync(outputPath, defaultIndexHtml, Encoding.UTF8);

            logger.LogInformation("預設首頁已生成: {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "生成預設首頁時發生錯誤");
            throw;
        }
    }

    /// <summary>
    /// 產生單篇文章的預設 HTML（與 index.html 版面一致，右側 TOC + 返回首頁）
    /// </summary>
    private string GenerateDefaultPostHtml(BlogPost post, string htmlContent)
    {
        var title = HttpUtility.HtmlEncode(post.Title);
        var publishedDate = post.PublishedDate.ToString("yyyy-MM-dd");

        return $$"""
                 <!DOCTYPE html>
                 <html lang="zh-TW">
                 <head>
                     <meta charset="UTF-8">
                     <meta content="width=device-width, initial-scale=1.0" name="viewport">
                     <title>{{title}}</title>
                     <style>
                         /* 與 index.html 共用的基礎樣式 */
                         body {
                             font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
                             line-height: 1.6;
                             margin: 0;
                             padding: 20px;
                             background-color: #f8f9fa;
                         }
                         .container {
                             max-width: 1200px;
                             margin: 0 auto;
                             display: grid;
                             grid-template-columns: 1fr 300px;
                             gap: 40px;
                         }
                         .main-content {
                             background: white;
                             padding: 30px;
                             border-radius: 8px;
                             box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);
                         }
                         .sidebar {
                             background: white;
                             padding: 20px;
                             border-radius: 8px;
                             box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);
                             position: sticky;
                             top: 20px;
                             max-height: calc(100vh - 40px);
                             overflow-y: auto;
                             display: flex;
                             flex-direction: column;
                             gap: 20px;
                         }
                         .home-link {
                             font-size: 0.9rem;
                             background: #28a745;
                             color: #fff;
                             padding: 8px 12px;
                             border-radius: 6px;
                             text-decoration: none;
                             text-align: center;
                         }
                         .home-link:hover { background: #218838; }
                         /* TOC 樣式 */
                         .toc-title {
                             margin: 0 0 10px 0;
                             font-size: 1.1rem;
                             border-bottom: 2px solid #007bff;
                             padding-bottom: 6px;
                             color: #333;
                         }
                         .toc-list {
                             list-style: none;
                             padding-left: 0;
                             font-size: 0.85rem;
                         }
                         .toc-list li {
                             margin-bottom: 6px;
                         }
                         .toc-list a {
                             text-decoration: none;
                             color: #007bff;
                         }
                         .toc-list a:hover {
                             text-decoration: underline;
                         }
                     </style>
                 </head>
                 <body>
                     <div class="container">
                         <!-- 左側：文章主要內容 -->
                         <article class="main-content">
                             <h1>{{title}}</h1>
                             <p style="color:#6c757d;font-size:0.9rem;">發布日期：{{publishedDate}}</p>
                             {{{htmlContent}}}
                         </article>

                         <!-- 右側：返回首頁 + 目錄 -->
                         <aside class="sidebar">
                             <a href="index.html" class="home-link">🏠 返回首頁</a>

                             <div>
                                 <h3 class="toc-title">目錄</h3>
                                 <ul id="toc" class="toc-list"></ul>
                             </div>
                         </aside>
                     </div>

                     <script>
                         // 動態產生 TOC
                         (function () {
                             const tocUl = document.getElementById('toc');
                             if (!tocUl) return;

                             const headings = document.querySelectorAll('article h1, article h2, article h3');
                             headings.forEach(h => {
                                 if (!h.id) {
                                     h.id = h.textContent.trim().toLowerCase()
                                         .replace(/\\s+/g, '-')
                                         .replace(/[^a-z0-9\\-]/g, '');
                                 }
                                 const li = document.createElement('li');
                                 li.style.marginLeft = (parseInt(h.tagName.substring(1)) - 1) * 10 + 'px';
                                 const a = document.createElement('a');
                                 a.href = '#' + h.id;
                                 a.textContent = h.textContent;
                                 li.appendChild(a);
                                 tocUl.appendChild(li);
                             });
                         })();
                     </script>
                 </body>
                 </html>
                 """;
    }

    private string GenerateImageFileName(string originalPath)
    {
        try
        {
            var extension = Path.GetExtension(originalPath).ToLowerInvariant();
            var guid = Guid.NewGuid().ToString("N");
            return $"{guid}{extension}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "生成圖片檔名時發生錯誤，使用原始檔名");
            return Path.GetFileName(originalPath);
        }
    }

    private async Task GenerateIndexPageAsync(IEnumerable<BlogPost> posts)
    {
        try
        {
            var postsList = posts.ToList();
            logger.LogInformation("生成首頁，文章數量: {Count}", postsList.Count);

            var templatePath = Path.Combine(configuration["BlogSettings:TemplatePath"] ?? "", "index.html");

            if (File.Exists(templatePath))
            {
                await GenerateCustomIndexPageAsync(postsList, templatePath);
            }
            else
            {
                await GenerateDefaultIndexPageAsync(postsList);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "生成首頁時發生錯誤");
            throw;
        }
    }

    private async Task<string> GetFileHashAsync(string filePath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private bool IsGuidFileName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return nameWithoutExtension.Length == 32 && nameWithoutExtension.All(char.IsLetterOrDigit);
    }

    private bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg";
    }

    private Task OrganizeExistingImagesAsync(string imageOutputPath)
    {
        try
        {
            logger.LogInformation("開始整理現有圖片檔案...");

            // 只處理根目錄下的圖片檔案（應該移到子目錄中）
            var rootImageFiles = Directory.GetFiles(imageOutputPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsImageFile)
                .ToList();

            var movedCount = 0;

            foreach (var imageFile in rootImageFiles)
            {
                var fileName = Path.GetFileName(imageFile);

                // 如果檔案名稱是 GUID 格式，移動到對應的子目錄
                if (IsGuidFileName(fileName))
                {
                    var subDir = fileName.Substring(0, 2);
                    var targetSubDirPath = Path.Combine(imageOutputPath, subDir);
                    var targetFilePath = Path.Combine(targetSubDirPath, fileName);

                    // 確保目標子目錄存在
                    if (!Directory.Exists(targetSubDirPath))
                    {
                        Directory.CreateDirectory(targetSubDirPath);
                    }

                    // 移動檔案
                    if (!File.Exists(targetFilePath))
                    {
                        File.Move(imageFile, targetFilePath);
                        movedCount++;
                        logger.LogDebug("整理圖片: {Original} -> {Target}", imageFile, targetFilePath);
                    }
                    else
                    {
                        // 如果目標檔案已存在，刪除原始檔案
                        File.Delete(imageFile);
                        logger.LogDebug("刪除重複的圖片檔案: {File}", imageFile);
                    }
                }
            }

            logger.LogInformation("現有圖片檔案整理完成，移動了 {Count} 張圖片", movedCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "整理現有圖片檔案時發生錯誤");
        }

        return Task.CompletedTask;
    }

    private string PostProcessImageTags(string html)
    {
        try
        {
            // 處理 HTML 中的 img 標籤，確保使用正確的 UUID 檔名
            var imagePattern = @"<img\s+([^>]*)\s*/?>";
            var regex = new Regex(imagePattern);

            var result = regex.Replace(html, match =>
            {
                var fullMatch = match.Value;
                var attributes = match.Groups[1].Value;

                // 解析 src 屬性
                var srcPattern = @"src\s*=\s*[""']([^""']+)[""']";
                var srcMatch = Regex.Match(attributes, srcPattern);

                if (srcMatch.Success)
                {
                    var originalSrc = srcMatch.Groups[1].Value;

                    // 如果是網路圖片，不需要處理
                    if (originalSrc.StartsWith("http") || originalSrc.StartsWith("data:"))
                    {
                        // 只添加 loading 屬性
                        if (!attributes.Contains("loading="))
                        {
                            attributes += " loading=\"lazy\"";
                        }

                        return $"<img {attributes.Trim()}>";
                    }

                    // 處理本地圖片路徑
                    var mappedPath = FindMappedImagePath(originalSrc);
                    if (!string.IsNullOrEmpty(mappedPath))
                    {
                        // 更新 src 屬性
                        var updatedAttributes = Regex.Replace(attributes, srcPattern, $"src=\"{mappedPath}\"");

                        // 添加 loading 屬性
                        if (!updatedAttributes.Contains("loading="))
                        {
                            updatedAttributes += " loading=\"lazy\"";
                        }

                        logger.LogDebug("更新圖片路徑: {Original} -> {Updated}", originalSrc, mappedPath);
                        return $"<img {updatedAttributes.Trim()}>";
                    }
                    else
                    {
                        logger.LogWarning("找不到圖片映射: {Path}", originalSrc);
                    }
                }

                // 如果沒有找到 src 或無法處理，只添加 loading 屬性
                if (!attributes.Contains("loading="))
                {
                    attributes += " loading=\"lazy\"";
                }

                return $"<img {attributes.Trim()}>";
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "後處理圖片標籤時發生錯誤");
            return html;
        }
    }

    private string ProcessConditionalSyntax(string template, List<string> tags, List<string> categories)
    {
        try
        {
            var tagsPattern = @"{{#if Tags}}(.*?){{/if}}";
            var tagsRegex = new Regex(tagsPattern, RegexOptions.Singleline);
            template = tagsRegex.Replace(template, match =>
                tags.Count > 0 ? match.Groups[1].Value : "");

            var categoriesPattern = @"{{#if Categories}}(.*?){{/if}}";
            var categoriesRegex = new Regex(categoriesPattern, RegexOptions.Singleline);
            template = categoriesRegex.Replace(template, match =>
                categories.Count > 0 ? match.Groups[1].Value : "");

            return template;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "處理條件語法時發生錯誤");
            return template;
        }
    }

    private string ProcessEachPostsSyntax(string template, List<BlogPost> posts)
    {
        try
        {
            var eachPattern = @"{{#each Posts}}(.*?){{/each}}";
            var eachRegex = new Regex(eachPattern, RegexOptions.Singleline);

            return eachRegex.Replace(template, match =>
            {
                var itemTemplate = match.Groups[1].Value;
                var itemsHtml = string.Join("", posts.Select(post =>
                {
                    var tagsHtml = string.Empty;
                    if (post.Tags.Count > 0)
                    {
                        tagsHtml = string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"));
                    }

                    var categoriesHtml = string.Empty;
                    if (post.Categories.Count > 0)
                    {
                        categoriesHtml = string.Join("",
                            post.Categories.Select(category => $"<span class=\"category\">{category}</span>"));
                    }

                    Debug.Assert(post.Tags != null);
                    Debug.Assert(post.Categories != null);
                    var processedItem = ProcessConditionalSyntax(itemTemplate, post.Tags, post.Categories);

                    return processedItem
                        .Replace("{{Title}}", post.Title)
                        .Replace("{{Slug}}", post.Slug)
                        .Replace("{{HtmlFilePath}}", post.HtmlFilePath)
                        .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
                        .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
                        .Replace("{{{Tags}}}", tagsHtml)
                        .Replace("{{TagsPlain}}", post.Tags.Count > 0 ? string.Join(", ", post.Tags) : "")
                        .Replace("{{{Categories}}}", categoriesHtml)
                        .Replace("{{CategoriesPlain}}",
                            post.Categories.Count > 0 ? string.Join("/", post.Categories) : "");
                }));

                return itemsHtml;
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "處理 {{#each Posts}} 語法時發生錯誤");
            return template;
        }
    }

    private string ProcessImagePaths(string markdown, string? markdownFilePath)
    {
        try
        {
            if (string.IsNullOrEmpty(markdownFilePath))
            {
                return markdown;
            }

            var imagePattern = @"!\[([^\]]*)\]\(([^)]+)\)";
            var regex = new Regex(imagePattern);

            logger.LogDebug("開始處理圖片路徑，Markdown 檔案: {FilePath}", markdownFilePath);

            var result = regex.Replace(markdown, match =>
            {
                var altText = match.Groups[1].Value;
                var imagePath = match.Groups[2].Value;

                logger.LogDebug("找到圖片: {AltText}, 路徑: {Path}", altText, imagePath);

                // 如果是網路圖片，不需要處理
                if (imagePath.StartsWith("http"))
                {
                    return match.Value;
                }

                // 處理相對路徑和絕對路徑
                string fullImagePath;

                if (Path.IsPathRooted(imagePath))
                {
                    // 絕對路徑
                    fullImagePath = imagePath;
                }
                else
                {
                    // 相對路徑 - 需要先解碼 URL 編碼
                    var decodedPath = Uri.UnescapeDataString(imagePath);
                    logger.LogDebug("解碼圖片路徑: {Original} -> {Decoded}", imagePath, decodedPath);

                    var markdownDir = Path.GetDirectoryName(markdownFilePath);
                    if (string.IsNullOrEmpty(markdownDir))
                    {
                        return match.Value;
                    }

                    // 處理 "./" 開頭的路徑
                    if (decodedPath.StartsWith("./"))
                    {
                        decodedPath = decodedPath.Substring(2);
                    }

                    fullImagePath = Path.Combine(markdownDir, decodedPath);
                    fullImagePath = Path.GetFullPath(fullImagePath);
                }

                logger.LogDebug("轉換圖片路徑: {Original} -> {Full}", imagePath, fullImagePath);

                // 檢查圖片是否存在
                if (File.Exists(fullImagePath))
                {
                    // 檢查是否已經有映射關係
                    if (!_imageMapping.ContainsKey(fullImagePath))
                    {
                        // 生成新的檔案名稱
                        var newFileName = GenerateImageFileName(fullImagePath);
                        var subDir = newFileName.Substring(0, 2);
                        var relativePath = $"images/{subDir}/{newFileName}";

                        // 記錄映射關係
                        _imageMapping[fullImagePath] = relativePath;

                        logger.LogDebug("圖片映射: {Original} -> {New}", fullImagePath, relativePath);
                    }

                    var mappedPath = _imageMapping[fullImagePath];
                    return $"![{altText}]({mappedPath})";
                }
                else
                {
                    logger.LogWarning("圖片檔案不存在: {Path}", fullImagePath);
                }

                // 返回原始內容
                return match.Value;
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "處理圖片路徑時發生錯誤");
            return markdown;
        }
    }

    private string ProcessMermaidCodeBlocks(string markdown)
    {
        try
        {
            // 清空之前的占位符
            _mermaidPlaceholders.Clear();

            // 匹配 Mermaid 程式碼區塊
            var mermaidPattern = @"```mermaid\r?\n(.*?)\r?\n```";
            var regex = new Regex(mermaidPattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var matches = regex.Matches(markdown);
            logger.LogDebug("找到 {Count} 個 Mermaid 圖表", matches.Count);

            var result = regex.Replace(markdown, match =>
            {
                var mermaidCode = match.Groups[1].Value.Trim();
                logger.LogDebug("處理 Mermaid 圖表，程式碼長度: {Length}", mermaidCode.Length);

                // 生成唯一的占位符和 ID
                var chartId = $"mermaid-{Guid.NewGuid().ToString("N")[..8]}";
                var placeholder = $"MERMAID_PLACEHOLDER_{chartId}";

                // 儲存 Mermaid HTML 到占位符映射
                _mermaidPlaceholders[placeholder] = $"<div class=\"mermaid\" id=\"{chartId}\">\n{mermaidCode}\n</div>";

                // 返回占位符
                return placeholder;
            });

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "處理 Mermaid 程式碼區塊時發生錯誤，使用原始內容");
            return markdown;
        }
    }

    private string RestoreMermaidPlaceholders(string html)
    {
        try
        {
            // 建立快照以避免枚舉期間修改集合的例外
            var snapshot = _mermaidPlaceholders.ToList();

            // 將占位符替換回 Mermaid HTML
            foreach (var (key, value) in snapshot)
            {
                // 占位符可能被包裝在 <p> 標籤中，需要移除
                html = html.Replace($"<p>{key}</p>", value);
                html = html.Replace(key, value);
            }

            return html;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "恢復 Mermaid 占位符時發生錯誤");
            return html;
        }
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

    private async Task SaveHtmlFileAsync(BlogPost post, string htmlContent)
    {
        var outputPath = configuration["BlogSettings:HtmlOutputPath"]!;
        var fullOutputPath = Path.Combine(outputPath, post.HtmlFilePath);
        var directory = Path.GetDirectoryName(fullOutputPath)!;

        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory); // 只有 root 目錄時，這行幾乎不做事

        await File.WriteAllTextAsync(fullOutputPath, htmlContent, Encoding.UTF8);
    }
}