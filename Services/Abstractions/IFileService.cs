namespace Velo.Services.Abstractions;

/// <summary>
/// 檔案服務介面
/// 提供檔案和目錄操作的抽象
/// </summary>
public interface IFileService
{
    /// <summary>
    /// 清空輸出目錄
    /// 刪除目錄中的所有檔案和子目錄
    /// </summary>
    /// <param name="outputPath">輸出目錄路徑</param>
    /// <returns>異步任務</returns>
    Task ClearOutputDirectoryAsync(string outputPath);

    /// <summary>
    /// 複製檔案
    /// </summary>
    /// <param name="sourcePath">來源檔案路徑</param>
    /// <param name="destinationPath">目標檔案路徑</param>
    /// <param name="overwrite">是否覆寫現有檔案</param>
    Task CopyFileAsync(string sourcePath, string destinationPath, bool overwrite = true);

    /// <summary>
    /// 建立目錄
    /// </summary>
    /// <param name="directoryPath">目錄路徑</param>
    void CreateDirectory(string directoryPath);

    /// <summary>
    /// 檢查目錄是否存在
    /// </summary>
    /// <param name="directoryPath">目錄路徑</param>
    /// <returns>目錄是否存在</returns>
    bool DirectoryExists(string directoryPath);

    /// <summary>
    /// 確保目錄存在
    /// </summary>
    /// <param name="directoryPath">目錄路徑</param>
    void EnsureDirectoryExists(string directoryPath);

    /// <summary>
    /// 檢查檔案是否存在
    /// </summary>
    /// <param name="filePath">檔案路徑</param>
    /// <returns>檔案是否存在</returns>
    bool FileExists(string filePath);

    /// <summary>
    /// 取得應用程式執行檔所在的目錄路徑
    /// 處理各種部署模式（包括 PublishSingleFile）
    /// </summary>
    /// <returns>執行檔目錄的絕對路徑</returns>
    string GetExecutableDirectory();

    /// <summary>
    /// 取得目錄中的所有檔案
    /// </summary>
    /// <param name="directoryPath">目錄路徑</param>
    /// <param name="searchPattern">搜尋模式</param>
    /// <param name="searchOption">搜尋選項</param>
    /// <returns>檔案路徑集合</returns>
    IEnumerable<string> GetFiles(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.AllDirectories);

    /// <summary>
    /// 取得檔案的最後修改時間
    /// </summary>
    /// <param name="filePath">檔案路徑</param>
    /// <returns>最後修改時間</returns>
    DateTime GetLastWriteTime(string filePath);

    /// <summary>
    /// 讀取檔案內容
    /// </summary>
    /// <param name="filePath">檔案路徑</param>
    /// <returns>檔案內容</returns>
    Task<string> ReadAllTextAsync(string filePath);

    /// <summary>
    /// 解析相對路徑為絕對路徑
    /// 將相對路徑轉換為以指定基準目錄為基礎的絕對路徑
    /// </summary>
    /// <param name="path">原始路徑（可以是相對路徑或絕對路徑）</param>
    /// <param name="basePath">基準目錄路徑</param>
    /// <returns>解析後的絕對路徑，如果輸入為 null 或空則返回原值</returns>
    string? ResolveRelativePath(string? path, string basePath);

    /// <summary>
    /// 寫入檔案內容
    /// </summary>
    /// <param name="filePath">檔案路徑</param>
    /// <param name="content">檔案內容</param>
    Task WriteAllTextAsync(string filePath, string content);
}