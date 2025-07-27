using Microsoft.Extensions.Logging;
using Velo.Models;

namespace Velo.Services;

public class ConverterService(
    IBlogService blogService,
    IMarkdownToHtmlService markdownToHtmlService,
    ITemplateService templateService,
    ILogger<ConverterService> logger)
{
    private readonly IBlogService _blogService = blogService;
    private readonly ILogger<ConverterService> _logger = logger;
    private readonly IMarkdownToHtmlService _markdownToHtmlService = markdownToHtmlService;
    private readonly ITemplateService _templateService = templateService;

    public async Task ConvertAllPostsAsync()
    {
        try
        {
            _logger.LogInformation("開始轉換流程...");

            // 1. 重新載入文章
            await _blogService.ForceScanAndReloadPostsAsync();

            // 2. 轉換所有文章
            await _markdownToHtmlService.ConvertAndSaveAllPostsAsync();

            _logger.LogInformation("轉換流程完成！");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "轉換流程發生錯誤");
            throw;
        }
    }

    public async Task<ConversionResult> ConvertSinglePostAsync(string slug)
    {
        try
        {
            var post = await _blogService.GetPostBySlugAsync(slug);
            if (post == null)
            {
                return new ConversionResult { Success = false, Message = "找不到指定的文章" };
            }

            var htmlContent = await _markdownToHtmlService.ConvertToHtmlAsync(post.ContentHtml, post.SourceFilePath);
            var finalHtml = await _templateService.RenderPostAsync(post, htmlContent);

            return new ConversionResult
            {
                Success = true,
                Message = "轉換成功",
                HtmlContent = finalHtml,
                Post = post
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "轉換單篇文章失敗: {Slug}", slug);
            return new ConversionResult { Success = false, Message = ex.Message };
        }
    }
}

public class ConversionResult
{
    public string? HtmlContent { get; set; }
    public string Message { get; set; } = string.Empty;
    public BlogPost? Post { get; set; }
    public bool Success { get; set; }
}