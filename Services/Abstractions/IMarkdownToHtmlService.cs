namespace Velo.Services;

public interface IMarkdownToHtmlService
{
    Task ConvertAndSaveAllPostsAsync();
    Task<string> ConvertToHtmlAsync(string markdown, string? sourceFilePath = null);
}