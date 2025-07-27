``` markdown
### 支援的模板變數

**文章頁面 (post.html):**

**基本資訊**
- `{{Title}}` - 文章標題
- `{{{Content}}}` - 轉換後的 HTML 內容（三個大括號避免 HTML 跳脫）
- `{{PublishedDate}}` - 發佈日期 (yyyy-MM-dd)
- `{{PublishedDateLong}}` - 發佈日期 (yyyy年MM月dd日)
- `{{Slug}}` - 文章的 slug
- `{{Author}}` - 作者姓名

**分類與標籤**
- `{{{Categories}}}` - 分類 (HTML 格式，含 Bootstrap badge 樣式)
- `{{CategoriesPlain}}` - 分類 (純文字，以 " / " 分隔)
- `{{{Tags}}}` - 標籤 (HTML 格式，含 Bootstrap badge 樣式，以 # 開頭)
- `{{TagsPlain}}` - 標籤 (純文字，以 # 開頭，空格分隔)

**圖片相關**
- `{{FirstImageUrl}}` - 第一張圖片的網址
- `{{{FirstImageHTML}}}` - 第一張圖片的完整 HTML 標籤 (含 CSS 類別)
- `{{ImageCount}}` - 文章中的圖片總數
- `{{ImagesJSON}}` - 所有圖片路徑的 JSON 陣列格式
- `{{{ImagesHTML}}}` - 所有圖片的 HTML 標籤串接

**首頁 (index.html):**

**基本資訊**
- `{{PostCount}}` - 文章總數
- `{{GeneratedDate}}` - 網站生成日期時間

**文章列表循環中可用的變數 (在 `{{#each Posts}}...{{/each}}` 區塊內):**
- `{{Title}}` - 文章標題
- `{{Slug}}` - 文章的 slug
- `{{HtmlFilePath}}` - 文章的 HTML 檔案路徑
- `{{PublishedDate}}` - 發佈日期 (yyyy-MM-dd)
- `{{PublishedDateLong}}` - 發佈日期 (yyyy年MM月dd日)
- `{{Author}}` - 作者姓名
- `{{CategoriesPlain}}` - 分類 (純文字)
- `{{TagsPlain}}` - 標籤 (純文字)
- `{{{Categories}}}` - 分類 (HTML 連結格式，含 data-category 屬性)
- `{{FirstImageUrl}}` - 第一張圖片網址
- `{{{FirstImageHTML}}}` - 第一張圖片 HTML
- `{{ImageCount}}` - 圖片數量
- `{{ImagesJSON}}` - 圖片陣列 JSON
- `{{{ImagesHTML}}}` - 所有圖片 HTML

### 支援的條件語法

**文章頁面和首頁共用：**
- `{{#if Categories}}...{{/if}}` - 當有分類時顯示
- `{{#if Tags}}...{{/if}}` - 當有標籤時顯示
- `{{#if Author}}...{{/if}}` - 當有作者資訊時顯示
- `{{#if FirstImage}}...{{/if}}` - 當有第一張圖片時顯示

**首頁專用：**
- `{{#each Posts}}...{{/each}}` - 遍歷所有文章（按發佈日期倒序排列）

### 模板變數使用注意事項

1. **HTML 跳脫**：
   - 使用 `{{變數}}` 會自動跳脫 HTML 字元
   - 使用 `{{{變數}}}` 不會跳脫 HTML，適用於已經是 HTML 格式的內容

2. **圖片相關變數**：
   - `FirstImageUrl` 如果文章沒有圖片會返回空字串
   - `ImageCount` 為 0 時表示沒有圖片
   - `ImagesJSON` 沒有圖片時會返回空陣列 `[]`

3. **條件語法**：
   - 條件判斷會檢查內容是否存在且不為空
   - 支援巢狀的條件語法

4. **CSS 類別**：
   - 分類 HTML 使用 `badge bg-primary` 類別
   - 標籤 HTML 使用 `badge bg-secondary` 類別
   - 圖片 HTML 使用 `post-thumbnail` 和 `post-image` 類別

### 使用範例

**顯示第一張圖片（如果存在）：**
```
html {{#if FirstImage}}
{{{FirstImageHTML}}}
{{/if}}
``` 

**顯示所有圖片：**
```
html {{#if ImageCount}}
共 {{ImageCount}} 張圖片
{{{ImagesHTML}}}
{{/if}}
``` 

**在首頁列表中顯示文章資訊：**
```
html {{#each Posts}}
## [{{Title}}]({{HtmlFilePath}})
{{PublishedDateLong}} {{#if Author}}作者：{{Author}}{{/if}}
{{#if FirstImage}}
{{/if}} {{#if Categories}}
{{{Categories}}}
{{/if}} {{#if Tags}}
{{{Tags}}}
{{/if}}  {{/each}}
article
article
這個更新的文件包含了：
1. **完整的變數列表**：包括基本資訊、分類標籤、圖片相關變數
2. **HTML 跳脫說明**：解釋何時使用 `{{}}` 和 `{{{}}}`
3. **首頁循環變數**：詳細說明在 中可用的所有變數 `{{#each Posts}}`
4. **使用注意事項**：重要的使用細節和限制
5. **實用範例**：展示如何在實際模板中使用這些變數
