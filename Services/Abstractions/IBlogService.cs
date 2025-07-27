using Velo.Models;

namespace Velo.Services;

/// <summary>
///內容服務的介面。
/// </summary>
public interface IBlogService
{
    Task ForceScanAndReloadPostsAsync();
    Task<IEnumerable<BlogPost>> GetAllPostsAsync();

    /// <summary>
    /// 非同步取得分類樹的根節點。
    /// </summary>
    Task<CategoryNode> GetCategoryTreeAsync();

    Task<BlogPost?> GetPostBySlugAsync(string slug);
}