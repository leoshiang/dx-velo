# velo - 靜態部落格生成器

一個簡單但功能豐富的靜態部落格生成器，支援 Markdown 文章並自動產生分類目錄。

## 功能特色

### 文章管理
- 支援 Markdown 格式文章
- 自動解析 Front Matter (標題、日期、分類、標籤)
- 支援文章分類和標籤系統
- 自動產生文章摘要

### 模板系統
- 支援自定義 HTML 模板
- 內建預設模板 (可覆蓋)
- 支援 Handlebars 語法
- 響應式設計，支援桌面和行動裝置

### 進階功能
- **搜尋功能**：即時搜尋文章標題、標籤和分類
- **分類樹狀結構**：支援多層分類導航
- **卡片式設計**：現代化的文章展示介面
- **目錄導航**：自動產生文章內標題目錄 (TOC)
- **返回頂端按鈕**：智能定位的浮動按鈕

### 使用者體驗
- **固定側邊欄**：在文章頁面中，返回首頁按鈕固定不滾動
- **互動式篩選**：可依分類篩選文章
- **漸層美化**：現代化的視覺設計
- **響應式佈局**：適應各種螢幕尺寸

## 快速開始

### 使用預編譯版本 (推薦)

1. 前往 [Releases 頁面](../../releases) 下載對應平台的 ZIP 檔案
2. 解壓縮到任意目錄
3. 建立 `velo.config.json` 設定檔案 (可參考範例)
4. 建立 `Posts` 目錄並放置 Markdown 文章
5. 執行 velo 程式產生網站

### 從原始碼編譯

#### 環境需求
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 或更高版本

#### 編譯步驟

**Clone 專案：**
```bash
git clone https://github.com/leoshiang/dx-velo.git
cd velo
```

**開發模式執行：**

```bash
dotnet run
```

**建置 Release 版本：**

```bash
dotnet build -c Release
```

**發佈特定平台：**

