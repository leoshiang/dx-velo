using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;

namespace Velo.Services;

public class TemplateService(
    IConfiguration configuration,
    IFileService fileService,
    ILogger<TemplateService> logger) : ITemplateService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly IFileService _fileService = fileService;
    private readonly ILogger<TemplateService> _logger = logger;

    public Task<bool> HasCustomTemplateAsync(string templateName)
    {
        var templatePath = Path.Combine(_configuration["BlogSettings:TemplatePath"] ?? "", $"{templateName}.html");
        return Task.FromResult(File.Exists(templatePath));
    }

    public async Task<string> RenderIndexAsync(IEnumerable<BlogPost> posts, CategoryNode categoryTree)
    {
        var templatePath = Path.Combine(_configuration["BlogSettings:TemplatePath"] ?? "", "index.html");

        if (await HasCustomTemplateAsync("index"))
        {
            return await RenderCustomIndexAsync(posts, categoryTree, templatePath);
        }

        return RenderDefaultIndexHtml(posts, categoryTree);
    }

    public async Task<string> RenderPostAsync(BlogPost post, string htmlContent)
    {
        var templatePath = Path.Combine(_configuration["BlogSettings:TemplatePath"] ?? "", "post.html");

        if (await HasCustomTemplateAsync("post"))
        {
            return await RenderCustomPostAsync(post, htmlContent, templatePath);
        }

        return RenderDefaultPostHtml(post, htmlContent);
    }

    private string GenerateCategoriesHtml(List<string> categories)
    {
        if (categories.Count == 0) return string.Empty;

        return string.Join("", categories.Select(category =>
            $"<span class=\"category\">{category}</span>"));
    }

    private string GenerateTagsHtml(List<string> tags)
    {
        if (tags.Count == 0) return string.Empty;

        return string.Join("", tags.Select(tag =>
            $"<span class=\"tag\">#{tag}</span>"));
    }

    private string GenerateCategoryNodeHtml(CategoryNode node, string parentPath)
    {
        var sb = new StringBuilder();

        foreach (var child in node.Children)
        {
            var currentPath = string.IsNullOrEmpty(parentPath) ? child.Name : $"{parentPath}/{child.Name}";
            var hasChildren = child.Children.Count > 0;

            sb.AppendLine("<div class=\"category-node\">");
            sb.AppendLine($"  <div class=\"category-item\" data-category-path=\"{currentPath}\">");
            sb.AppendLine($"    <span class=\"category-name\">{(hasChildren ? "📁" : "📄")} {child.Name}</span>");

            if (child.PostCount > 0)
            {
                sb.AppendLine($"    <span class=\"category-count\">{child.PostCount}</span>");
            }

            sb.AppendLine("  </div>");

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
        if (categoryTree.Children.Count == 0)
        {
            return "<p class=\"no-categories\">📝 尚無分類</p>";
        }

        return GenerateCategoryNodeHtml(categoryTree, "");
    }

    private string ProcessTemplateVariables(string template, List<BlogPost> posts, string categoryTreeHtml)
    {
        var postsJson = System.Text.Json.JsonSerializer.Serialize(posts.Select(p => new
        {
            Title = p.Title,
            HtmlFilePath = p.HtmlFilePath,
            PublishedDate = p.PublishedDate.ToString("yyyy-MM-dd"),
            PublishedDateLong = p.PublishedDate.ToString("yyyy年MM月dd日"),
            Author = p.Author ?? string.Empty,
            Categories = GenerateCategoriesHtml(p.Categories),
            CategoriesPlain = string.Join(" / ", p.Categories),
            Tags = GenerateTagsHtml(p.Tags),
            TagsPlain = string.Join(" ", p.Tags.Select(t => $"#{t}")),
            FirstImageUrl = p.FirstImageUrl,
            ImageCount = p.ImagePaths.Count,
            Slug = p.Slug
        }));

        return template
            .Replace("{{PostCount}}", posts.Count.ToString())
            .Replace("{{{CategoryTree}}}", categoryTreeHtml)
            .Replace("{{CategoryTree}}", categoryTreeHtml)
            .Replace("{{GeneratedDate}}", DateTime.Now.ToString("yyyy年MM月dd日 HH:mm:ss"))
            .Replace("{{PostsJSON}}", postsJson);
    }

    private async Task<string> RenderCustomIndexAsync(IEnumerable<BlogPost> posts, CategoryNode categoryTree,
        string templatePath)
    {
        try
        {
            var template = await _fileService.ReadFileAsync(templatePath);
            var postsList = posts.ToList();

            var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);
            var processedTemplate = ProcessTemplateVariables(template, postsList, categoryTreeHtml);

            // 如果模板包含 Handlebars 循環語法，處理 Posts 循環
            if (processedTemplate.Contains("{{#each Posts}}"))
            {
                var postsHtml = string.Join("", postsList.Select(post =>
                {
                    var postTemplate = ExtractPostTemplate(processedTemplate);
                    return RenderSinglePostInLoop(postTemplate, post);
                }));

                processedTemplate = processedTemplate.Replace(
                    System.Text.RegularExpressions.Regex.Match(processedTemplate, @"{{#each Posts}}.*?{{/each}}",
                        System.Text.RegularExpressions.RegexOptions.Singleline).Value,
                    postsHtml);
            }

            return processedTemplate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "渲染自訂首頁模板失敗");
            throw;
        }
    }

    private string ExtractPostTemplate(string template)
    {
        var match = System.Text.RegularExpressions.Regex.Match(template,
            @"{{#each Posts}}(.*?){{/each}}",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return match.Success ? match.Groups[1].Value : "";
    }

    private string RenderSinglePostInLoop(string postTemplate, BlogPost post)
    {
        return postTemplate
            .Replace("{{Title}}", post.Title)
            .Replace("{{HtmlFilePath}}", post.HtmlFilePath)
            .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
            .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
            .Replace("{{Author}}", post.Author ?? string.Empty)
            .Replace("{{{Categories}}}", GenerateCategoriesHtml(post.Categories))
            .Replace("{{CategoriesPlain}}", string.Join(" / ", post.Categories))
            .Replace("{{{Tags}}}", GenerateTagsHtml(post.Tags))
            .Replace("{{TagsPlain}}", string.Join(" ", post.Tags.Select(t => $"#{t}")))
            .Replace("{{FirstImageUrl}}", post.FirstImageUrl)
            .Replace("{{ImageCount}}", post.ImagePaths.Count.ToString())
            .Replace("{{Slug}}", post.Slug);
    }

    private async Task<string> RenderCustomPostAsync(BlogPost post, string htmlContent, string templatePath)
    {
        try
        {
            var template = await _fileService.ReadFileAsync(templatePath);

            var tagsHtml = post.Tags.Count > 0
                ? string.Join("．", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"))
                : string.Empty;

            var categoriesHtml = post.Categories.Count > 0
                ? string.Join("", post.Categories.Select(category => $"<span class=\"category\">{category}</span>"))
                : string.Empty;

            return template
                .Replace("{{Title}}", post.Title)
                .Replace("{{{Content}}}", htmlContent)
                .Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"))
                .Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"))
                .Replace("{{Slug}}", post.Slug)
                .Replace("{{FirstImageUrl}}", post.FirstImageUrl)
                .Replace("{{{Tags}}}", tagsHtml)
                .Replace("{{TagsPlain}}", post.Tags.Count > 0 ? string.Join(", ", post.Tags) : "")
                .Replace("{{{Categories}}}", categoriesHtml)
                .Replace("{{CategoriesPlain}}", post.Categories.Count > 0 ? string.Join(", ", post.Categories) : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "渲染自訂文章模板失敗");
            throw;
        }
    }

    private string RenderDefaultIndexHtml(IEnumerable<BlogPost> posts, CategoryNode categoryTree)
    {
        var postsList = posts.ToList();
        var categoryTreeHtml = GenerateCategoryTreeHtml(categoryTree);

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

        // 使用 StringBuilder 構建完整的 HTML
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-TW\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("    <title>LEOSHIANG 的部落格</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine("        /* CSS 樣式 */");
        sb.AppendLine(
            "        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; margin: 0; padding: 20px; background-color: #f8f9fa; }");
        sb.AppendLine(
            "        .container { max-width: 1200px; margin: 0 auto; display: grid; grid-template-columns: 1fr 300px; gap: 40px; }");
        sb.AppendLine(
            "        .main-content { background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }");
        sb.AppendLine(
            "        .sidebar { background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); height: fit-content; position: sticky; top: 20px; }");
        sb.AppendLine("        .blog-header { margin-bottom: 30px; text-align: center; }");
        sb.AppendLine("        .blog-header h1 { color: #333; margin-bottom: 10px; }");
        sb.AppendLine("        .blog-stats { color: #666; font-size: 0.9rem; }");
        sb.AppendLine(
            "        .post-item { margin-bottom: 30px; padding-bottom: 20px; border-bottom: 1px solid #e9ecef; }");
        sb.AppendLine("        .post-item:last-child { border-bottom: none; }");
        sb.AppendLine("        .post-item h2 { margin-bottom: 10px; font-size: 1.5rem; }");
        sb.AppendLine("        .post-item h2 a { color: #333; text-decoration: none; transition: color 0.3s ease; }");
        sb.AppendLine("        .post-item h2 a:hover { color: #007bff; }");
        sb.AppendLine(
            "        .post-meta { color: #666; font-size: 0.9rem; display: flex; flex-wrap: wrap; gap: 15px; align-items: center; }");
        sb.AppendLine(
            "        .tag, .category { background: #007bff; color: white; padding: 3px 8px; border-radius: 12px; margin-right: 5px; font-size: 0.8rem; }");
        sb.AppendLine("        .category { background: #28a745; }");
        sb.AppendLine(
            "        .category-tree h3 { margin-top: 0; margin-bottom: 15px; color: #333; font-size: 1.2rem; border-bottom: 2px solid #007bff; padding-bottom: 8px; }");
        sb.AppendLine("        .category-node { margin-bottom: 5px; }");
        sb.AppendLine(
            "        .category-item { display: flex; align-items: center; justify-content: space-between; padding: 8px 10px; color: #555; font-size: 0.9rem; cursor: pointer; transition: background-color 0.2s ease; border-radius: 4px; }");
        sb.AppendLine("        .category-item:hover { background-color: #f8f9fa; }");
        sb.AppendLine("        .category-name { font-weight: 500; display: flex; align-items: center; gap: 5px; }");
        sb.AppendLine(
            "        .category-count { background: #007bff; color: white; padding: 2px 6px; border-radius: 10px; font-size: 0.8rem; }");
        sb.AppendLine("        .no-categories { color: #666; font-style: italic; }");
        sb.AppendLine("        @media (max-width: 768px) {");
        sb.AppendLine("            .container { grid-template-columns: 1fr; gap: 20px; }");
        sb.AppendLine("            .sidebar { position: static; order: -1; }");
        sb.AppendLine("            body { padding: 10px; }");
        sb.AppendLine("        }");
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

    private string RenderDefaultPostHtml(BlogPost post, string htmlContent)
    {
        var tagsHtml = post.Tags.Count > 0
            ? $"<div class=\"tags\">{string.Join("", post.Tags.Select(tag => $"<span class=\"tag\">{tag}</span>"))}</div>"
            : "";

        var categoriesHtml = post.Categories.Count > 0
            ? $"<div class=\"categories\">{string.Join("", post.Categories.Select(cat => $"<span class=\"category\">{cat}</span>"))}</div>"
            : "";

        // 使用 StringBuilder 避免字串插值問題
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-TW\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>{post.Title}</title>");
        sb.AppendLine("    <style>");
        sb.AppendLine(
            "        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; line-height: 1.6; margin: 0; padding: 20px; }");
        sb.AppendLine("        .container { max-width: 800px; margin: 0 auto; }");
        sb.AppendLine("        .post-meta { color: #666; margin-bottom: 20px; }");
        sb.AppendLine(
            "        .tag, .category { background: #007bff; color: white; padding: 3px 8px; border-radius: 12px; margin-right: 5px; font-size: 0.8rem; }");
        sb.AppendLine("        .category { background: #28a745; }");
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
}