using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Velo.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Velo.Services;

public class BlogService(ILogger<BlogService> logger, IConfiguration configuration) : IBlogService
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly List<BlogPost> _posts = [];
    private readonly object _sync = new();
    private bool _isLoaded;

    public async Task ForceScanAndReloadPostsAsync()
    {
        logger.LogInformation("強制重新掃描並載入所有文章");
        lock (_sync)
        {
            _posts.Clear();
            _isLoaded = false;
        }

        await LoadPostsAsync();
    }

    public async Task<IEnumerable<BlogPost>> GetAllPostsAsync()
    {
        await EnsureLoadedAsync();
        lock (_sync)
        {
            return _posts.OrderByDescending(p => p.PublishedDate).ToList();
        }
    }

    public async Task<CategoryNode> GetCategoryTreeAsync()
    {
        if (!_isLoaded)
        {
            await LoadPostsAsync();
        }

        var root = new CategoryNode
        {
            Name = "Root",
            PathSegment = "",
            Children = new List<CategoryNode>()
        };

        lock (_sync)
        {
            var allPosts = _posts.ToList();

            foreach (var post in allPosts)
            {
                if (post.Categories.Count == 0)
                {
                    continue;
                }

                var currentNode = root;
                var currentPath = "";

                foreach (var category in post.Categories)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? category : $"{currentPath}/{category}";

                    var existingChild = currentNode.Children.FirstOrDefault(c => c.Name == category);

                    if (existingChild == null)
                    {
                        existingChild = new CategoryNode
                        {
                            Name = category,
                            PathSegment = currentPath,
                            Children = new List<CategoryNode>(),
                            PostCount = 0
                        };
                        currentNode.Children.Add(existingChild);
                    }

                    currentNode = existingChild;
                }

                // 增加最終分類的文章計數
                currentNode.PostCount++;
            }
        }

        // 遞歸計算每個節點的總文章數（包括子分類）
        UpdatePostCounts(root);

        return root;
    }

    public async Task<BlogPost?> GetPostBySlugAsync(string slug)
    {
        await EnsureLoadedAsync();
        lock (_sync)
        {
            return _posts.FirstOrDefault(p => p.Slug == slug);
        }
    }

    public async Task<IEnumerable<BlogPost>> SearchPostsAsync(string query)
    {
        if (!_isLoaded)
        {
            await LoadPostsAsync();
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllPostsAsync();
        }

        var searchTerm = query.ToLower();

        lock (_sync)
        {
            return _posts.Where(p => p.Title.ToLower().Contains(searchTerm) ||
                                     p.ContentHtml.ToLower().Contains(searchTerm) ||
                                     p.Tags.Any(t => t.ToLower().Contains(searchTerm)) ||
                                     p.Categories.Any(c => c.ToLower().Contains(searchTerm)))
                .OrderByDescending(p => p.PublishedDate)
                .ToList();
        }
    }

    private string AddDefaultYamlHeader(string content, string fileName, List<string> directoryCategories)
    {
        // 確保檔案名稱中的特殊字元被正確處理
        var safeTitle = fileName.Replace("\"", "\\\"");

        var categoriesYaml = directoryCategories.Count > 0
            ? $"[{string.Join(", ", directoryCategories.Select(c => $"\"{c.Replace("\"", "\\\"")}\""))}]"
            : "[]";

        // 使用新的英文 slug 生成邏輯
        var englishSlug = GenerateSlug(fileName);

        var defaultYamlHeader = $@"---
