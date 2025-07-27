using System.Reflection;
using Microsoft.Extensions.Logging;
using Velo.Services.Abstractions;

namespace Velo.Services;

/// <summary>
/// 檔案服務實作
/// 提供檔案和目錄操作功能
/// </summary>
public class FileService(ILogger<FileService> logger) : IFileService
{
    /// <summary>
    /// 清空輸出目錄
    /// 刪除目錄中的所有檔案和子目錄
    /// </summary>
    public async Task ClearOutputDirectoryAsync(string outputPath)
    {
        try
        {
            logger.LogInformation("開始清空輸出目錄: {OutputPath}", outputPath);

            if (!Directory.Exists(outputPath))
            {
                logger.LogWarning("輸出目錄不存在: {OutputPath}", outputPath);
                return;
            }

            await Task.Run(() =>
            {
                // 取得目錄中的所有檔案和子目錄
                var directoryInfo = new DirectoryInfo(outputPath);
                int deletedFiles = 0;
                int deletedDirectories = 0;

                // 刪除所有檔案
                foreach (var file in directoryInfo.GetFiles())
                {
                    try
                    {
                        file.Delete();
                        deletedFiles++;
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
                        deletedDirectories++;
                        logger.LogDebug("已刪除目錄: {DirectoryPath}", directory.FullName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "無法刪除目錄: {DirectoryPath}", directory.FullName);
                    }
                }

                logger.LogInformation("輸出目錄清除完成，已刪除 {FileCount} 個檔案和 {DirectoryCount} 個目錄",
                    deletedFiles, deletedDirectories);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清除輸出目錄時發生錯誤: {OutputPath}", outputPath);
            throw;
        }
    }

    /// <summary>
    /// 複製檔案
    /// </summary>
    public async Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true)
    {
        try
        {
            logger.LogDebug("複製檔案: {Source} -> {Destination}", sourcePath, destinationPath);

            // 確保目標目錄存在
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                EnsureDirectoryExists(destinationDirectory);
            }

            await Task.Run(() => File.Copy(sourcePath, destinationPath, overwrite));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "複製檔案失敗: {Source} -> {Destination}", sourcePath, destinationPath);
            throw;
        }
    }

    /// <summary>
    /// 建立目錄
    /// </summary>
    public void CreateDirectory(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                logger.LogDebug("建立目錄: {DirectoryPath}", directoryPath);
                Directory.CreateDirectory(directoryPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "建立目錄失敗: {DirectoryPath}", directoryPath);
            throw;
        }
    }

    /// <summary>
    /// 檢查目錄是否存在
    /// </summary>
    public bool DirectoryExists(string directoryPath)
    {
        return Directory.Exists(directoryPath);
    }

    /// <summary>
    /// 確保目錄存在
    /// </summary>
    public void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            logger.LogDebug("建立目錄: {DirectoryPath}", directoryPath);
            Directory.CreateDirectory(directoryPath);
        }
    }

    /// <summary>
    /// 檢查檔案是否存在
    /// </summary>
    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    /// <summary>
    /// 取得應用程式執行檔所在的目錄路徑
    /// 處理各種部署模式（包括 PublishSingleFile）
    /// </summary>
    /// <returns>執行檔目錄的絕對路徑</returns>
    public string GetExecutableDirectory()
    {
        try
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;

            if (string.IsNullOrEmpty(assemblyLocation))
            {
                // 在某些情況下（如 PublishSingleFile），Assembly.Location 可能為空
                // 這時使用 AppContext.BaseDirectory
                logger.LogDebug("Assembly.Location 為空，使用 AppContext.BaseDirectory: {BaseDirectory}",
                    AppContext.BaseDirectory);
                return AppContext.BaseDirectory;
            }

            var executableDirectory = Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
            logger.LogDebug("取得執行檔目錄: {Directory}", executableDirectory);

            return executableDirectory;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "取得執行檔目錄失敗");
            throw;
        }
    }

    /// <summary>
    /// 取得目錄中的所有檔案
    /// </summary>
    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.AllDirectories)
    {
        try
        {
            logger.LogDebug("掃描目錄檔案: {DirectoryPath}, 模式: {Pattern}", directoryPath, searchPattern);
            return Directory.GetFiles(directoryPath, searchPattern, searchOption);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "掃描目錄檔案失敗: {DirectoryPath}", directoryPath);
            throw;
        }
    }

    /// <summary>
    /// 取得檔案的最後修改時間
    /// </summary>
    public DateTime GetLastWriteTime(string filePath)
    {
        try
        {
            return File.GetLastWriteTime(filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "取得檔案修改時間失敗: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// 讀取檔案內容
    /// </summary>
    public async Task<string> ReadAllTextAsync(string filePath)
    {
        try
        {
            logger.LogDebug("讀取檔案: {FilePath}", filePath);
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "讀取檔案失敗: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// 解析相對路徑為絕對路徑
    /// 將相對路徑轉換為以指定基準目錄為基礎的絕對路徑
    /// </summary>
    /// <param name="path">原始路徑（可以是相對路徑或絕對路徑）</param>
    /// <param name="basePath">基準目錄路徑</param>
    /// <returns>解析後的絕對路徑，如果輸入為 null 或空則返回原值</returns>
    public string? ResolveRelativePath(string? path, string basePath)
    {
        try
        {
            // 如果路徑為空或 null，直接返回
            if (string.IsNullOrEmpty(path))
            {
                logger.LogDebug("路徑為空，直接返回: {Path}", path);
                return path;
            }

            // 如果已經是絕對路徑，直接返回
            if (Path.IsPathRooted(path))
            {
                logger.LogDebug("已是絕對路徑，直接返回: {Path}", path);
                return path;
            }

            // 相對路徑，以基準目錄為基礎進行解析
            var resolvedPath = Path.GetFullPath(Path.Combine(basePath, path));
            logger.LogDebug("解析相對路徑: {OriginalPath} + {BasePath} -> {ResolvedPath}",
                path, basePath, resolvedPath);

            return resolvedPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解析相對路徑失敗: Path={Path}, BasePath={BasePath}", path, basePath);
            throw;
        }
    }

    /// <summary>
    /// 寫入檔案內容
    /// </summary>
    public async Task WriteAllTextAsync(string filePath, string content)
    {
        try
        {
            logger.LogDebug("寫入檔案: {FilePath}", filePath);

            // 確保目錄存在
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                EnsureDirectoryExists(directory);
            }

            await File.WriteAllTextAsync(filePath, content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "寫入檔案失敗: {FilePath}", filePath);
            throw;
        }
    }
}