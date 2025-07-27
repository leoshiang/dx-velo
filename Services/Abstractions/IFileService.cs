namespace Velo.Services;

public interface IFileService
{
    Task CopyFileAsync(string sourcePath, string targetPath);
    void EnsureDirectoryExists(string path);
    Task<string> ReadFileAsync(string filePath);
    Task WriteFileAsync(string filePath, string content);
}