title: ""{safeTitle}""
date: {DateTime.Now:yyyy-MM-dd}
author: """"
tags: []
categories: {categoriesYaml}
slug: {englishSlug}
draft: false
---

";

        return defaultYamlHeader + content;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded) return; // 進入臨界區後再次確認
            await LoadPostsAsync();
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// 根據檔案路徑生成分類列表
    /// </summary>
    private List<string> GenerateCategoriesFromPath(string filePath)
    {
        var categories = new List<string>();

        try
        {
            var contentPath = configuration["BlogContentPath"];
            if (string.IsNullOrEmpty(contentPath))
            {
                return categories;
            }

            // 將路徑正規化
            var normalizedContentPath = Path.GetFullPath(contentPath);
            var normalizedFilePath = Path.GetFullPath(filePath);

            // 確保檔案在內容目錄中
            if (!normalizedFilePath.StartsWith(normalizedContentPath))
            {
                return categories;
            }

            // 取得相對路徑
            var relativePath = Path.GetRelativePath(normalizedContentPath, normalizedFilePath);

            // 取得目錄部分（不包含檔案名稱）
            var directoryPath = Path.GetDirectoryName(relativePath);

            if (string.IsNullOrEmpty(directoryPath) || directoryPath == ".")
            {
                // 檔案在根目錄，沒有分類
                return categories;
            }

            // 將目錄路徑分割成分類
            var pathSegments = directoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(segment => !string.IsNullOrEmpty(segment))
                .ToList();

            // 過濾掉不適合作為分類的目錄名稱
            var excludedDirectories = configuration.GetSection("BlogSettings:ExcludedDirectories").Get<string[]>()
                                      ?? ["drafts", "temp", "tmp", ".git", ".vs", "bin", "obj"];

            categories = pathSegments
                .Where(segment => !excludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase))
                .ToList();

            logger.LogDebug("檔案 {FilePath} 的目錄分類: [{Categories}]",
                filePath, string.Join(", ", categories));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "無法從路徑 {FilePath} 生成分類", filePath);
        }

        return categories;
    }

    private string GenerateSlug(string title)
    {
        if (string.IsNullOrEmpty(title))
            return Guid.NewGuid().ToString("N")[..8];

        // 首先處理中文轉英文的基本映射
        var slug = TransliterateToEnglish(title);

        // 轉換為小寫
        slug = slug.ToLower();

        // 移除非字母數字字符（只保留英文字母、數字、空格和連字符）
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");

        // 將空格替換為連字符
        slug = Regex.Replace(slug, @"\s+", "-");

        // 移除重複的連字符
        slug = Regex.Replace(slug, @"-+", "-");

        // 移除開頭和結尾的連字符
        slug = slug.Trim('-');

        // 如果結果為空或太短，使用基於日期的隨機字符串
        if (string.IsNullOrEmpty(slug) || slug.Length < 3)
        {
            var datePrefix = DateTime.Now.ToString("yyyyMMdd");
            var randomSuffix = Guid.NewGuid().ToString("N")[..4];
            slug = $"post-{datePrefix}-{randomSuffix}";
        }

        return slug;
    }

    private bool HasYamlFrontmatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // 檢查是否以 --- 開頭
        content = content.TrimStart();
        if (!content.StartsWith("---"))
            return false;

        // 找到第一個 --- 後面的內容
        var firstDelimiterIndex = content.IndexOf("---", StringComparison.Ordinal);
        if (firstDelimiterIndex == -1)
            return false;

        // 找到第二個 --- 
        var secondDelimiterIndex = content.IndexOf("---", firstDelimiterIndex + 3, StringComparison.Ordinal);

        return secondDelimiterIndex != -1;
    }

    private async Task LoadPostsAsync()
    {
        var contentPath = configuration["BlogContentPath"];

        if (string.IsNullOrEmpty(contentPath))
        {
            throw new InvalidOperationException("BlogContentPath 設定不能為空");
        }

        if (!Directory.Exists(contentPath))
        {
            throw new DirectoryNotFoundException($"內容目錄不存在: {contentPath}");
        }

        var posts = new List<BlogPost>();
        var markdownFiles = Directory.GetFiles(contentPath, "*.md", SearchOption.AllDirectories);

        logger.LogInformation("開始載入文章，找到 {Count} 個 Markdown 檔案", markdownFiles.Length);

        foreach (var file in markdownFiles)
        {
            try
            {
                var post = await ParseMarkdownFileAsync(file);
                if (post != null)
                {
                    posts.Add(post);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "處理檔案時發生錯誤: {FilePath}", file);
            }
        }

        lock (_sync)
        {
            _posts.Clear();
            _posts.AddRange(posts);
            _isLoaded = true;
        }

        logger.LogInformation("文章載入完成，共 {Count} 篇", posts.Count);
    }

    private async Task<BlogPost?> ParseMarkdownFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // 根據目錄結構自動生成分類
            var directoryCategories = GenerateCategoriesFromPath(filePath);

            // 檢查是否有 YAML 標頭
            if (!HasYamlFrontmatter(content))
            {
                // 如果設定中啟用了自動添加 YAML 標頭的選項
                var autoAddYamlHeader = configuration.GetValue("BlogSettings:AutoAddYamlHeader", false);

                if (autoAddYamlHeader)
                {
                    content = AddDefaultYamlHeader(content, fileName, directoryCategories);

                    // 如果設定中啟用了自動儲存修改的選項
                    var autoSaveModified = configuration.GetValue("BlogSettings:AutoSaveModified", false);
                    if (autoSaveModified)
                    {
                        await File.WriteAllTextAsync(filePath, content);
                        logger.LogInformation("已為檔案 {FilePath} 添加 YAML 標頭並儲存", filePath);
                    }
                    else
                    {
                        logger.LogInformation("已為檔案 {FilePath} 添加 YAML 標頭（僅在記憶體中）", filePath);
                    }
                }
            }

            var pipeline = new MarkdownPipelineBuilder()
                .UseYamlFrontMatter()
                .UseAdvancedExtensions()
                .Build();

            var document = Markdown.Parse(content, pipeline);
            var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

            // 移除 YAML 標頭後的內容
            var contentWithoutYaml = content;
            if (yamlBlock != null)
            {
                var yamlEndIndex = content.IndexOf("---", 4, StringComparison.Ordinal);
                if (yamlEndIndex != -1)
                {
                    contentWithoutYaml = content.Substring(yamlEndIndex + 3).TrimStart();
                }
            }

            var blogPost = new BlogPost
            {
                Title = fileName,
                Slug = GenerateSlug(fileName),
                SourceFilePath = filePath,
                ContentHtml = contentWithoutYaml,
                PublishedDate = File.GetLastWriteTime(filePath),
                Tags = new List<string>(),
                Categories = [..directoryCategories], // 使用目錄結構分類
                HtmlFileId = Guid.NewGuid().ToString("N")
            };

            if (yamlBlock != null)
            {
                var yamlContent = content.Substring(yamlBlock.Span.Start, yamlBlock.Span.Length);

                // 移除 YAML 分隔符號
                yamlContent = yamlContent.Replace("---", "").Trim();

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();

                try
                {
                    var frontMatter = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

                    // 解析 YAML 標頭
                    if (frontMatter.TryGetValue("title", out var title))
                        blogPost.Title = title.ToString()!;

                    if (frontMatter.TryGetValue("date", out var date))
                    {
                        if (DateTime.TryParse(date.ToString(), out var parsedDate))
                            blogPost.PublishedDate = parsedDate;
                    }

                    if (frontMatter.TryGetValue("author", out var author))
                        blogPost.Author = author.ToString();

                    if (frontMatter.TryGetValue("tags", out var tags))
                    {
                        blogPost.Tags = ParseTagsOrCategories(tags);
                    }

                    // 處理分類
                    if (frontMatter.TryGetValue("categories", out var categories))
                    {
                        var yamlCategories = ParseTagsOrCategories(categories);

                        // 檢查是否要合併目錄分類
                        var mergeCategories =
                            configuration.GetValue("BlogSettings:MergeDirectoryCategories", true);

                        if (mergeCategories && yamlCategories.Count > 0)
                        {
                            // 合併並去重
                            var allCategories = new List<string>(directoryCategories);
                            allCategories.AddRange(yamlCategories);
                            blogPost.Categories = allCategories.Distinct().ToList();
                        }
                        else if (yamlCategories.Count > 0)
                        {
                            // 只使用 YAML 分類
                            blogPost.Categories = yamlCategories;
                        }
                        // 如果 YAML 分類為空，則保持使用目錄分類
                    }
                    else if (frontMatter.TryGetValue("category", out var category))
                    {
                        // 支援單一分類
                        var categoryStr = category.ToString()!;
                        if (!string.IsNullOrEmpty(categoryStr))
                        {
                            var yamlCategories = categoryStr.Split('/')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();

                            var mergeCategories =
                                configuration.GetValue("BlogSettings:MergeDirectoryCategories", true);

                            if (mergeCategories && yamlCategories.Count > 0)
                            {
                                var allCategories = new List<string>(directoryCategories);
                                allCategories.AddRange(yamlCategories);
                                blogPost.Categories = allCategories.Distinct().ToList();
                            }
                            else if (yamlCategories.Count > 0)
                            {
                                blogPost.Categories = yamlCategories;
                            }
                        }
                    }
                    // 如果 YAML 中沒有分類，則使用目錄分類（已在上面設定）

                    if (frontMatter.TryGetValue("slug", out var slug))
                        blogPost.Slug = slug.ToString()!;

                    // 草稿功能
                    var isDraft = filePath.Contains("drafts") || fileName.StartsWith("draft-");
                    if (frontMatter.TryGetValue("draft", out var draft))
                    {
                        if (bool.TryParse(draft.ToString(), out var draftValue))
                            isDraft = draftValue;
                    }

                    if (isDraft)
                    {
                        logger.LogInformation("跳過草稿檔案: {FilePath}", filePath);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "無法解析 YAML 標頭: {FilePath}", filePath);
                }
            }

            // 確保 slug 是有效的
            if (string.IsNullOrEmpty(blogPost.Slug))
            {
                blogPost.Slug = GenerateSlug(blogPost.Title);
            }

            // 記錄分類資訊
            logger.LogInformation("文章 {Title} 的分類: [{Categories}]",
                blogPost.Title, string.Join(", ", blogPost.Categories));

            return blogPost;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "解析 Markdown 檔案時發生錯誤: {FilePath}", filePath);
            return null;
        }
    }

    private List<string> ParseTagsOrCategories(object value)
    {
        return value switch
        {
            List<object> list => list.Select(item => item.ToString()!).ToList(),
            string str when !string.IsNullOrEmpty(str) => str.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList(),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// 將中文和其他語言轉換為英文
    /// </summary>
    private string TransliterateToEnglish(string input)
    {
        // 基本的中文到英文映射
        var chineseToEnglish = new Dictionary<string, string>
        {
            // 常用技術詞彙
            { "技術", "tech" },
            { "文章", "article" },
            { "筆記", "note" },
            { "學習", "learn" },
            { "教學", "tutorial" },
            { "指南", "guide" },
            { "入門", "intro" },
            { "進階", "advanced" },
            { "基礎", "basic" },
            { "概念", "concept" },
            { "實作", "practice" },
            { "範例", "example" },
            { "分享", "share" },
            { "心得", "experience" },
            { "開發", "development" },
            { "程式", "programming" },
            { "程式碼", "code" },
            { "專案", "project" },
            { "工具", "tool" },
            { "框架", "framework" },
            { "函式庫", "library" },
            { "資料庫", "database" },
            { "伺服器", "server" },
            { "前端", "frontend" },
            { "後端", "backend" },
            { "全端", "fullstack" },
            { "網頁", "web" },
            { "應用程式", "application" },
            { "手機", "mobile" },
            { "桌面", "desktop" },
            { "雲端", "cloud" },
            { "安全", "security" },
            { "測試", "test" },
            { "部署", "deployment" },
            { "維護", "maintenance" },
            { "最佳化", "optimization" },
            { "效能", "performance" },
            { "設計", "design" },
            { "架構", "architecture" },
            { "模式", "pattern" },
            { "演算法", "algorithm" },
            { "資料結構", "data-structure" },
            { "網路", "network" },
            { "協定", "protocol" },
            { "介面", "interface" },
            { "使用者", "user" },
            { "系統", "system" },
            { "平台", "platform" },
            { "環境", "environment" },
            { "設定", "config" },
            { "配置", "configuration" },
            { "安裝", "installation" },
            { "建置", "build" },
            { "編譯", "compile" },
            { "執行", "run" },
            { "除錯", "debug" },
            { "錯誤", "error" },
            { "問題", "issue" },
            { "解決", "solution" },
            { "方法", "method" },
            { "技巧", "tip" },
            { "訣竅", "trick" },
            { "小技巧", "tips" },
            { "常見問題", "faq" },
            { "疑難排解", "troubleshooting" },

            // 日期和時間
            { "年", "year" },
            { "月", "month" },
            { "日", "day" },
            { "週", "week" },
            { "今天", "today" },
            { "昨天", "yesterday" },
            { "明天", "tomorrow" },

            // 常用詞彙
            { "新", "new" },
            { "舊", "old" },
            { "最新", "latest" },
            { "更新", "update" },
            { "版本", "version" },
            { "發布", "release" },
            { "介紹", "introduction" },
            { "總結", "summary" },
            { "結論", "conclusion" },
            { "開始", "start" },
            { "結束", "end" },
            { "完成", "complete" },
            { "成功", "success" },
            { "失敗", "failure" },
            { "是", "yes" },
            { "否", "no" },
            { "和", "and" },
            { "或", "or" },
            { "不", "not" },
            { "沒有", "no" },
            { "有", "has" },
            { "可以", "can" },
            { "不能", "cannot" },
            { "必須", "must" },
            { "應該", "should" },
            { "可能", "maybe" },
            { "也許", "perhaps" },
            { "但是", "but" },
            { "然而", "however" },
            { "因此", "therefore" },
            { "所以", "so" },
            { "如果", "if" },
            { "當", "when" },
            { "為什麼", "why" },
            { "如何", "how" },
            { "什麼", "what" },
            { "哪裡", "where" },
            { "誰", "who" },
            { "多少", "how-many" },
            { "多少錢", "how-much" },

            // 生活相關
            { "生活", "life" },
            { "工作", "work" },
            { "家庭", "family" },
            { "朋友", "friend" },
            { "旅行", "travel" },
            { "美食", "food" },
            { "運動", "sport" },
            { "音樂", "music" },
            { "電影", "movie" },
            { "書籍", "book" },
            { "閱讀", "reading" },
            { "寫作", "writing" },
            { "思考", "thinking" },
            { "感想", "thought" },
            { "回憶", "memory" },
            { "夢想", "dream" },
            { "計劃", "plan" },
            { "目標", "goal" },
            { "挑戰", "challenge" },
            { "成長", "growth" },
            { "進步", "progress" },
            { "改變", "change" },
            { "機會", "opportunity" },
            { "選擇", "choice" },
            { "決定", "decision" },
            { "重要", "important" },
            { "有趣", "interesting" },
            { "困難", "difficult" },
            { "簡單", "simple" },
            { "複雜", "complex" },
            { "好", "good" },
            { "壞", "bad" },
            { "棒", "great" },
            { "差", "poor" },
            { "快", "fast" },
            { "慢", "slow" },
            { "大", "big" },
            { "小", "small" },
            { "高", "high" },
            { "低", "low" },
            { "多", "many" },
            { "少", "few" },
            { "長", "long" },
            { "短", "short" },
            { "深", "deep" },
            { "淺", "shallow" },
            { "遠", "far" },
            { "近", "near" },
            { "早", "early" },
            { "晚", "late" },
            { "熱", "hot" },
            { "冷", "cold" },
            { "亮", "bright" },
            { "暗", "dark" },
            { "美", "beautiful" },
            { "醜", "ugly" },
            { "乾淨", "clean" },
            { "髒", "dirty" },
            { "健康", "healthy" },
            { "生病", "sick" },
            { "快樂", "happy" },
            { "悲傷", "sad" },
            { "生氣", "angry" },
            { "害怕", "afraid" },
            { "驚訝", "surprised" },
            { "無聊", "boring" },
            { "累", "tired" },
            { "輕鬆", "relaxed" },
            { "緊張", "nervous" },
            { "興奮", "excited" },
            { "平靜", "calm" },
            { "忙碌", "busy" },
            { "空閒", "free" },
            { "忙", "busy" },
            { "閒", "free" },

            // 數字
            { "一", "one" },
            { "二", "two" },
            { "三", "three" },
            { "四", "four" },
            { "五", "five" },
            { "六", "six" },
            { "七", "seven" },
            { "八", "eight" },
            { "九", "nine" },
            { "十", "ten" },
            { "百", "hundred" },
            { "千", "thousand" },
            { "萬", "ten-thousand" },
            { "億", "hundred-million" },

            // 特殊符號轉換
            { "&", "and" },
            { "@", "at" },
            { "#", "hash" },
            { "$", "dollar" },
            { "%", "percent" },
            { "*", "star" },
            { "+", "plus" },
            { "=", "equals" },
            { "?", "question" },
            { "!", "exclamation" },
            { "～", "tilde" },
            { "。", "dot" },
            { "，", "comma" },
            { "；", "semicolon" },
            { "：", "colon" },
            { "「", "quote" },
            { "」", "quote" },
            { "『", "quote" },
            { "』", "quote" },
            { "）", "bracket" },
            { "【", "bracket" },
            { "】", "bracket" },
            { "《", "bracket" },
            { "》", "bracket" },
            { "〈", "bracket" },
            { "〉", "bracket" },
            { "｛", "brace" },
            { "｝", "brace" },
            { "［", "bracket" },
            { "］", "bracket" },
            { "＼", "backslash" },
            { "／", "slash" },
            { "｜", "pipe" },
            { "－", "dash" },
            { "—", "dash" },
            { "…", "ellipsis" },
            { "‧", "dot" },
            { "·", "dot" },
            { "•", "bullet" },
            { "◦", "bullet" },
            { "▪", "bullet" },
            { "▫", "bullet" },
            { "◆", "diamond" },
            { "◇", "diamond" },
            { "■", "square" },
            { "□", "square" },
            { "●", "circle" },
            { "○", "circle" },
            { "★", "star" },
            { "☆", "star" },
            { "♠", "spade" },
            { "♥", "heart" },
            { "♦", "diamond" },
            { "♣", "club" },
            { "♪", "note" },
            { "♫", "note" },
            { "♬", "note" },
            { "♭", "flat" },
            { "♮", "natural" },
            { "♯", "sharp" },
            { "℃", "celsius" },
            { "℉", "fahrenheit" },
            { "℅", "care-of" },
            { "№", "number" },
            { "℡", "telephone" },
            { "™", "trademark" },
            { "©", "copyright" },
            { "®", "registered" },
            { "§", "section" },
            { "¶", "paragraph" },
            { "†", "dagger" },
            { "‡", "double-dagger" },
            { "‰", "per-mille" },
            { "′", "prime" },
            { "″", "double-prime" },
            { "‴", "triple-prime" },
            { "‹", "angle-bracket" },
            { "›", "angle-bracket" },
            { "«", "angle-bracket" },
            { "»", "angle-bracket" },
            { "‚", "comma" },
            { "„", "comma" },
            { "'", "apostrophe" },
            { "\"", "quote" },
            { "‥", "ellipsis" },
            { "‱", "per-ten-thousand" },
            { "‾", "overline" },
            { "‿", "undertie" },
            { "⁀", "character-tie" },
            { "⁁", "caret" },
            { "⁂", "asterism" },
            { "⁃", "bullet" },
            { "⁄", "fraction-slash" },
            { "⁅", "bracket" },
            { "⁆", "bracket" },
            { "⁇", "question" },
            { "⁈", "question" },
            { "⁉", "exclamation" },
            { "⁊", "tironian-et" },
            { "⁋", "pilcrow" },
            { "⁌", "black-leftwards-bullet" },
            { "⁍", "black-rightwards-bullet" },
            { "⁎", "asterisk" },
            { "⁏", "reversed-semicolon" },
            { "⁐", "close-up" },
            { "⁑", "asterism" },
            { "⁒", "commercial-minus" },
            { "⁓", "swung-dash" },
            { "⁔", "inverted-undertie" },
            { "⁕", "asterisk" },
            { "⁖", "double-question" },
            { "⁗", "double-exclamation" },
            { "⁘", "quadruple-prime" },
            { "⁙", "five-dot-punctuation" },
            { "⁚", "two-dot-punctuation" },
            { "⁛", "four-dot-punctuation" },
            { "⁜", "dotted-cross" },
            { "⁝", "tricolon" },
            { "⁞", "vertical-four-dots" },
            { " ", "medium-mathematical-space" },
            { "⁠", "word-joiner" },
            { "⁡", "function-application" },
            { "⁢", "invisible-times" },
            { "⁣", "invisible-separator" },
            { "⁤", "invisible-plus" },
            { "⁥", "left-to-right-mark" },
            { "⁦", "left-to-right-embedding" },
            { "⁧", "right-to-left-embedding" },
            { "⁨", "pop-directional-formatting" },
            { "⁩", "left-to-right-override" },
            { "⁪", "right-to-left-override" },
            { "⁫", "word-joiner" },
            { "⁬", "invisible-times" },
            { "⁭", "invisible-separator" },
            { "⁮", "invisible-plus" },
            { "⁯", "narrow-no-break-space" },
            { "﹏", "wavy-low-line" },
            { "﹐", "small-comma" },
            { "﹑", "small-ideographic-comma" },
            { "﹒", "small-full-stop" },
            { "﹔", "small-semicolon" },
            { "﹕", "small-colon" },
            { "﹖", "small-question-mark" },
            { "﹗", "small-exclamation-mark" },
            { "﹘", "small-em-dash" },
            { "﹙", "small-left-parenthesis" },
            { "﹚", "small-right-parenthesis" },
            { "﹛", "small-left-curly-bracket" },
            { "﹜", "small-right-curly-bracket" },
            { "﹝", "small-tortoise-shell-bracket" },
            { "﹞", "small-tortoise-shell-bracket" },
            { "﹟", "small-number-sign" },
            { "﹠", "small-ampersand" },
            { "﹡", "small-asterisk" },
            { "﹢", "small-plus-sign" },
            { "﹣", "small-hyphen-minus" },
            { "﹤", "small-less-than-sign" },
            { "﹥", "small-greater-than-sign" },
            { "﹦", "small-equals-sign" },
            { "﹨", "small-reverse-solidus" },
            { "﹩", "small-dollar-sign" },
            { "﹪", "small-percent-sign" },
            { "﹫", "small-commercial-at" },
            { "！", "exclamation" },
            { "＂", "quotation-mark" },
            { "＃", "number-sign" },
            { "＄", "dollar-sign" },
            { "％", "percent-sign" },
            { "＆", "ampersand" },
            { "＇", "apostrophe" },
            { "（", "left-parenthesis" },
            { "＊", "asterisk" },
            { "＋", "plus-sign" },
            { "．", "full-stop" },
            { "０", "zero" },
            { "１", "one" },
            { "２", "two" },
            { "３", "three" },
            { "４", "four" },
            { "５", "five" },
            { "６", "six" },
            { "７", "seven" },
            { "８", "eight" },
            { "９", "nine" },
            { "＜", "less-than-sign" },
            { "＝", "equals-sign" },
            { "＞", "greater-than-sign" },
            { "？", "question-mark" },
            { "＠", "commercial-at" },
            { "Ａ", "A" },
            { "Ｂ", "B" },
            { "Ｃ", "C" },
            { "Ｄ", "D" },
            { "Ｅ", "E" },
            { "Ｆ", "F" },
            { "Ｇ", "G" },
            { "Ｈ", "H" },
            { "Ｉ", "I" },
            { "Ｊ", "J" },
            { "Ｋ", "K" },
            { "Ｌ", "L" },
            { "Ｍ", "M" },
            { "Ｎ", "N" },
            { "Ｏ", "O" },
            { "Ｐ", "P" },
            { "Ｑ", "Q" },
            { "Ｒ", "R" },
            { "Ｓ", "S" },
            { "Ｔ", "T" },
            { "Ｕ", "U" },
            { "Ｖ", "V" },
            { "Ｗ", "W" },
            { "Ｘ", "X" },
            { "Ｙ", "Y" },
            { "Ｚ", "Z" },
            { "＾", "circumflex-accent" },
            { "＿", "low-line" },
            { "｀", "grave-accent" },
            { "ａ", "a" },
            { "ｂ", "b" },
            { "ｃ", "c" },
            { "ｄ", "d" },
            { "ｅ", "e" },
            { "ｆ", "f" },
            { "ｇ", "g" },
            { "ｈ", "h" },
            { "ｉ", "i" },
            { "ｊ", "j" },
            { "ｋ", "k" },
            { "ｌ", "l" },
            { "ｍ", "m" },
            { "ｎ", "n" },
            { "ｏ", "o" },
            { "ｐ", "p" },
            { "ｑ", "q" },
            { "ｒ", "r" },
            { "ｓ", "s" },
            { "ｔ", "t" },
            { "ｕ", "u" },
            { "ｖ", "v" },
            { "ｗ", "w" },
            { "ｘ", "x" },
            { "ｙ", "y" },
            { "ｚ", "z" },
            { "｟", "left-white-parenthesis" },
            { "｠", "right-white-parenthesis" },
            { "｡", "halfwidth-ideographic-full-stop" },
            { "｢", "halfwidth-left-corner-bracket" },
            { "｣", "halfwidth-right-corner-bracket" },
            { "､", "halfwidth-ideographic-comma" },
            { "･", "halfwidth-katakana-middle-dot" },
            { "ｦ", "halfwidth-katakana-wo" },
            { "ｧ", "halfwidth-katakana-small-a" },
            { "ｨ", "halfwidth-katakana-small-i" },
            { "ｩ", "halfwidth-katakana-small-u" },
            { "ｪ", "halfwidth-katakana-small-e" },
            { "ｫ", "halfwidth-katakana-small-o" },
            { "ｬ", "halfwidth-katakana-small-ya" },
            { "ｭ", "halfwidth-katakana-small-yu" },
            { "ｮ", "halfwidth-katakana-small-yo" },
            { "ｯ", "halfwidth-katakana-small-tu" },
            { "ｰ", "halfwidth-katakana-hiragana-prolonged-sound-mark" },
            { "ｱ", "halfwidth-katakana-a" },
            { "ｲ", "halfwidth-katakana-i" },
            { "ｳ", "halfwidth-katakana-u" },
            { "ｴ", "halfwidth-katakana-e" },
            { "ｵ", "halfwidth-katakana-o" },
            { "ｶ", "halfwidth-katakana-ka" },
            { "ｷ", "halfwidth-katakana-ki" },
            { "ｸ", "halfwidth-katakana-ku" },
            { "ｹ", "halfwidth-katakana-ke" },
            { "ｺ", "halfwidth-katakana-ko" },
            { "ｻ", "halfwidth-katakana-sa" },
            { "ｼ", "halfwidth-katakana-si" },
            { "ｽ", "halfwidth-katakana-su" },
            { "ｾ", "halfwidth-katakana-se" },
            { "ｿ", "halfwidth-katakana-so" },
            { "ﾀ", "halfwidth-katakana-ta" },
            { "ﾁ", "halfwidth-katakana-ti" },
            { "ﾂ", "halfwidth-katakana-tu" },
            { "ﾃ", "halfwidth-katakana-te" },
            { "ﾄ", "halfwidth-katakana-to" },
            { "ﾅ", "halfwidth-katakana-na" },
            { "ﾆ", "halfwidth-katakana-ni" },
            { "ﾇ", "halfwidth-katakana-nu" },
            { "ﾈ", "halfwidth-katakana-ne" },
            { "ﾉ", "halfwidth-katakana-no" },
            { "ﾊ", "halfwidth-katakana-ha" },
            { "ﾋ", "halfwidth-katakana-hi" },
            { "ﾌ", "halfwidth-katakana-hu" },
            { "ﾍ", "halfwidth-katakana-he" },
            { "ﾎ", "halfwidth-katakana-ho" },
            { "ﾏ", "halfwidth-katakana-ma" },
            { "ﾐ", "halfwidth-katakana-mi" },
            { "ﾑ", "halfwidth-katakana-mu" },
            { "ﾒ", "halfwidth-katakana-me" },
            { "ﾓ", "halfwidth-katakana-mo" },
            { "ﾔ", "halfwidth-katakana-ya" },
            { "ﾕ", "halfwidth-katakana-yu" },
            { "ﾖ", "halfwidth-katakana-yo" },
            { "ﾗ", "halfwidth-katakana-ra" },
            { "ﾘ", "halfwidth-katakana-ri" },
            { "ﾙ", "halfwidth-katakana-ru" },
            { "ﾚ", "halfwidth-katakana-re" },
            { "ﾛ", "halfwidth-katakana-ro" },
            { "ﾜ", "halfwidth-katakana-wa" },
            { "ﾝ", "halfwidth-katakana-n" },
            { "ﾞ", "halfwidth-katakana-voiced-sound-mark" },
            { "ﾟ", "halfwidth-katakana-semi-voiced-sound-mark" },
            { "ￂ", "halfwidth-not-sign" },
            { "ￃ", "halfwidth-broken-bar" },
            { "ￄ", "halfwidth-macron" },
            { "ￅ", "halfwidth-overline" },
            { "ￆ", "halfwidth-yen-sign" },
            { "ￇ", "halfwidth-won-sign" },
            { "ￊ", "halfwidth-forms-light-vertical" },
            { "ￋ", "halfwidth-leftwards-arrow" },
            { "ￌ", "halfwidth-upwards-arrow" },
            { "ￍ", "halfwidth-rightwards-arrow" },
            { "ￎ", "halfwidth-downwards-arrow" },
            { "ￏ", "halfwidth-black-square" },
            { "￐", "halfwidth-white-circle" },
            { "￑", "halfwidth-black-circle" },
            { "ￒ", "halfwidth-black-up-pointing-triangle" },
            { "ￓ", "halfwidth-black-down-pointing-triangle" },
            { "ￔ", "halfwidth-black-left-pointing-triangle" },
            { "ￕ", "halfwidth-black-right-pointing-triangle" },
            { "ￖ", "halfwidth-white-up-pointing-triangle" },
            { "ￗ", "halfwidth-white-down-pointing-triangle" },
            { "￘", "halfwidth-white-left-pointing-triangle" },
            { "￙", "halfwidth-white-right-pointing-triangle" },
            { "ￚ", "halfwidth-black-diamond" },
            { "ￛ", "halfwidth-white-diamond" },
            { "ￜ", "halfwidth-black-small-square" },
            { "￝", "halfwidth-white-small-square" },
            { "￞", "halfwidth-black-rectangle" },
            { "￟", "halfwidth-white-rectangle" },
            { "￠", "halfwidth-black-vertical-rectangle" },
            { "￡", "halfwidth-white-vertical-rectangle" },
            { "￢", "halfwidth-black-parallelogram" },
            { "￣", "halfwidth-white-parallelogram" },
            { "￤", "halfwidth-black-up-pointing-triangle" },
            { "￥", "halfwidth-white-up-pointing-triangle" },
            { "￦", "halfwidth-black-down-pointing-triangle" },
            { "￧", "halfwidth-white-down-pointing-triangle" },
            { "￨", "halfwidth-black-left-pointing-triangle" },
            { "￩", "halfwidth-white-left-pointing-triangle" },
            { "￪", "halfwidth-black-right-pointing-triangle" },
            { "￫", "halfwidth-white-right-pointing-triangle" },
            { "￬", "halfwidth-black-diamond" },
            { "￭", "halfwidth-white-diamond" },
            { "￮", "halfwidth-black-lozenge" },
            { "￯", "halfwidth-white-lozenge" },
            { "￰", "halfwidth-black-star" },
            { "￱", "halfwidth-white-star" },
            { "￲", "halfwidth-black-telephone" },
            { "￳", "halfwidth-white-telephone" },
            { "￴", "halfwidth-black-square-button" },
            { "￵", "halfwidth-white-square-button" },
            { "￶", "halfwidth-black-circle-button" },
            { "￷", "halfwidth-white-circle-button" },
            { "￸", "halfwidth-black-up-pointing-triangle-button" },
            { "￹", "halfwidth-white-up-pointing-triangle-button" },
            { "￺", "halfwidth-black-down-pointing-triangle-button" },
            { "￻", "halfwidth-white-down-pointing-triangle-button" },
            { "￼", "halfwidth-black-left-pointing-triangle-button" },
            { "�", "halfwidth-white-left-pointing-triangle-button" },
            { "￾", "halfwidth-black-right-pointing-triangle-button" },
            { "￿", "halfwidth-white-right-pointing-triangle-button" }
        };

        var result = input;

        // 按照長度排序，優先處理較長的詞彙
        var sortedKeys = chineseToEnglish.Keys.OrderByDescending(k => k.Length);

        foreach (var chineseWord in sortedKeys)
        {
            result = result.Replace(chineseWord, chineseToEnglish[chineseWord]);
        }

        return result;
    }

    private void UpdatePostCounts(CategoryNode node)
    {
        // 遞歸更新子節點
        foreach (var child in node.Children)
        {
            UpdatePostCounts(child);
            // 父節點的計數包括子節點的計數
            node.PostCount += child.PostCount;
        }
    }
}