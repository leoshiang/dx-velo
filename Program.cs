using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velo.Services;
using Velo.Services.Abstractions;

namespace Velo;

class Program
{
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
                // 建立臨時的 FileService 實例來取得執行檔目錄
                using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var tempLogger = loggerFactory.CreateLogger<FileService>();
                var tempFileService = new FileService(tempLogger);

                var executableDirectory = tempFileService.GetExecutableDirectory();

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
                    .ConfigureAppConfiguration(config =>
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
                        if (commandLineOverrides.Count == 0) return;
                        config.AddInMemoryCollection(commandLineOverrides!);
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.Configure<ConsoleLifetimeOptions>(options =>
                        {
                            options.SuppressStatusMessages = true;
                        });
                        // 使用由主機建立的 IConfiguration 實例
                        services.AddSingleton(context.Configuration);
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
                var fileService = scope.ServiceProvider.GetRequiredService<IFileService>(); // 取得 FileService
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>(); // 取得最終的配置

                try
                {
                    // 顯示設定資訊
                    var contentPathConfig = configuration["BlogContentPath"];
                    var outputPathConfig = configuration["BlogSettings:HtmlOutputPath"];
                    var imagePathConfig = configuration["BlogSettings:ImageOutputPath"];
                    var templatePathConfig = configuration["BlogSettings:TemplatePath"];
                    var clearOutputDirectory =
                        configuration.GetValue("BlogSettings:ClearOutputDirectoryOnStart", false);

                    // 使用 FileService 解析相對路徑 - 以執行檔目錄為基準
                    contentPathConfig = fileService.ResolveRelativePath(contentPathConfig, executableDirectory);
                    outputPathConfig = fileService.ResolveRelativePath(outputPathConfig, executableDirectory);
                    imagePathConfig = fileService.ResolveRelativePath(imagePathConfig, executableDirectory);
                    templatePathConfig = fileService.ResolveRelativePath(templatePathConfig, executableDirectory);

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

                    // 檢查目錄是否存在 - 使用 FileService
                    if (!fileService.DirectoryExists(contentPathConfig))
                    {
                        Console.WriteLine($"錯誤: 內容目錄不存在: {contentPathConfig}");
                        Console.WriteLine($"請建立目錄或修改設定檔中的 BlogContentPath");
                        Environment.Exit(1);
                    }

                    // 處理輸出目錄 - 使用 FileService
                    if (clearOutputDirectory && fileService.DirectoryExists(outputPathConfig))
                    {
                        Console.WriteLine($"正在清除輸出目錄: {outputPathConfig}");
                        logger.LogInformation("開始清除輸出目錄: {OutputPath}", outputPathConfig);

                        // 使用 FileService 的方法
                        await fileService.ClearOutputDirectoryAsync(outputPathConfig);

                        Console.WriteLine("輸出目錄已清除");
                        logger.LogInformation("輸出目錄清除完成");
                    }

                    // 確保輸出目錄存在 - 使用 FileService
                    if (!fileService.DirectoryExists(outputPathConfig))
                    {
                        Console.WriteLine($"建立輸出目錄: {outputPathConfig}");
                        fileService.CreateDirectory(outputPathConfig);
                    }

                    // 確保圖片輸出目錄存在 - 使用 FileService
                    if (!string.IsNullOrEmpty(imagePathConfig) && !fileService.DirectoryExists(imagePathConfig))
                    {
                        Console.WriteLine($"建立圖片輸出目錄: {imagePathConfig}");
                        fileService.CreateDirectory(imagePathConfig);
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
}