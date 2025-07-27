using Microsoft.Extensions.Logging;

namespace Velo.Services;

public class FileService(ILogger<FileService> logger) : IFileService
{
    private readonly ILogger<FileService> _logger = logger;

    public async Task CopyFileAsync(string sourcePath, string targetPath)
    {
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(targetPath)!);

            await using var sourceStream = File.OpenRead(sourcePath);
            await using var targetStream = File.Create(targetPath);
            await sourceStream.CopyToAsync(targetStream);

            _logger.LogDebug("檔案複製成功: {Source} -> {Target}", sourcePath, targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "複製檔案失敗: {Source} -> {Target}", sourcePath, targetPath);
            throw;
        }
    }

    public void EnsureDirectoryExists(string path)
    {
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public Task<IEnumerable<string>> GetMarkdownFilesAsync(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("目錄不存在: {Directory}", directory);
                return Task.FromResult(Enumerable.Empty<string>());
            }

            var files = Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories);
            _logger.LogInformation("找到 {Count} 個 Markdown 檔案", files.Length);

            return Task.FromResult<IEnumerable<string>>(files);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "掃描 Markdown 檔案失敗: {Directory}", directory);
            throw;
        }
    }

    public async Task<string> ReadFileAsync(string filePath)
    {
        try
        {
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "讀取檔案失敗: {FilePath}", filePath);
            throw;
        }
    }

    public async Task WriteFileAsync(string filePath, string content)
    {
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "寫入檔案失敗: {FilePath}", filePath);
            throw;
        }
    }
}