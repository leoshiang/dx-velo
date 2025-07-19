using Velo.Models;

namespace Velo.Services;

public interface ITemplateService
{
    Task<string> LoadTemplateAsync(string templateName);
    Task<string> RenderIndexAsync(string templateContent, IEnumerable<BlogPost> posts);
    Task<string> RenderPostAsync(string templateContent, BlogPost post, string htmlContent);
    Task<bool> TemplateExistsAsync(string templateName);
}