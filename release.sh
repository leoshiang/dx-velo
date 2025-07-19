#!/bin/bash

# 設定顏色輸出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# 設定變數
PROJECT_NAME="Velo"
VERSION="1.0.0"
TEMP_DIR="temp_build"

# 產生日期字串 (YYYYMMDD)
BUILD_DATE=$(date +%Y%m%d)
BUILD_TIME=$(date +%H:%M)
BUILD_DIR="releases/${BUILD_DATE}"

echo -e "${BLUE}================================${NC}"
echo -e "${BLUE}     Velo 發布工具 v1.0${NC}"
echo -e "${BLUE}================================${NC}"

# 檢查是否安裝了 .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}❌ 錯誤：未找到 .NET SDK，請先安裝 .NET 9.0 SDK${NC}"
    exit 1
fi

# 檢查是否安裝了 zip
if ! command -v zip &> /dev/null; then
    echo -e "${YELLOW}⚠️  警告：未找到 zip 命令，將只建立目錄，不會建立 ZIP 檔案${NC}"
    echo -e "${YELLOW}   在 Ubuntu/Debian: sudo apt install zip${NC}"
    echo -e "${YELLOW}   在 macOS: zip 已內建${NC}"
    echo -e "${YELLOW}   在 RHEL/CentOS: sudo yum install zip${NC}"
    USE_ZIP=false
else
    USE_ZIP=true
fi

# 建立發布目錄
if [ ! -d "releases" ]; then
    mkdir -p "releases"
fi

if [ -d "$BUILD_DIR" ]; then
    echo -e "${YELLOW}⚠️  警告：目錄 $BUILD_DIR 已存在${NC}"
    read -p "是否要覆蓋現有檔案? (y/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo -e "${YELLOW}取消發布${NC}"
        exit 0
    fi
    echo -e "${CYAN}清理現有目錄...${NC}"
    rm -rf "$BUILD_DIR"
fi

mkdir -p "$BUILD_DIR"

echo
echo -e "${CYAN}建立日期：${BUILD_DATE}${NC}"
echo -e "${CYAN}發布目錄：${BUILD_DIR}${NC}"
echo -e "${CYAN}開始建立發布包...${NC}"
echo

# 定義目標平台陣列
platforms=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")
display_names=(
    "Windows x64"
    "Windows ARM64"
    "Linux x64"
    "Linux ARM64"
    "macOS x64 (Intel)"
    "macOS ARM64 (Apple Silicon)"
)

# 建立建置資訊檔案
create_build_info() {
    cat > "$BUILD_DIR/BUILD_INFO.txt" << EOF
# Velo 發布資訊

建置日期：$BUILD_DATE
建置時間：$BUILD_TIME
版本：$VERSION
建置系統：$(uname -s) $(uname -m)

## 包含的平台

- Windows x64
- Windows ARM64
- Linux x64
- Linux ARM64
- macOS x64 (Intel)
- macOS ARM64 (Apple Silicon)

## 檔案說明

每個 ZIP 包含：
- 單一執行檔 (無需額外依賴)
- Templates 目錄 (模板檔案)
- 範例設定檔
- README 使用說明

直接解壓即可使用！
EOF
}

