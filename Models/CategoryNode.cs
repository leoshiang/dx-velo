namespace Velo.Models;

/// <summary>
/// 代表分類樹中的一個節點。
/// </summary>
public class CategoryNode
{
    public List<CategoryNode> Children { get; set; } = [];
    public required string Name { get; set; }
    public required string PathSegment { get; set; }
    public int PostCount { get; set; }
}