using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velo.Services;

// 引入 System.CommandLine
// 引入 System.CommandLine.Parsing

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

    /// <summary>
    /// 取得執行檔所在的目錄路徑
    /// </summary>
    private static string GetExecutableDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (string.IsNullOrEmpty(assemblyLocation))
        {
            // 在某些情況下（如 PublishSingleFile），Assembly.Location 可能為空
            // 這時使用 AppContext.BaseDirectory
            return AppContext.BaseDirectory;
        }

        return Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
    }

    static async Task Main(string[] args)
    {
        // 1. 定義命令列選項
        var configFileOption = new Option<string?>(
            ["--config", "-f"],
            description: "設定檔名稱 (預設: velo.config.json)",
            getDefaultValue: () => "velo.config.json"
        );
        var contentPathOption = new Option<string?>(
            ["--content-path", "-c"],
            description: "Markdown 文章檔案存放目錄"
        );
        var outputPathOption = new Option<string?>(
            ["--output-path", "-o"],
            description: "生成的 HTML 檔案輸出目錄"
        );
        var templatePathOption = new Option<string?>(
            ["--template-path", "-t"],
            description: "模板檔案存放目錄"
        );
        var imagePathOption = new Option<string?>(
            ["--image-path", "-i"],
            description: "圖片資源輸出目錄"
        );
        var clearOutputOption = new Option<bool?>(
            ["--clear-output", "-C"], // 大寫 C 以區分 content
            description: "生成前是否清空輸出目錄"
        );
        var autoYamlOption = new Option<bool?>(
            ["--auto-yaml", "-a"],
            description: "自動為缺少 Front Matter 的檔案添加"
        );
        var autoSaveModifiedOption = new Option<bool?>(
            ["--save-modified", "-s"],
            description: "自動儲存修改"
        );

        // 2. 建立根命令並加入選項
        var rootCommand = new RootCommand("Velo Markdown 轉換器：一個簡單但功能豐富的靜態部落格生成器。")
        {
            configFileOption,
            contentPathOption,
            outputPathOption,
            templatePathOption,
            imagePathOption,
            clearOutputOption,
            autoYamlOption,
            autoSaveModifiedOption
        };

        // 3. 設定命令處理器
        rootCommand.SetHandler(
            async (configFile, contentPath, outputPath, templatePath, imagePath, clearOutput, autoYaml, autoSave) =>
            {
                var executableDirectory = GetExecutableDirectory();

                // 使用參數指定的設定檔名稱，如果沒有指定則使用預設值
                var configFileName = configFile ?? "velo.config.json";
                var configFilePath = Path.Combine(executableDirectory, configFileName);

                Console.WriteLine("Velo Markdown 轉換器啟動");
                Console.WriteLine("================================");
                Console.WriteLine($"執行檔目錄: {executableDirectory}");
                Console.WriteLine($"設定檔名稱: {configFileName}");
                Console.WriteLine($"設定檔路徑: {configFilePath}");
                Console.WriteLine();

                // 檢查設定檔是否存在
                if (!File.Exists(configFilePath))
                {
                    Console.WriteLine($"錯誤: 找不到設定檔 {configFileName}");
                    Console.WriteLine($"請確保設定檔存在於執行檔目錄中: {executableDirectory}");
                    Console.WriteLine($"您可以參考 velo.config.json.example 建立設定檔");
                    Environment.Exit(1);
                }

                // 建立主機設定
                var host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        // 清除預設的設定來源，以便完全控制
                        config.Sources.Clear();

                        // 使用指定的設定檔
                        config.SetBasePath(executableDirectory)
                            .AddJsonFile(configFileName, optional: false, reloadOnChange: true);

                        // 建立一個字典來儲存從命令列解析出的參數，它們將覆寫設定檔中的值
                        var commandLineOverrides = new Dictionary<string, string>();

                        // 如果命令列選項有提供值（即不為 null），則加入到覆寫字典中
                        if (contentPath != null) commandLineOverrides["BlogContentPath"] = contentPath;
                        if (outputPath != null) commandLineOverrides["BlogSettings:HtmlOutputPath"] = outputPath;
                        if (templatePath != null) commandLineOverrides["BlogSettings:TemplatePath"] = templatePath;
                        if (imagePath != null) commandLineOverrides["BlogSettings:ImageOutputPath"] = imagePath;
                        // 對於 bool? 型別，檢查 HasValue 以判斷是否明確提供了參數
                        if (clearOutput.HasValue)
                            commandLineOverrides["BlogSettings:ClearOutputDirectoryOnStart"] =
                                clearOutput.Value.ToString();
                        if (autoYaml.HasValue)
                            commandLineOverrides["BlogSettings:AutoAddYamlHeader"] = autoYaml.Value.ToString();
                        if (autoSave.HasValue)
                            commandLineOverrides["BlogSettings:AutoSaveModified"] = autoSave.Value.ToString();

                        // 將命令列參數作為 In-Memory Collection 加入，確保其優先級最高
                        if (commandLineOverrides.Any())
                        {
                            config.AddInMemoryCollection(commandLineOverrides);
                        }
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.Configure<ConsoleLifetimeOptions>(options =>
                        {
                            options.SuppressStatusMessages = true;
                        });
                        // 使用由主機建立的 IConfiguration 實例
                        services.AddSingleton<IConfiguration>(context.Configuration);
                        services.AddScoped<IBlogService, BlogService>();
                        services.AddScoped<IMarkdownToHtmlService, MarkdownToHtmlConverter>();
                        services.AddScoped<ITemplateService, TemplateService>();
                        services.AddScoped<IFileService, FileService>();
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
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>(); // 取得最終的配置

                try
                {
                    // 顯示設定資訊
                    var contentPathConfig = configuration["BlogContentPath"];
                    var outputPathConfig = configuration["BlogSettings:HtmlOutputPath"];
                    var imagePathConfig = configuration["BlogSettings:ImageOutputPath"];
                    var templatePathConfig = configuration["BlogSettings:TemplatePath"];
                    var clearOutputDirectory =
                        configuration.GetValue<bool>("BlogSettings:ClearOutputDirectoryOnStart", false);

                    // 解析相對路徑 - 以執行檔目錄為基準
                    contentPathConfig = ResolveRelativePath(contentPathConfig, executableDirectory);
                    outputPathConfig = ResolveRelativePath(outputPathConfig, executableDirectory);
                    imagePathConfig = ResolveRelativePath(imagePathConfig, executableDirectory);
                    templatePathConfig = ResolveRelativePath(templatePathConfig, executableDirectory);

                    Console.WriteLine($"當前工作目錄: {Directory.GetCurrentDirectory()}");
                    Console.WriteLine($"內容目錄: {contentPathConfig}");
                    Console.WriteLine($"HTML 輸出目錄: {outputPathConfig}");
                    Console.WriteLine($"圖片輸出目錄: {imagePathConfig}");
                    Console.WriteLine($"模板目錄: {templatePathConfig}");
                    Console.WriteLine($"清除輸出目錄: {(clearOutputDirectory ? "是" : "否")}");
                    Console.WriteLine();

                    // 檢查設定檔路徑
                    if (string.IsNullOrEmpty(contentPathConfig) || string.IsNullOrEmpty(outputPathConfig))
                    {
                        Console.WriteLine($"錯誤: 請檢查 {configFileName} 設定檔中的路徑設定");
                        Console.WriteLine("請確保 BlogContentPath 和 BlogSettings:HtmlOutputPath 已正確設定");
                        Environment.Exit(1);
                    }

                    // 檢查目錄是否存在
                    if (!Directory.Exists(contentPathConfig))
                    {
                        Console.WriteLine($"錯誤: 內容目錄不存在: {contentPathConfig}");
                        Console.WriteLine($"請建立目錄或修改設定檔中的 BlogContentPath");
                        Environment.Exit(1);
                    }

                    // 處理輸出目錄
                    if (clearOutputDirectory && Directory.Exists(outputPathConfig))
                    {
                        Console.WriteLine($"正在清除輸出目錄: {outputPathConfig}");
                        logger.LogInformation("開始清除輸出目錄: {OutputPath}", outputPathConfig);

                        await ClearOutputDirectoryAsync(outputPathConfig, logger);

                        Console.WriteLine("輸出目錄已清除");
                        logger.LogInformation("輸出目錄清除完成");
                    }

                    // 確保輸出目錄存在
                    if (!Directory.Exists(outputPathConfig))
                    {
                        Console.WriteLine($"建立輸出目錄: {outputPathConfig}");
                        Directory.CreateDirectory(outputPathConfig);
                    }

                    // 確保圖片輸出目錄存在
                    if (!string.IsNullOrEmpty(imagePathConfig) && !Directory.Exists(imagePathConfig))
                    {
                        Console.WriteLine($"建立圖片輸出目錄: {imagePathConfig}");
                        Directory.CreateDirectory(imagePathConfig);
                    }

                    logger.LogInformation("開始執行轉換作業...");

                    // 執行轉換
                    await converterService.ConvertAllPostsAsync();

                    logger.LogInformation("轉換作業已完成！");
                    Console.WriteLine();
                    Console.WriteLine("轉換作業已完成！");
                    Console.WriteLine($"請查看輸出目錄: {outputPathConfig}");
                }
                catch (FileNotFoundException ex) when (ex.Message.Contains(configFileName))
                {
                    Console.WriteLine();
                    Console.WriteLine($"錯誤: 找不到設定檔 {configFileName}");
                    Console.WriteLine($"請確保 {configFileName} 檔案存在於執行檔目錄中: {executableDirectory}");
                    Console.WriteLine("您可以參考 velo.config.json.example 建立設定檔");
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
                        Console.WriteLine($"請檢查 {configFileName} 設定檔:");
                        Console.WriteLine("- 確保 JSON 格式正確");
                        Console.WriteLine("- 確保所有必要的設定項目存在");
                        Console.WriteLine("- 確保路徑設定正確且可存取");
                        Console.WriteLine($"- 設定檔位置: {configFilePath}");
                    }

                    Environment.Exit(1);
                }

                Console.WriteLine();
                Console.WriteLine("程式執行完畢");
            }, configFileOption, contentPathOption, outputPathOption, templatePathOption, imagePathOption,
            clearOutputOption,
            autoYamlOption, autoSaveModifiedOption);

        // 執行命令列解析
        await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// 解析相對路徑，以執行檔目錄為基準
    /// </summary>
    /// <param name="path">原始路徑</param>
    /// <param name="basePath">基準路徑（執行檔目錄）</param>
    /// <returns>解析後的絕對路徑</returns>
    private static string? ResolveRelativePath(string? path, string basePath)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // 如果已經是絕對路徑，直接返回
        if (Path.IsPathRooted(path))
            return path;

        // 相對路徑，以執行檔目錄為基準
        return Path.GetFullPath(Path.Combine(basePath, path));
    }
}