# 建立 README 檔案
create_readme() {
    local readme_dir="$1"
    local platform_name="$2"
    local exe_name="$3"
    
    cat > "$readme_dir/README.txt" << EOF
# Velo - 靜態部落格生成器

版本：$VERSION
建置日期：$BUILD_DATE
平台：$platform_name

## 快速開始

1. 建立 \`velo.config.json\` 設定檔（可參考 \`velo.config.json.example\`）
2. 建立 \`Posts\` 目錄並放置 Markdown 檔案
3. 執行程式：
EOF

    if [[ "$platform_name" == Windows* ]]; then
        cat >> "$readme_dir/README.txt" << EOF
   \`\`\`
   $exe_name
   \`\`\`
EOF
    else
        cat >> "$readme_dir/README.txt" << EOF
   \`\`\`
   chmod +x $exe_name
   ./$exe_name
   \`\`\`
EOF
    fi

    cat >> "$readme_dir/README.txt" << EOF
4. 產生的網站檔案會在 \`Output\` 目錄中

## 設定檔範例

\`\`\`json
{
  "PostsPath": "Posts",
  "OutputPath": "Output",
  "TemplatePath": "Templates"
}
\`\`\`

## 自定義模板

- 修改 \`Templates/index.html\` 自定義首頁
- 修改 \`Templates/post.html\` 自定義文章頁面

## 文章格式

\`\`\`markdown
---
title: "文章標題"
date: 2024-01-01
categories: ["分類1", "分類2"]
tags: ["標籤1", "標籤2"]
---

這裡是文章內容...
\`\`\`

## 支援與文件

更多資訊請參考專案文件或 GitHub 頁面
EOF
}

# 取得檔案大小（人類可讀格式）
get_file_size() {
    local file="$1"
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        stat -f%z "$file" | awk '{printf "%.1f MB", $1/1024/1024}'
    else
        # Linux
        stat -c%s "$file" | awk '{printf "%.1f MB", $1/1024/1024}'
    fi
}

# 建立建置資訊
create_build_info

# 遍歷所有平台
total_platforms=${#platforms[@]}
for i in "${!platforms[@]}"; do
    platform="${platforms[$i]}"
    display_name="${display_names[$i]}"
    
    echo -e "${CYAN}[$((i+1))/$total_platforms] 正在建立 $display_name 版本...${NC}"
    
    # 清理臨時目錄
    if [ -d "$TEMP_DIR" ]; then
        rm -rf "$TEMP_DIR"
    fi
    mkdir -p "$TEMP_DIR"
    
    # 發布專案 (單一執行檔)
    echo -e "    ${YELLOW}- 編譯中...${NC}"
    if ! dotnet publish -c Release -r "$platform" --self-contained true \
         -p:PublishSingleFile=true -p:PublishTrimmed=true \
         -o "$TEMP_DIR" > /dev/null 2>&1; then
        echo -e "    ${RED}❌ 失敗：無法建立 $display_name 版本${NC}"
        continue
    fi
    
    # 建立發布檔案結構
    release_dir="$BUILD_DIR/${PROJECT_NAME}-v${VERSION}-${platform}"
    mkdir -p "$release_dir"
    
    # 複製執行檔
    echo -e "    ${YELLOW}- 打包檔案...${NC}"
    if [[ "$platform" == win-* ]]; then
        cp "$TEMP_DIR/${PROJECT_NAME}.exe" "$release_dir/"
        exe_name="${PROJECT_NAME}.exe"
    else
        cp "$TEMP_DIR/${PROJECT_NAME}" "$release_dir/"
        exe_name="${PROJECT_NAME}"
        # 設定執行權限
        chmod +x "$release_dir/$exe_name"
    fi
    
    # 複製 Templates 目錄
    if [ -d "Templates" ]; then
        cp -r "Templates" "$release_dir/"
    fi
    
    # 建立範例設定檔
    if [ -f "velo.config.json" ]; then
        cp "velo.config.json" "$release_dir/velo.config.json.example"
    fi
    
    # 建立 README 檔案
    create_readme "$release_dir" "$display_name" "$exe_name"
    
    # 建立 ZIP 檔案
    zip_name="${PROJECT_NAME}-v${VERSION}-${platform}.zip"
    
    if [ "$USE_ZIP" = true ]; then
        echo -e "    ${YELLOW}- 建立 ZIP...${NC}"
        
        # 切換到發布目錄建立 ZIP
        cd "$release_dir" || exit 1
        if zip -r "../$zip_name" . > /dev/null 2>&1; then
            echo -e "    ${GREEN}✅ 完成：$zip_name${NC}"
            cd - > /dev/null || exit 1
            # 清理解壓後的目錄 (保留 ZIP)
            rm -rf "$release_dir"
        else
            echo -e "    ${YELLOW}⚠️  警告：無法建立 ZIP 檔案，但檔案已準備就緒於 $release_dir${NC}"
            cd - > /dev/null || exit 1
        fi
    else
        echo -e "    ${GREEN}✅ 完成：檔案準備就緒於 $release_dir${NC}"
    fi
    
    echo
done

# 清理臨時檔案
if [ -d "$TEMP_DIR" ]; then
    rm -rf "$TEMP_DIR"
fi

echo -e "${BLUE}================================${NC}"
echo -e "${GREEN}📦 發布完成！${NC}"
echo -e "${BLUE}================================${NC}"
echo -e "${CYAN}發布日期：$BUILD_DATE${NC}"
echo -e "${CYAN}檔案位置：$BUILD_DIR/${NC}"
echo

# 顯示建立的檔案
if [ -d "$BUILD_DIR" ]; then
    echo -e "${PURPLE}📋 已建立的發布包：${NC}"
    echo
    
    # 列出 ZIP 檔案
    zip_found=false
    for zip_file in "$BUILD_DIR"/*.zip; do
        if [ -f "$zip_file" ]; then
            zip_found=true
            filename=$(basename "$zip_file")
            filesize=$(get_file_size "$zip_file")
            echo -e "    ${GREEN}$filename${NC} ($filesize)"
        fi
    done
    
    # 如果沒有 ZIP 檔案，顯示目錄
    if [ "$zip_found" = false ]; then
        echo -e "    ${YELLOW}⚠️  沒有找到 ZIP 檔案，請檢查以下目錄：${NC}"
        for dir in "$BUILD_DIR"/*; do
            if [ -d "$dir" ]; then
                dirname=$(basename "$dir")
                echo -e "    ${CYAN}📁 $dirname${NC}"
            fi
        done
    fi
fi

echo
echo -e "${PURPLE}💡 提示：你可以在 releases/ 目錄中找到所有日期的發布版本${NC}"

# 如果是互動模式，等待使用者按鍵
if [ -t 0 ]; then
    echo
    read -p "按任意鍵繼續..."
fi
