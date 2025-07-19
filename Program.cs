using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velo.Services;

namespace Velo;

class Program
{
    private static Task ClearOutputDirectoryAsync(string outputPath, ILogger logger)
    {
        try
        {
            // 取得目錄中的所有檔案和子目錄
            var directoryInfo = new DirectoryInfo(outputPath);

            // 刪除所有檔案
            foreach (var file in directoryInfo.GetFiles())
            {
                try
                {
                    file.Delete();
                    logger.LogDebug("已刪除檔案: {FilePath}", file.FullName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "無法刪除檔案: {FilePath}", file.FullName);
                }
            }

            // 刪除所有子目錄
            foreach (var directory in directoryInfo.GetDirectories())
            {
                try
                {
                    directory.Delete(true); // 遞歸刪除
                    logger.LogDebug("已刪除目錄: {DirectoryPath}", directory.FullName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "無法刪除目錄: {DirectoryPath}", directory.FullName);
                }
            }

            logger.LogInformation("輸出目錄清除完成，已刪除 {FileCount} 個檔案和 {DirectoryCount} 個目錄",
                directoryInfo.GetFiles().Length, directoryInfo.GetDirectories().Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清除輸出目錄時發生錯誤: {OutputPath}", outputPath);
            throw;
        }

        return Task.CompletedTask;
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Velo Markdown 轉換器啟動");
        Console.WriteLine("================================");

        // 建立設定
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("Velo.config.json", optional: false, reloadOnChange: true)
            .Build();

        // 建立主機設定
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // 清除預設的設定來源
                config.Sources.Clear();

                // 只加載我們自訂的設定檔
                config.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("Velo.config.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // 移除預設的 appsettings.json 設定
                services.Configure<ConsoleLifetimeOptions>(options => { options.SuppressStatusMessages = true; });

                // 註冊設定
                services.AddSingleton<IConfiguration>(configuration);

                // 註冊服務
                services.AddScoped<IBlogService, BlogService>();
                services.AddScoped<ITemplateService, TemplateService>();
                services.AddScoped<IMarkdownToHtmlService, MarkdownToHtmlConverter>();
                services.AddScoped<ConverterService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .UseConsoleLifetime()
            .Build();

        // 取得服務和 logger
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var converterService = scope.ServiceProvider.GetRequiredService<ConverterService>();

        try
        {
            // 顯示設定資訊
            var contentPath = configuration["BlogContentPath"];
            var outputPath = configuration["BlogSettings:HtmlOutputPath"];
            var imagePath = configuration["BlogSettings:ImageOutputPath"];
            var templatePath = configuration["BlogSettings:TemplatePath"];
            var clearOutputDirectory = configuration.GetValue<bool>("BlogSettings:ClearOutputDirectoryOnStart", false);

            Console.WriteLine($"內容目錄: {contentPath}");
            Console.WriteLine($"HTML 輸出目錄: {outputPath}");
            Console.WriteLine($"圖片輸出目錄: {imagePath}");
            Console.WriteLine($"模板目錄: {templatePath}");
            Console.WriteLine($"清除輸出目錄: {(clearOutputDirectory ? "是" : "否")}");
            Console.WriteLine();

            // 檢查設定檔路徑
            if (string.IsNullOrEmpty(contentPath) || string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("錯誤: 請檢查 Velo.config.json 設定檔中的路徑設定");
                Console.WriteLine("請確保 BlogContentPath 和 BlogSettings:HtmlOutputPath 已正確設定");
                Environment.Exit(1);
            }

            // 檢查目錄是否存在
            if (!Directory.Exists(contentPath))
            {
                Console.WriteLine($"錯誤: 內容目錄不存在: {contentPath}");
                Environment.Exit(1);
            }

            // 處理輸出目錄
            if (clearOutputDirectory && Directory.Exists(outputPath))
            {
                Console.WriteLine($"正在清除輸出目錄: {outputPath}");
                logger.LogInformation("開始清除輸出目錄: {OutputPath}", outputPath);

                await ClearOutputDirectoryAsync(outputPath, logger);

                Console.WriteLine("輸出目錄已清除");
                logger.LogInformation("輸出目錄清除完成");
            }

            // 確保輸出目錄存在
            if (!Directory.Exists(outputPath))
            {
                Console.WriteLine($"建立輸出目錄: {outputPath}");
                Directory.CreateDirectory(outputPath);
            }

            // 確保圖片輸出目錄存在
            if (!string.IsNullOrEmpty(imagePath) && !Directory.Exists(imagePath))
            {
                Console.WriteLine($"建立圖片輸出目錄: {imagePath}");
                Directory.CreateDirectory(imagePath);
            }

            logger.LogInformation("開始執行轉換作業...");

            // 執行轉換
            await converterService.ConvertAllPostsAsync();

            logger.LogInformation("轉換作業已完成！");
            Console.WriteLine();
            Console.WriteLine("轉換作業已完成！");
            Console.WriteLine($"請查看輸出目錄: {outputPath}");
        }
        catch (FileNotFoundException ex) when (ex.Message.Contains("Velo.config.json"))
        {
            Console.WriteLine();
            Console.WriteLine("錯誤: 找不到設定檔 Velo.config.json");
            Console.WriteLine("請確保 Velo.config.json 檔案存在於程式目錄中");
            Console.WriteLine("您可以參考 Velo.config.example.json 建立設定檔");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "轉換過程中發生錯誤");
            Console.WriteLine();
            Console.WriteLine("轉換過程中發生錯誤:");
            Console.WriteLine(ex.Message);

            // 如果是設定相關的錯誤，提供更詳細的說明
            if (ex.Message.Contains("configuration") || ex.Message.Contains("設定"))
            {
                Console.WriteLine();
                Console.WriteLine("請檢查 Velo.config.json 設定檔:");
                Console.WriteLine("- 確保 JSON 格式正確");
                Console.WriteLine("- 確保所有必要的設定項目存在");
                Console.WriteLine("- 確保路徑設定正確且可存取");
            }

            Environment.Exit(1);
        }

        Console.WriteLine();
        Console.WriteLine("程式執行完畢");
    }
}