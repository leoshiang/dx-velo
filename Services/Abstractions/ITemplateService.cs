using Velo.Models;

namespace Velo.Services;

public interface ITemplateService
{
    Task<string> RenderIndexAsync(IEnumerable<BlogPost> posts, CategoryNode categoryTree);
    Task<string> RenderPostAsync(BlogPost post, string htmlContent);
}