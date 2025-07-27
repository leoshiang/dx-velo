using Microsoft.Extensions.Logging;

namespace Velo.Services;

/// <summary>
/// 檔案系統操作服務
/// 提供檔案和目錄的基本操作功能，包括讀取、寫入、複製檔案以及目錄管理
/// 所有操作都包含完整的錯誤處理和日誌記錄
/// </summary>
/// <param name="logger">日誌記錄器，用於記錄檔案操作的結果和錯誤資訊</param>
public class FileService(ILogger<FileService> logger) : IFileService
{
    #region 檔案複製操作

    /// <summary>
    /// 非同步複製檔案
    /// 使用串流的方式進行檔案複製，支援大檔案的高效複製
    /// 自動確保目標目錄存在，避免因目錄不存在而複製失敗
    /// </summary>
    /// <param name="sourcePath">來源檔案的完整路徑</param>
    /// <param name="targetPath">目標檔案的完整路徑</param>
    /// <returns>非同步任務</returns>
    /// <exception cref="FileNotFoundException">當來源檔案不存在時拋出</exception>
    /// <exception cref="UnauthorizedAccessException">當沒有檔案存取權限時拋出</exception>
    /// <exception cref="DirectoryNotFoundException">當來源目錄不存在時拋出</exception>
    /// <exception cref="IOException">當發生 I/O 錯誤時拋出</exception>
    public async Task CopyFileAsync(string sourcePath, string targetPath)
    {
        try
        {
            // 確保目標檔案的目錄存在
            // 從目標檔案路徑中提取目錄路徑，並確保該目錄存在
            EnsureDirectoryExists(Path.GetDirectoryName(targetPath)!);

            // 使用非同步串流進行檔案複製
            // await using 確保串流在使用完畢後自動釋放資源
            await using var sourceStream = File.OpenRead(sourcePath); // 開啟來源檔案進行讀取
            await using var targetStream = File.Create(targetPath); // 建立目標檔案進行寫入

            // 將來源串流的內容複製到目標串流
            // CopyToAsync 方法會自動處理緩衝區管理，適合處理大檔案
            await sourceStream.CopyToAsync(targetStream);

            // 記錄成功的複製操作
            logger.LogDebug("檔案複製成功: {Source} -> {Target}", sourcePath, targetPath);
        }
        catch (Exception ex)
        {
            // 記錄錯誤並重新拋出例外，讓呼叫者能夠處理
            logger.LogError(ex, "複製檔案失敗: {Source} -> {Target}", sourcePath, targetPath);
            throw;
        }
    }

    #endregion

    #region 目錄管理操作

