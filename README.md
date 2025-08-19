<div align="center">
  <h1>Velo</h1>
  <p><strong>一個簡潔優雅的靜態部落格生成器</strong></p>
  <p>
    <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 9.0">
    <img src="https://img.shields.io/badge/Language-C%23_13.0-239120?style=flat-square&logo=csharp" alt="C# 13.0">
    <img src="https://img.shields.io/badge/License-MIT-green?style=flat-square" alt="MIT License">
    <img src="https://img.shields.io/badge/Platform-Cross--Platform-lightgrey?style=flat-square" alt="Cross Platform">
  </p>
</div>

## 範例網站
* https://blog.leoshiang.tw
* https://photography.leoshiang.tw

## 特色功能

### 內容管理
- **Markdown 支援**：完整的 Markdown 語法支援，包含 Front Matter
- **智慧解析**：自動解析文章標題、日期、分類和標籤
- **多層分類**：支援階層式分類結構
- **自動摘要**：自動生成文章摘要

### 現代化設計
- **響應式佈局**：完美適配桌面、平板和手機
- **卡片式介面**：現代化的文章展示方式
- **漸層美化**：精美的視覺效果
- **互動動畫**：流暢的使用者體驗

### 智能功能
- **即時搜尋**：快速搜尋文章標題、標籤和分類
- **動態篩選**：依分類即時篩選文章
- **目錄導航**：自動生成文章內標題目錄 (TOC)
- **智能定位**：返回頂端按鈕智能定位

### 效能與便利性
- **高速生成**：快速轉換 Markdown 到 HTML
- **模板系統**：靈活的自定義模板支援
- **跨平台**：支援 Windows、macOS、Linux
- **單一執行檔**：無需額外依賴

## 快速開始

### 方法一：下載預編譯版本

