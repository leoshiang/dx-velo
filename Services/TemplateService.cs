using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;

namespace Velo.Services;

/// <summary>
/// 模板服務：負責處理 HTML 模板的載入、渲染和變數替換
/// 支援自定義模板和預設內建模板，提供豐富的模板變數和條件語法
/// </summary>
/// <remarks>
/// 主要功能：
/// 1. 支援首頁（index.html）和文章頁（post.html）模板
/// 2. 提供 Handlebars 風格的模板語法（{{變數}}、{{{HTML變數}}}、條件語法）
/// 3. 自動生成分類樹狀結構和標籤 HTML
/// 4. 支援圖片處理和響應式佈局
/// </remarks>
public class TemplateService(
    IConfiguration configuration,
    IFileService fileService,
    ILogger<TemplateService> logger) : ITemplateService
{
    #region 公開方法 - 主要渲染入口

    /// <summary>
    /// 渲染首頁 HTML：根據文章列表和分類樹生成首頁內容
    /// </summary>
    /// <param name="posts">文章列表（按發佈日期排序）</param>
    /// <param name="categoryTree">分類樹狀結構</param>
    /// <returns>完整的首頁 HTML 字串</returns>
    public async Task<string> RenderIndexAsync(IEnumerable<BlogPost> posts, CategoryNode categoryTree)
    {
        var templatePath = Path.Combine(configuration["BlogSettings:TemplatePath"] ?? "", "index.html");

        // 優先使用自定義模板，如果不存在則使用內建預設模板
        if (await HasCustomTemplateAsync("index"))
        {
            return await RenderCustomIndexAsync(posts, categoryTree, templatePath);
        }

        return RenderDefaultIndexHtml(posts, categoryTree);
    }

    /// <summary>
    /// 渲染文章頁 HTML：將 Markdown 轉換的 HTML 內容嵌入到模板中
    /// </summary>
    /// <param name="post">文章物件（包含標題、日期、分類等中繼資料）</param>
    /// <param name="htmlContent">已轉換為 HTML 的文章內容</param>
    /// <returns>完整的文章頁 HTML 字串</returns>
    public async Task<string> RenderPostAsync(BlogPost post, string htmlContent)
    {
        var templatePath = Path.Combine(configuration["BlogSettings:TemplatePath"] ?? "", "post.html");

        // 優先使用自定義模板，如果不存在則使用內建預設模板
        if (await HasCustomTemplateAsync("post"))
        {
            return await RenderCustomPostAsync(post, htmlContent, templatePath);
        }

        return RenderDefaultPostHtml(post, htmlContent);
    }

    /// <summary>
    /// 檢查指定名稱的自定義模板檔案是否存在
    /// </summary>
    /// <param name="templateName">模板名稱（不包含 .html 副檔名）</param>
    /// <returns>如果模板檔案存在則返回 true</returns>
    public Task<bool> HasCustomTemplateAsync(string templateName)
    {
        var templatePath = Path.Combine(configuration["BlogSettings:TemplatePath"] ?? "", $"{templateName}.html");
        return Task.FromResult(File.Exists(templatePath));
    }

    #endregion

    #region HTML 生成方法 - 處理分類、標籤和樹狀結構

    /// <summary>
    /// 生成分類的 Bootstrap HTML 標籤
    /// </summary>
    /// <param name="categories">分類列表</param>
    /// <returns>包含 Bootstrap badge 樣式的分類 HTML 字串</returns>
    /// <example>
    /// 輸入：["技術", "程式設計"]
    /// 輸出：&lt;span class="badge bg-primary category"&gt;技術&lt;/span&gt;&lt;span class="badge bg-primary category"&gt;程式設計&lt;/span&gt;
    /// </example>
    private string GenerateCategoriesHtml(List<string> categories)
    {
        if (categories.Count == 0) return string.Empty;

        return string.Join("", categories.Select(category =>
            $"<span class=\"badge bg-primary category\">{category}</span>"));
    }

    /// <summary>
    /// 生成標籤的 Bootstrap HTML 標籤
    /// </summary>
    /// <param name="tags">標籤列表</param>
    /// <returns>包含 Bootstrap badge 樣式的標籤 HTML 字串，以 # 開頭</returns>
    /// <example>
    /// 輸入：["C#", "ASP.NET"]
    /// 輸出：&lt;span class="badge bg-secondary tag"&gt;#C#&lt;/span&gt;&lt;span class="badge bg-secondary tag"&gt;#ASP.NET&lt;/span&gt;
    /// </example>
    private string GenerateTagsHtml(List<string> tags)
    {
        if (tags.Count == 0) return string.Empty;

        return string.Join("", tags.Select(tag =>
            $"<span class=\"badge bg-secondary tag\">#{tag}</span>"));
    }

    /// <summary>
    /// 遞歸生成分類節點的 HTML 結構
    /// </summary>
    /// <param name="node">當前分類節點</param>
    /// <param name="parentPath">父分類路徑（用於建立完整路徑）</param>
    /// <returns>分類節點及其子節點的 HTML 字串</returns>
    /// <remarks>
    /// 生成的 HTML 結構：
    /// - 每個分類項目包含展開/收合按鈕（如果有子分類）
    /// - 顯示文章數量的計數器
    /// - 支援點擊篩選功能的 data-category-path 屬性
    /// - 遞歸處理子分類，形成樹狀結構
    /// </remarks>
    private string GenerateCategoryNodeHtml(CategoryNode node, string parentPath)
    {
        var sb = new StringBuilder();

        foreach (var child in node.Children)
        {
            // 建立當前分類的完整路徑
            var currentPath = string.IsNullOrEmpty(parentPath) ? child.Name : $"{parentPath}/{child.Name}";
            var hasChildren = child.Children.Count > 0;

            sb.AppendLine("<div class=\"category-node\">");
            sb.AppendLine($"  <div class=\"category-item\" data-category-path=\"{currentPath}\">");

            // 根據是否有子分類顯示不同的圖示
            sb.AppendLine($"    <span class=\"category-name\">{(hasChildren ? "📁" : "📄")} {child.Name}</span>");

            // 如果有文章，顯示文章數量
            if (child.PostCount > 0)
            {
                sb.AppendLine($"    <span class=\"category-count\">{child.PostCount}</span>");
            }

            sb.AppendLine("  </div>");

            // 如果有子分類，遞歸生成子分類的 HTML
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

    /// <summary>
    /// 生成完整的分類樹狀結構 HTML
    /// </summary>
    /// <param name="categoryTree">分類樹的根節點</param>
    /// <returns>完整的分類樹 HTML 字串，如果沒有分類則返回提示訊息</returns>
    private string GenerateCategoryTreeHtml(CategoryNode categoryTree)
    {
        if (categoryTree.Children.Count == 0)
        {
            return "<p class=\"no-categories\">📝 尚無分類</p>";
        }

        return GenerateCategoryNodeHtml(categoryTree, "");
    }

    #endregion

    #region 模板語法處理 - 條件語法和循環語法

    /// <summary>
    /// 處理模板中的條件語法區塊（{{#if condition}}...{{/if}}）
    /// </summary>
    /// <param name="template">原始模板內容</param>
    /// <param name="post">文章物件</param>
    /// <returns>處理完條件語法的模板字串</returns>
    /// <remarks>
    /// 支援的條件語法：
    /// - {{#if Categories}} - 當文章有分類時顯示
    /// - {{#if Tags}} - 當文章有標籤時顯示  
    /// - {{#if Author}} - 當文章有作者資訊時顯示
    /// - {{#if FirstImage}} - 當文章有第一張圖片時顯示
    /// 
    /// 在條件區塊內，會自動替換對應的變數：
    /// - {{{Categories}}} -> HTML 格式的分類標籤
    /// - {{CategoriesPlain}} -> 純文字格式的分類
    /// </remarks>
    private string ProcessConditionalBlocks(string template, BlogPost post)
    {
        // 處理分類條件：檢查是否有分類
        template = ProcessIfBlock(template, "Categories", post.Categories.Count > 0,
            () => GenerateCategoriesHtml(post.Categories), // HTML 格式
            () => string.Join(" / ", post.Categories)); // 純文字格式

        // 處理標籤條件：檢查是否有標籤
        template = ProcessIfBlock(template, "Tags", post.Tags.Count > 0,
            () => GenerateTagsHtml(post.Tags), // HTML 格式（含 # 前綴）
            () => string.Join(" ", post.Tags.Select(t => $"#{t}"))); // 純文字格式

        // 處理作者條件：檢查是否有作者資訊
        template = ProcessIfBlock(template, "Author", !string.IsNullOrEmpty(post.Author),
            () => post.Author ?? string.Empty,
            () => post.Author ?? string.Empty);

        // 處理第一張圖片條件：檢查是否有圖片
        template = ProcessIfBlock(template, "FirstImage", !string.IsNullOrEmpty(post.FirstImageUrl),
            () => $"<img src=\"{post.FirstImageUrl}\" alt=\"{post.Title}\" class=\"post-thumbnail post-image\">",
            () => post.FirstImageUrl);

        return template;
    }

    /// <summary>
    /// 處理特定條件的 if 區塊
    /// </summary>
    /// <param name="template">模板內容</param>
    /// <param name="condition">條件名稱（如 "Categories"、"Tags"）</param>
    /// <param name="isTrue">條件是否為真</param>
    /// <param name="htmlValueProvider">HTML 格式值的提供者</param>
    /// <param name="plainValueProvider">純文字格式值的提供者</param>
    /// <returns>處理完該條件的模板字串</returns>
    /// <remarks>
    /// 使用正規表示式匹配 {{#if condition}}...{{/if}} 格式的條件區塊
    /// 如果條件為真，則替換區塊內的變數並保留內容
    /// 如果條件為假，則移除整個條件區塊
    /// </remarks>
    private string ProcessIfBlock(string template, string condition, bool isTrue,
        Func<string> htmlValueProvider, Func<string> plainValueProvider)
    {
        // 建立正規表示式模式，匹配條件區塊
        var pattern = $@"{{{{#if\s+{condition}}}}}(.*?){{{{/if}}}}";
        var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return regex.Replace(template, match =>
        {
            if (!isTrue) return string.Empty;
            var content = match.Groups[1].Value;

            // 在條件區塊內替換對應的變數
            // {{{變數}}} 用於 HTML 內容（不跳脫）
            // {{變數Plain}} 用於純文字內容（會跳脫）
            content = content.Replace($"{{{{{{{condition}}}}}}}", htmlValueProvider())
                .Replace($"{{{{{condition}Plain}}}}", plainValueProvider());

            return content;

            // 條件為假時，移除整個區塊
        });
    }

    /// <summary>
    /// 處理首頁模板中的文章列表循環（{{#each Posts}}...{{/each}}）
    /// </summary>
    /// <param name="template">模板內容</param>
    /// <param name="posts">文章列表</param>
    /// <returns>處理完循環的模板字串</returns>
    /// <remarks>
    /// 在循環區塊內，每篇文章都可以使用以下變數：
    /// - {{Title}} - 文章標題
    /// - {{HtmlFilePath}} - 文章 HTML 檔案路徑
    /// - {{PublishedDate}} - 發佈日期（yyyy-MM-dd）
    /// - {{PublishedDateLong}} - 發佈日期（yyyy年MM月dd日）
    /// - {{Author}} - 作者姓名
    /// - {{CategoriesPlain}} - 分類（純文字）
    /// - {{TagsPlain}} - 標籤（純文字）
    /// - {{{Categories}}} - 分類（HTML 格式）
    /// - 以及其他文章相關變數
    /// </remarks>
    private string ProcessEachBlock(string template, List<BlogPost> posts)
    {
        var pattern = @"{{#each\s+Posts}}(.*?){{/each}}";
        var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return regex.Replace(template, match =>
        {
            var postTemplate = match.Groups[1].Value;
            var postsHtml = new StringBuilder();

            // 為每篇文章渲染模板
            foreach (var post in posts)
            {
                var processedPost = RenderSinglePostInLoop(postTemplate, post);
                postsHtml.AppendLine(processedPost);
            }

            return postsHtml.ToString();
        });
    }

    #endregion

    #region 自定義模板渲染

    /// <summary>
    /// 使用自定義模板渲染首頁
    /// </summary>
    /// <param name="posts">文章列表</param>
    /// <param name="categoryTree">分類樹</param>
    /// <param name="templatePath">模板檔案路徑</param>
    /// <returns>渲染後的首頁 HTML</returns>
    private async Task<string> RenderCustomIndexAsync(IEnumerable<BlogPost> posts, CategoryNode categoryTree,
        string templatePath)
    {
        try
        {
            // 載入自定義模板檔案
            var template = await fileService.ReadFileAsync(templatePath);
            var postsList = posts.ToList();

            // 替換基本變數
            var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);
            template = template
                .Replace("{{PostCount}}", postsList.Count.ToString())
                .Replace("{{{CategoryTree}}}", categoryTreeHtml)
                .Replace("{{CategoryTree}}", categoryTreeHtml)
                .Replace("{{GeneratedDate}}", DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss"));

            // 處理文章列表循環
            template = ProcessEachBlock(template, postsList);

            return template;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "渲染自訂首頁模板失敗");
            throw;
        }
    }

    /// <summary>
    /// 使用自定義模板渲染文章頁
    /// </summary>
    /// <param name="post">文章物件</param>
    /// <param name="htmlContent">文章 HTML 內容</param>
    /// <param name="templatePath">模板檔案路徑</param>
    /// <returns>渲染後的文章頁 HTML</returns>
    private async Task<string> RenderCustomPostAsync(BlogPost post, string htmlContent, string templatePath)
    {
        try
        {
            // 載入自定義模板檔案
            var template = await fileService.ReadFileAsync(templatePath);

            // 替換基本變數
            template = template
                .Replace("{{Title}}", post.Title)
                .Replace("{{{Content}}}", htmlContent) // 使用三個大括號避免 HTML 跳脫
                .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
                .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
                .Replace("{{Slug}}", post.Slug)
                .Replace("{{FirstImageUrl}}", post.FirstImageUrl)
                .Replace("{{ImageCount}}", post.ImagePaths.Count.ToString());

            // 處理條件語法（分類、標籤、作者、圖片等）
            template = ProcessConditionalBlocks(template, post);

            return template;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "渲染自訂文章模板失敗");
            throw;
        }
    }

    #endregion

    #region 預設內建模板

    /// <summary>
    /// 生成預設的首頁 HTML（當沒有自定義模板時使用）
    /// </summary>
    /// <param name="posts">文章列表</param>
    /// <param name="categoryTree">分類樹</param>
    /// <returns>完整的首頁 HTML 字串</returns>
    /// <remarks>
    /// 預設首頁包含：
    /// - 響應式網格佈局（主內容區 + 側邊欄）
    /// - 文章卡片列表顯示
    /// - 分類樹狀導航
    /// - 內建 CSS 樣式
    /// - 支援行動裝置的響應式設計
    /// </remarks>
    private string RenderDefaultIndexHtml(IEnumerable<BlogPost> posts, CategoryNode categoryTree)
    {
        var postsList = posts.ToList();
        var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);

        // 生成文章列表 HTML
        var postsHtml = string.Join("", postsList.Select(post =>
        {
            var tagsHtml = post.Tags.Count > 0
                ? string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"))
                : "";

            var categoriesHtml = post.Categories.Count > 0
                ? string.Join("", post.Categories.Select(category => $"<span class=\"category\">{category}</span>"))
                : "";

            var categoriesPlain = post.Categories.Count > 0 ? string.Join("/", post.Categories) : "";

            var postSb = new StringBuilder();
            postSb.AppendLine($"    <article class=\"post-item\" data-categories=\"{categoriesPlain}\">");
            postSb.AppendLine($"        <h2><a href=\"{post.HtmlFilePath}\">{post.Title}</a></h2>");
            postSb.AppendLine("        <div class=\"post-meta\">");
            postSb.AppendLine($"            <span class=\"date\">{post.PublishedDate:yyyy-MM-dd}</span>");

            if (post.Categories.Count > 0)
            {
                postSb.AppendLine($"            <div class=\"categories\">{categoriesHtml}</div>");
            }

            if (post.Tags.Count > 0)
            {
                postSb.AppendLine($"            <div class=\"tags\">{tagsHtml}</div>");
            }

            postSb.AppendLine("        </div>");
            postSb.AppendLine("    </article>");

            return postSb.ToString();
        }));

        // 使用 StringBuilder 構建完整的 HTML 文件
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-TW\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <title>LEOSHIANG 的部落格</title>");
        sb.AppendLine("    <style>");

        // 內建 CSS 樣式（省略詳細樣式代碼以節省空間）
        sb.AppendLine("        /* 內建預設 CSS 樣式 */");
        sb.AppendLine(
            "        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; margin: 0; padding: 20px; background-color: #f8f9fa; }");
        // ... 其他 CSS 樣式

        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine("        <main class=\"main-content\">");
        sb.AppendLine("            <header class=\"blog-header\">");
        sb.AppendLine("                <h1>LEOSHIANG 的部落格</h1>");
        sb.AppendLine("                <div class=\"blog-stats\">");
        sb.AppendLine($"                    <span>📝 文章總數: {postsList.Count}</span>");
        sb.AppendLine("                </div>");
        sb.AppendLine("            </header>");
        sb.AppendLine("            <div class=\"posts\" id=\"posts-container\">");
        sb.AppendLine(postsHtml);
        sb.AppendLine("            </div>");
        sb.AppendLine("        </main>");
        sb.AppendLine("        <aside class=\"sidebar\">");
        sb.AppendLine("            <div class=\"category-tree\">");
        sb.AppendLine("                <h3>📁 分類目錄</h3>");
        sb.AppendLine(categoryTreeHtml);
        sb.AppendLine("            </div>");
        sb.AppendLine("        </aside>");
        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// 生成預設的文章頁 HTML（當沒有自定義模板時使用）
    /// </summary>
    /// <param name="post">文章物件</param>
    /// <param name="htmlContent">文章 HTML 內容</param>
    /// <returns>完整的文章頁 HTML 字串</returns>
    private string RenderDefaultPostHtml(BlogPost post, string htmlContent)
    {
        var tagsHtml = post.Tags.Count > 0
            ? $"<div class=\"tags\">{string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"))}</div>"
            : "";

        var categoriesHtml = post.Categories.Count > 0
            ? $"<div class=\"categories\">{string.Join("", post.Categories.Select(cat => $"<span class=\"category\">{cat}</span>"))}</div>"
            : "";

        // 使用 StringBuilder 構建完整的 HTML 文件
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-TW\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>{post.Title}</title>");
        sb.AppendLine("    <style>");
        // 內建 CSS 樣式
        sb.AppendLine(
            "        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; margin: 0; padding: 20px; }");
        // ... 其他 CSS 樣式
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("    <div class=\"container\">");
        sb.AppendLine("        <article>");
        sb.AppendLine($"            <h1>{post.Title}</h1>");
        sb.AppendLine("            <div class=\"post-meta\">");
        sb.AppendLine($"                <span class=\"date\">{post.PublishedDate:yyyy-MM-dd}</span>");
        sb.AppendLine($"                {categoriesHtml}");
        sb.AppendLine($"                {tagsHtml}");
        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"content\">");
        sb.AppendLine($"                {htmlContent}");
        sb.AppendLine("            </div>");
        sb.AppendLine("        </article>");
        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    #endregion

    #region 輔助方法

    /// <summary>
    /// 在文章列表循環中渲染單篇文章的模板
    /// </summary>
    /// <param name="postTemplate">文章的模板片段</param>
    /// <param name="post">文章物件</param>
    /// <returns>處理完變數替換的單篇文章 HTML</returns>
    /// <remarks>
    /// 這個方法專門用於處理 {{#each Posts}} 循環內的每篇文章
    /// 會替換所有可用的文章變數，並處理條件語法
    /// </remarks>
    private string RenderSinglePostInLoop(string postTemplate, BlogPost post)
    {
        var processed = postTemplate;

        // 替換基本文章變數
        processed = processed
            .Replace("{{Title}}", post.Title)
            .Replace("{{HtmlFilePath}}", post.HtmlFilePath)
            .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
            .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
            .Replace("{{Author}}", post.Author ?? string.Empty)
            .Replace("{{CategoriesPlain}}", string.Join(" / ", post.Categories))
            .Replace("{{TagsPlain}}", string.Join(" ", post.Tags.Select(t => $"#{t}")))
            .Replace("{{FirstImageUrl}}", post.FirstImageUrl)
            .Replace("{{ImageCount}}", post.ImagePaths.Count.ToString())
            .Replace("{{Slug}}", post.Slug);

        // 處理條件語法（在循環內同樣支援條件判斷）
        processed = ProcessConditionalBlocks(processed, post);

        return processed;
    }

    #endregion
}