    /// <summary>
    /// 確保指定的目錄存在
    /// 如果目錄不存在，則遞歸建立所有必要的父目錄
    /// 這是一個同步方法，因為目錄建立操作通常很快完成
    /// </summary>
    /// <param name="path">要確保存在的目錄路徑</param>
    /// <remarks>
    /// 此方法是冪等的（idempotent），多次呼叫相同路徑不會產生錯誤
    /// 如果路徑為空或 null，方法會安全地忽略操作
    /// </remarks>
    public void EnsureDirectoryExists(string path)
    {
        // 檢查路徑是否有效且目錄是否不存在
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            // CreateDirectory 會自動建立所有必要的父目錄
            // 例如：建立 "a/b/c" 時，會自動建立 "a" 和 "a/b"
            Directory.CreateDirectory(path);
        }
    }

    #endregion

    #region 檔案讀取操作

    /// <summary>
    /// 非同步讀取檔案的完整內容
    /// 以 UTF-8 編碼讀取文字檔案，適用於 Markdown、HTML、JSON 等文字格式
    /// </summary>
    /// <param name="filePath">要讀取的檔案完整路徑</param>
    /// <returns>檔案的完整文字內容</returns>
    /// <exception cref="FileNotFoundException">當檔案不存在時拋出</exception>
    /// <exception cref="UnauthorizedAccessException">當沒有檔案讀取權限時拋出</exception>
    /// <exception cref="DirectoryNotFoundException">當檔案所在目錄不存在時拋出</exception>
    /// <exception cref="IOException">當發生 I/O 錯誤時拋出</exception>
    /// <remarks>
    /// 此方法會將整個檔案載入記憶體，不適用於非常大的檔案
    /// 對於大檔案，建議使用串流讀取方式
    /// </remarks>
    public async Task<string> ReadFileAsync(string filePath)
    {
        try
        {
            // File.ReadAllTextAsync 預設使用 UTF-8 編碼
            // 自動處理 BOM (Byte Order Mark) 和不同的換行符號格式
            return await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            // 記錄詳細的錯誤資訊，包括檔案路徑
            logger.LogError(ex, "讀取檔案失敗: {FilePath}", filePath);
            throw;
        }
    }

    #endregion

    #region 檔案寫入操作

    /// <summary>
    /// 非同步寫入內容到檔案
    /// 以 UTF-8 編碼寫入文字內容，如果檔案已存在則完全覆蓋
    /// 自動確保目標目錄存在，避免因目錄不存在而寫入失敗
    /// </summary>
    /// <param name="filePath">要寫入的檔案完整路徑</param>
    /// <param name="content">要寫入的文字內容</param>
    /// <returns>非同步任務</returns>
    /// <exception cref="UnauthorizedAccessException">當沒有檔案寫入權限時拋出</exception>
    /// <exception cref="DirectoryNotFoundException">當無法建立目標目錄時拋出</exception>
    /// <exception cref="IOException">當發生 I/O 錯誤時拋出</exception>
    /// <remarks>
    /// 此方法會完全覆蓋現有檔案內容
    /// 使用 UTF-8 編碼寫入，包含 BOM
    /// 適用於 HTML、Markdown、JSON 等文字檔案的寫入
    /// </remarks>
    public async Task WriteFileAsync(string filePath, string content)
    {
        try
        {
            // 確保檔案所在的目錄存在
            // 從檔案路徑中提取目錄部分，並確保該目錄存在
            EnsureDirectoryExists(Path.GetDirectoryName(filePath)!);

            // File.WriteAllTextAsync 預設使用 UTF-8 編碼
            // 如果檔案已存在，會完全覆蓋原有內容
            // 如果檔案不存在，會自動建立新檔案
            await File.WriteAllTextAsync(filePath, content);
        }
        catch (Exception ex)
        {
            // 記錄詳細的錯誤資訊，包括檔案路徑
            logger.LogError(ex, "寫入檔案失敗: {FilePath}", filePath);
            throw;
        }
    }

    #endregion

    #region 檔案搜尋操作

    /// <summary>
    /// 非同步搜尋指定目錄中的所有 Markdown 檔案
    /// 遞歸搜尋所有子目錄，找出所有 .md 副檔名的檔案
    /// 這是部落格系統的核心功能，用於發現所有需要處理的文章檔案
    /// </summary>
    /// <param name="directory">要搜尋的根目錄路徑</param>
    /// <returns>找到的所有 Markdown 檔案的完整路徑集合</returns>
    /// <exception cref="UnauthorizedAccessException">當沒有目錄存取權限時拋出</exception>
    /// <exception cref="IOException">當發生 I/O 錯誤時拋出</exception>
    /// <remarks>
    /// 搜尋模式：
    /// - 檔案模式："*.md" (不區分大小寫)
    /// - 搜尋範圍：包含所有子目錄 (SearchOption.AllDirectories)
    /// - 安全處理：如果目錄不存在，回傳空集合而不拋出例外
    /// 
    /// 效能考量：
    /// - 對於包含大量檔案的目錄結構，此操作可能耗時較長
    /// - 建議在應用程式啟動時執行一次，然後快取結果
    /// </remarks>
    public Task<IEnumerable<string>> GetMarkdownFilesAsync(string directory)
    {
        try
        {
            // 檢查目錄是否存在
            if (!Directory.Exists(directory))
            {
                // 目錄不存在時記錄警告，但不拋出例外
                // 這樣可以讓應用程式在設定錯誤時仍能正常運行
                logger.LogWarning("目錄不存在: {Directory}", directory);
                return Task.FromResult(Enumerable.Empty<string>());
            }

            // 遞歸搜尋所有 .md 檔案
            // SearchOption.AllDirectories 表示包含所有子目錄
            // "*.md" 模式會匹配所有以 .md 結尾的檔案
            var files = Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories);

            // 記錄搜尋結果的統計資訊
            logger.LogInformation("找到 {Count} 個 Markdown 檔案", files.Length);

            // 將陣列轉換為 IEnumerable<string> 並包裝為已完成的 Task
            return Task.FromResult<IEnumerable<string>>(files);
        }
        catch (Exception ex)
        {
            // 記錄搜尋過程中的錯誤
            logger.LogError(ex, "掃描 Markdown 檔案失敗: {Directory}", directory);
            throw;
        }
    }

    #endregion
}