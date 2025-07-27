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
            logger.LogInformation("開始轉換流程...");

            // 1. 重新載入文章
            await blogService.ForceScanAndReloadPostsAsync();

            // 2. 轉換所有文章
            await markdownToHtmlService.ConvertAndSaveAllPostsAsync();

            logger.LogInformation("轉換流程完成！");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換流程發生錯誤");
            throw;
        }
    }
}