```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
# macOS x64
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

## 自動化發布

專案提供自動化發布腳本，可一次建置所有平台版本：

### Windows 系統
```cmd
build-release.bat
```

### Linux / macOS 系統
```bash
# 設定執行權限
chmod +x build-release.sh
# 執行發布腳本
./build-release.sh
```

### 發布腳本功能

- **多平台支援**：Windows (x64/ARM64)、Linux (x64/ARM64)、macOS (x64/ARM64)
- **單一執行檔**：使用 `PublishSingleFile` 和 `PublishTrimmed` 最佳化
- **日期版本控制**：依日期 (YYYYMMDD) 建立發布目錄
- **完整發布包**：包含執行檔、模板、設定範例和說明文件
- **自動打包**：產生 ZIP 檔案方便分發
- **依賴檢查**：自動檢查 .NET SDK 和必要工具

### 發布目錄結構
```
releases/ 
└── 20241201/ # 發布日期
├── BUILD_INFO.txt # 建置資訊
├── velo-v1.0.0-win-x64.zip # Windows x64 版本
├── velo-v1.0.0-win-arm64.zip # Windows ARM64 版本
├── velo-v1.0.0-linux-x64.zip # Linux x64 版本
├── velo-v1.0.0-linux-arm64.zip # Linux ARM64 版本
├── velo-v1.0.0-osx-x64.zip # macOS Intel 版本
└── velo-v1.0.0-osx-arm64.zip # macOS Apple Silicon 版本
```

每個 ZIP 檔案包含：
```
velo-v1.0.0-win-x64/ 
├── velo.exe # 執行檔 (Windows) 或 velo (Unix) 
├── templates/ # 模板目錄 │ 
├── index.html # 首頁模板 │ 
└── post.html # 文章頁模板 
├── velo.config.json.example # 設定檔範例 
└── README.txt # 使用說明
```

## 設定檔案 (velo.config.json)
velo 使用 JSON 格式的設定檔來控制生成行為。以下是完整的設定選項說明：
### 基本設定範例
``` json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "BlogContentPath": "./Posts",
  "BlogSettings": {
    "HtmlOutputPath": "./Output",
    "TemplatePath": "./templates",
    "ImageOutputPath": "./Output/images",
    "ClearOutputDirectoryOnStart": true,
    "AutoAddYamlHeader": true,
    "AutoSaveModified": true,
  }
}
```
### 詳細設定說明
#### 路徑設定

| 設定項目 | 類型 | 預設值 | 說明 |
| --- | --- | --- | --- |
| `BlogContentPath` | `string` | `"./Posts"` | Markdown 文章檔案存放目錄 |
| `HtmlOutputPath` | `string` | `"./Output"` | 生成的 HTML 檔案輸出目錄 |
| `TemplatePath` | `string` | `"./templates"` | 模板檔案存放目錄 |
| `ImageOutputPath` | `string` | `"./Output/images"` | 圖片資源輸出目錄 |

**路徑格式說明：**

- 支援相對路徑 (`./Posts`) 和絕對路徑 (`C:\MyBlog\Posts`)
- Windows 路徑範例：`"E:\\Dropbox\\部落格"`
- Unix 路徑範例：`"/home/user/blog/posts"`
- 相對路徑以執行檔所在目錄為基準

#### 行為設定

| 設定項目 | 類型 | 預設值 | 說明 |
| --- | --- | --- | --- |
| `ClearOutputDirectoryOnStart` | `bool` | `true` | 生成前是否清空輸出目錄 |
| `AutoAddYamlHeader` | `bool` | `true` | 自動為缺少 Front Matter 的檔案添加 |
| `AutoSaveModified` | `bool` | `true` | 自動儲存修改過的檔案 |
#### 日誌設定
``` json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",      // 一般日誌等級
      "Microsoft": "Warning",        // Microsoft 元件日誌等級  
      "System": "Warning",           // 系統日誌等級
      "velo": "Debug"               // velo 應用程式日誌等級
    }
  }
}
```
**日誌等級說明：**
- `Trace`: 最詳細的日誌
- `Debug`: 除錯資訊
- `Information`: 一般資訊 (預設)
- `Warning`: 警告訊息
- `Error`: 錯誤訊息
- `Critical`: 嚴重錯誤

### 設定檔範例
#### 簡化設定 (最小配置)
``` json
{
  "BlogContentPath": "Posts",
  "BlogSettings": {
    "HtmlOutputPath": "Output"
  }
}
```
#### 完整設定 (生產環境)
``` json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "BlogContentPath": "/home/user/blog/posts",
  "BlogSettings": {
    "HtmlOutputPath": "/var/www/html",
    "TemplatePath": "/home/user/blog/templates",
    "ImageOutputPath": "/var/www/html/assets/images",
    "ClearOutputDirectoryOnStart": true,
    "AutoAddYamlHeader": false,
    "AutoSaveModified": false,
  }
}
```
#### Dropbox 同步設定
``` json
{
  "BlogContentPath": "E:\\Dropbox\\部落格\\文章",
  "BlogSettings": {
    "HtmlOutputPath": "E:\\Dropbox\\部落格\\輸出",
    "TemplatePath": "E:\\Dropbox\\部落格\\模板", 
    "ImageOutputPath": "E:\\Dropbox\\部落格\\輸出\\images",
    "AutoSaveModified": true,
    "ClearOutputDirectoryOnStart": false
  }
}
```
### 注意事項
1. **路徑分隔符號**：
    - Windows：使用 `\\` 或 `/`
    - Unix/macOS：使用 `/`

2. **權限設定**：
    - 確保程式對所有指定目錄具有讀寫權限
    - 輸出目錄必須可寫入

3. **編碼設定**：
    - 設定檔必須使用 UTF-8 編碼
    - 支援中文路徑和檔案名稱

4. **備份建議**：
    - 建議定期備份設定檔和重要檔案
    - 測試新設定前先備份現有內容

## 文章格式
Markdown 文章需要包含 Front Matter：
``` markdown
---
title: "文章標題"
date: 2024-01-01
categories: ["分類1", "分類2"]  
tags: ["標籤1", "標籤2"]
---

