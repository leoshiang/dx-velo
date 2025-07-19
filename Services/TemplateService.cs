using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Velo.Models;

namespace Velo.Services;

public class TemplateService(IConfiguration configuration) : ITemplateService
{
    private readonly string _templatePath = configuration["BlogSettings:TemplatePath"] ??
                                            Path.Combine(Directory.GetCurrentDirectory(), "templates");

    public async Task<string> LoadTemplateAsync(string templateName)
    {
        var templateFilePath = Path.Combine(_templatePath, templateName);

        if (!File.Exists(templateFilePath))
        {
            throw new FileNotFoundException($"模板檔案不存在: {templateFilePath}");
        }

        return await File.ReadAllTextAsync(templateFilePath, Encoding.UTF8);
    }

    public Task<string> RenderIndexAsync(string templateContent, IEnumerable<BlogPost> posts)
    {
        var result = templateContent;

        // 替換基本資訊
        var blogPosts = posts.ToList();
        result = result.Replace("{{PostCount}}", blogPosts.Count().ToString());
        result = result.Replace("{{GeneratedDate}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        // 處理文章列表
        result = ProcessPostList(result, blogPosts);

        return Task.FromResult(result);
    }

    public Task<string> RenderPostAsync(string templateContent, BlogPost post, string htmlContent)
    {
        var result = templateContent;

        // 替換基本資訊
        result = result.Replace("{{Title}}", post.Title);
        result = result.Replace("{{Content}}", htmlContent);
        result = result.Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"));
        result = result.Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"));
        result = result.Replace("{{Slug}}", post.Slug);

        // 作者資訊
        result = result.Replace("{{Author}}", post.Author ?? string.Empty);

        // 替換分類
        if (post.Categories.Any())
        {
            var categoriesHtml = string.Join(" / ",
                post.Categories.Select(c => $"<span class=\"badge bg-primary\">{c}</span>"));
            result = result.Replace("{{Categories}}", categoriesHtml);
            result = result.Replace("{{CategoriesPlain}}", string.Join(" / ", post.Categories));
        }
        else
        {
            result = result.Replace("{{Categories}}", "");
            result = result.Replace("{{CategoriesPlain}}", "");
        }

        // 替換標籤
        if (post.Tags.Any())
        {
            var tagsHtml = string.Join(" ",
                post.Tags.Select(t => $"<span class=\"badge bg-secondary\">#{t}</span>"));
            result = result.Replace("{{Tags}}", tagsHtml);
            result = result.Replace("{{TagsPlain}}", string.Join(" ", post.Tags.Select(t => $"#{t}")));
        }
        else
        {
            result = result.Replace("{{Tags}}", "");
            result = result.Replace("{{TagsPlain}}", "");
        }

        // 處理條件性區塊
        result = ProcessConditionalBlocks(result, post);

        return Task.FromResult(result);
    }

    public Task<bool> TemplateExistsAsync(string templateName)
    {
        var templateFilePath = Path.Combine(_templatePath, templateName);
        return Task.FromResult(File.Exists(templateFilePath));
    }

    private string ProcessConditionalBlocks(string content, BlogPost post)
    {
        // 處理 {{#if Categories}} ... {{/if}} 區塊
        var categoriesPattern = @"{{#if Categories}}(.*?){{/if}}";
        content = Regex.Replace(content, categoriesPattern,
            match => post.Categories.Any() ? match.Groups[1].Value : "", RegexOptions.Singleline);

        // 處理 {{#if Tags}} ... {{/if}} 區塊
        var tagsPattern = @"{{#if Tags}}(.*?){{/if}}";
        content = Regex.Replace(content, tagsPattern,
            match => post.Tags.Any() ? match.Groups[1].Value : "", RegexOptions.Singleline);

        // 處理 {{#if Author}} ... {{/if}} 區塊
        var authorPattern = @"{{#if Author}}(.*?){{/if}}";
        content = Regex.Replace(content, authorPattern,
            match => !string.IsNullOrEmpty(post.Author) ? match.Groups[1].Value : "",
            RegexOptions.Singleline);

        return content;
    }

    private string ProcessPostList(string content, IEnumerable<BlogPost> posts)
    {
        // 處理 {{#each Posts}} ... {{/each}} 區塊
        var postListPattern = @"{{#each Posts}}(.*?){{/each}}";
        content = Regex.Replace(content, postListPattern, match =>
        {
            var itemTemplate = match.Groups[1].Value;
            var result = new StringBuilder();

            foreach (var post in posts.OrderByDescending(p => p.PublishedDate))
            {
                var item = itemTemplate;
                item = item.Replace("{{Title}}", post.Title);
                item = item.Replace("{{Slug}}", post.Slug);
                item = item.Replace("{{PublishedDate}}", post.PublishedDate.ToString("yyyy-MM-dd"));
                item = item.Replace("{{PublishedDateLong}}", post.PublishedDate.ToString("yyyy年MM月dd日"));
                item = item.Replace("{{Author}}", post.Author ?? string.Empty);

                item = item.Replace("{{CategoriesPlain}}",
                    post.Categories.Any() ? string.Join(" / ", post.Categories) : "");

                item = item.Replace("{{TagsPlain}}",
                    post.Tags.Any() ? string.Join(" ", post.Tags.Select(t => $"#{t}")) : "");

                result.AppendLine(item);
            }

            return result.ToString();
        }, RegexOptions.Singleline);

        return content;
    }
}