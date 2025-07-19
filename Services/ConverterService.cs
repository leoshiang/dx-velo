using Microsoft.Extensions.Logging;

namespace Velo.Services;

public class ConverterService(
    IBlogService blogService,
    IMarkdownToHtmlService markdownToHtmlService,
    ILogger<ConverterService> logger)
{
    public async Task ConvertAllPostsAsync()
    {
        try
        {
            logger.LogInformation("開始掃描文章...");

            // 強制重新掃描所有文章
            await blogService.ForceScanAndReloadPostsAsync();

            logger.LogInformation("開始轉換 Markdown 文章為 HTML...");

            // 轉換所有文章為 HTML
            await markdownToHtmlService.ConvertAndSaveAllPostsAsync();

            logger.LogInformation("所有轉換作業已完成！");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換過程中發生錯誤");
            throw;
        }
    }
}