這裡是文章內容...
```
## 模板系統
### 可用的模板檔案
- - 單篇文章模板 `post.html`
- - 首頁/文章列表模板 `index.html`

### 支援的模板變數
**文章頁面 (post.html):**
- `{{Title}}` - 文章標題
- `{{Content}}` - 轉換後的 HTML 內容
- `{{PublishedDate}}` - 發佈日期 (yyyy-MM-dd)
- `{{PublishedDateLong}}` - 發佈日期 (yyyy年MM月dd日)
- `{{Slug}}` - 文章的 slug
- `{{Categories}}` - 分類 (HTML 格式)
- `{{CategoriesPlain}}` - 分類 (純文字)
- `{{Tags}}` - 標籤 (HTML 格式)
- `{{TagsPlain}}` - 標籤 (純文字)

**首頁 (index.html):**
- `{{PostCount}}` - 文章總數
- `{{Posts}}` - 文章陣列 (用於 each 循環)
- `{{CategoryTree}}` - 分類樹狀結構 HTML

### 支援的條件語法
- `{{#if Categories}}...{{/if}}` - 當有分類時顯示
- `{{#if Tags}}...{{/if}}` - 當有標籤時顯示
- `{{#each Posts}}...{{/each}}` - 遍歷所有文章 (僅索引頁面)

## 內建功能
### 搜尋系統
- 即時搜尋文章標題、分類和標籤
- 支援中英文搜尋
- 搜尋結果計數顯示

### 分類系統
- 支援階層式分類結構
- 可展開/收合的分類樹
- 點擊分類篩選文章
- 分類文章數量顯示

### 互動功能
- 響應式導航選單
- 平滑滾動效果
- Hover 動畫效果
- 卡片式文章展示

## 視覺設計特色
### 現代化 UI
- **卡片式設計**：每篇文章都以獨立卡片呈現
- **漸層效果**：標籤、分類和按鈕使用漸層色彩
- **陰影效果**：立體感的視覺層次
- **Hover 動畫**：滑鼠懸停時的互動回饋

### 響應式設計
- **桌面版**：雙欄佈局，側邊欄固定
- **平板版**：自適應欄位寬度
- **手機版**：單欄佈局，側邊欄置頂

### 使用者體驗優化
- **固定返回首頁**：在文章頁面中不會隨內容滾動
- **智能定位**：返回頂端按鈕根據內容區域智能定位
- **視覺回饋**：所有互動元素都有適當的視覺回饋

## 開發資訊
### 技術堆疊
- **語言**：C# 13.0
- **框架**：.NET 9.0
- **相依套件**：
    - `Markdig` - Markdown 解析
    - `YamlDotNet` - YAML/Front Matter 解析
    - `Microsoft.Extensions.*` - 依賴注入與設定管理

### 專案結構

velo/
├── Models/             # 資料模型
├── Services/           # 業務邏輯服務
├── templates/          # 內建模板
├── Utils/              # 工具類別
├── Program.cs          # 程式進入點
├── velo.csproj         # 專案檔案
├── velo.config.json    # 設定檔
├── build-release.bat   # Windows 發布腳本
├── build-release.sh    # Unix 發布腳本
└── README.md           # 專案說明

### 建置設定
- 目標框架：.NET 9.0
- 輸出類型：控制台應用程式
- 支援 Nullable 參考型別
- 啟用隱式 Using 指令

## 模板覆蓋
系統會自動：
1. 檢查 `templates` 目錄是否存在對應的模板檔案
2. 如果存在，使用自定義模板
3. 如果不存在，使用預設的內建模板
4. 支援部分覆蓋，可只自定義需要的模板

這讓您可以輕鬆客製化網站外觀，同時保持核心功能不變。
## 授權
本專案採用 MIT 授權條款，詳見 [LICENSE](LICENSE) 檔案。
## 貢獻
歡迎提交 Issue 和 Pull Request！
## 支援
如有問題或建議，請：
1. 查看 [Issues](../../issues) 頁面
2. 建立新的 Issue
3. 參考本 README 文件

_讓寫作變得更簡單，讓部落格更美好！_
