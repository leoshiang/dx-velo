namespace Velo.Services;

public interface IMarkdownToHtmlService
{
    Task ConvertAndSaveAllPostsAsync();
}