1. 前往 [Releases 頁面](https://github.com/leoshiang/velo/releases)
2. 下載對應平台的 ZIP 檔案
3. 解壓縮到任意目錄
4. 建立 `velo.config.json` 設定檔案
5. 執行程式開始使用

### 方法二：從原始碼編譯

**環境需求**
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

**快速編譯**
```
bash git clone [https://github.com/leoshiang/velo.git](https://github.com/leoshiang/velo.git) cd velo dotnet run
```

**建置發行版本**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 專案結構
```
velo/
├── Models/              # 資料模型
├── Services/            # 核心服務
├── Utils/               # 工具類別
├── templates/           # 內建模板
├── Program.cs           # 程式進入點
├── velo.config.json     # 主設定檔
├── README.md            # 專案說明
└── LICENSE.txt          # 授權條款
```
## 設定檔案
建立 `velo.config.json` 設定您的部落格：
``` json
{
  "BlogContentPath": "./Posts",
  "BlogSettings": {
    "HtmlOutputPath": "./Output",
    "TemplatePath": "./templates",
    "ImageOutputPath": "./Output/images",
    "ClearOutputDirectoryOnStart": true,
    "AutoAddYamlHeader": true,
    "AutoSaveModified": true
  }
}
```
### 主要設定項目

| 設定項目 | 說明 | 預設值 |
| --- | --- | --- |
| `BlogContentPath` | Markdown 文章目錄 | `"./Posts"` |
| `HtmlOutputPath` | HTML 輸出目錄 | `"./Output"` |
| `TemplatePath` | 模板檔案目錄 | `"./templates"` |
| `ImageOutputPath` | 圖片輸出目錄 | `"./Output/images"` |
| `ClearOutputDirectoryOnStart` | 生成前清空輸出目錄 | `true` |
| `AutoAddYamlHeader` | 自動添加 Front Matter | `true` |
### 進階設定範例
**簡化設定（最小配置）**
``` json
{
  "BlogContentPath": "Posts",
  "BlogSettings": {
    "HtmlOutputPath": "Output"
  }
}
```
**生產環境設定**
``` json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "BlogContentPath": "/var/www/blog/posts",
  "BlogSettings": {
    "HtmlOutputPath": "/var/www/html",
    "TemplatePath": "/var/www/blog/templates",
    "ImageOutputPath": "/var/www/html/assets/images",
    "ClearOutputDirectoryOnStart": true,
    "AutoAddYamlHeader": false,
    "AutoSaveModified": false
  }
}
```
**雲端同步設定（Dropbox）**
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
## 文章格式
Markdown 文章需包含 Front Matter：
``` markdown
---
title: "我的第一篇文章"
date: 2024-01-01
categories: ["技術", "程式設計"]  
tags: ["C#", ".NET", "部落格"]
---

# 標題

這裡是文章內容...

## 小標題

更多內容...
```
### Front Matter 說明

| 欄位 | 必需 | 說明 | 範例 |
| --- | --- | --- | --- |
| `title` | 是 | 文章標題 | `"我的文章"` |
| `date` | 是 | 發佈日期 | `2024-01-01` |
| `categories` | 否 | 文章分類 | `["技術", "程式設計"]` |
| `tags` | 否 | 文章標籤 | `["C#", ".NET"]` |
## 模板系統
### 內建模板
- `index.html` - 首頁/文章列表模板
- `post.html` - 單篇文章模板

### 可用變數
**文章頁面**
``` handlebars
{{Title}}              <!-- 文章標題 -->
{{Content}}            <!-- HTML 內容 -->
{{PublishedDate}}      <!-- 發布日期 (yyyy-MM-dd) -->
{{PublishedDateLong}}  <!-- 發布日期 (yyyy年MM月dd日) -->
{{Slug}}               <!-- 文章的 slug -->
{{Categories}}         <!-- 分類（HTML 格式） -->
{{CategoriesPlain}}    <!-- 分類（純文字） -->
{{Tags}}               <!-- 標籤（HTML 格式） -->
{{TagsPlain}}          <!-- 標籤（純文字） -->
```
**首頁**
``` handlebars
{{PostCount}}          <!-- 文章總數 -->
{{#each Posts}}        <!-- 遍歷文章 -->
  {{Title}}
  {{Excerpt}}
  {{PublishedDate}}
  {{Categories}}
  {{Tags}}
{{/each}}
{{CategoryTree}}       <!-- 分類樹狀結構 HTML -->
```
### 條件語法
``` handlebars
{{#if Categories}}     <!-- 當有分類時顯示 -->
  分類：{{Categories}}
{{/if}}

{{#if Tags}}          <!-- 當有標籤時顯示 -->
  標籤：{{Tags}}
{{/if}}
```
### 自定義模板
1. 在 `templates/` 目錄建立同名檔案
2. 系統將自動使用您的自定義模板
3. 支援部分覆蓋，可只自定義需要的部分

**範例：自定義文章模板**
``` html
<!DOCTYPE html>
<html>
<head>
    <title>{{Title}}</title>
</head>
<body>
    <article>
        <h1>{{Title}}</h1>
        <time>{{PublishedDateLong}}</time>
        {{#if Categories}}
            <div class="categories">{{Categories}}</div>
        {{/if}}
        <div class="content">{{Content}}</div>
    </article>
</body>
</html>
```
## 自動化建置
### Windows
``` cmd
release.bat
```
### Linux / macOS
``` bash
chmod +x release.sh
./release.sh
```
### 支援平台
- Windows x64/ARM64
- Linux x64/ARM64
- macOS Intel/Apple Silicon

### 建置功能
建置腳本會自動：
- 產生所有平台的單一執行檔
- 最佳化檔案大小
- 包含完整的模板和設定範例
- 建立帶日期的發布目錄

### 發布目錄結構
``` 
releases/ 
└── 20241201/                      # 發布日期
    ├── BUILD_INFO.txt              # 建置資訊
    ├── velo-v1.0.0-win-x64.zip     # Windows x64 版本
    ├── velo-v1.0.0-win-arm64.zip   # Windows ARM64 版本
    ├── velo-v1.0.0-linux-x64.zip   # Linux x64 版本
    ├── velo-v1.0.0-linux-arm64.zip # Linux ARM64 版本
    ├── velo-v1.0.0-osx-x64.zip     # macOS Intel 版本
    └── velo-v1.0.0-osx-arm64.zip   # macOS Apple Silicon 版本
```
## 使用場景
### 個人部落格
``` json
{
  "BlogContentPath": "~/Documents/MyBlog",
  "BlogSettings": {
    "HtmlOutputPath": "~/Sites/blog"
  }
}
```
### 技術文件
``` json
{
  "BlogContentPath": "./docs",
  "BlogSettings": {
    "HtmlOutputPath": "./dist",
    "AutoAddYamlHeader": false
  }
}
```
### 多語言部落格
``` json
{
  "BlogContentPath": "./content",
  "BlogSettings": {
    "HtmlOutputPath": "./public",
    "TemplatePath": "./themes/multilang"
  }
}
```
## 內建功能詳解
### 搜尋系統
- 即時搜尋文章標題、分類和標籤
- 支援中英文搜尋
- 搜尋結果計數顯示
- 關鍵字高亮顯示

### 分類系統
- 支援階層式分類結構
- 可展開/收合的分類樹
- 點擊分類篩選文章
- 分類文章數量顯示

### 視覺設計
- **卡片式設計**：每篇文章都以獨立卡片呈現
- **漸層效果**：標籤、分類和按鈕使用漸層色彩
- **陰影效果**：立體感的視覺層次
- **Hover 動畫**：滑鼠懸停時的互動回饋

### 響應式設計
- **桌面版**：雙欄佈局，側邊欄固定
- **平板版**：自適應欄位寬度
- **手機版**：單欄佈局，側邊欄置頂

## 技術規格
### 核心技術
- **語言**: C# 13.0
- **框架**: .NET 9.0
- **架構**: 依賴注入 + 服務導向

### 主要套件
- `Markdig` - Markdown 解析引擎
- `YamlDotNet` - YAML/Front Matter 處理
- `Microsoft.Extensions.*` - 設定管理與依賴注入

### 效能特色
- 高速 Markdown 解析
- 智能檔案快取
- 增量式更新
- 記憶體最佳化

### 支援格式
- **輸入**: Markdown (.md), YAML Front Matter
- **輸出**: HTML5, CSS3, JavaScript
- **圖片**: 自動複製並最佳化路徑
- **編碼**: UTF-8 全面支援

## 貢獻指南
我們歡迎任何形式的貢獻！
### 如何貢獻
1. Fork 專案
2. 建立功能分支 (`git checkout -b feature/amazing-feature`)
3. 提交變更 (`git commit -m 'Add amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 建立 Pull Request

### 開發環境設定
``` bash
# Clone 專案
git clone https://github.com/leoshiang/velo.git
cd velo

# 安裝相依性
dotnet restore

# 執行開發版本
dotnet run

# 執行測試
dotnet test
```
### 程式碼規範
- 使用 C# 程式碼慣例
- 加入適當的 XML 文件註解
- 遵循 SOLID 原則
- 撰寫單元測試

## 常見問題
### Q: 如何自定義文章 URL 結構？
A: 目前 URL 結構基於檔案名稱，未來版本將支援自定義。
### Q: 支援哪些 Markdown 擴展語法？
A: 支援 GitHub Flavored Markdown，包括表格、程式碼區塊、刪除線等。
### Q: 可以使用自己的 CSS 樣式嗎？
A: 可以，通過自定義模板引入您的 CSS 檔案。
### Q: 如何處理大量文章的效能問題？
A: Velo 使用增量更新和智能快取，可有效處理大量文章。
## 待辦功能
- [ ] 進階搜尋功能
- [ ] 深色模式支援
- [ ] 文章統計分析
- [ ] 社群媒體整合
- [ ] PWA 支援
- [ ] 多語言支援
- [ ] SEO 最佳化
- [ ] 留言系統整合
- [ ] 自定義 URL 結構
- [ ] 文章草稿功能

## 授權條款
本專案採用 [MIT License](LICENSE.txt) 授權。
