
@echo off
setlocal enabledelayedexpansion

echo ================================
echo     Velo 發布工具 v1.0.2
echo ================================

:: 設定變數
set "PROJECT_NAME=Velo"
set "VERSION=1.0.2"
set "TEMP_DIR=temp_build"

:: 產生日期字串 (YYYYMMDD)
for /f "tokens=2 delims==" %%a in ('"wmic OS Get localdatetime /value"') do set "dt=%%a"
set "BUILD_DATE=%dt:~0,8%"
set "BUILD_DIR=releases\%BUILD_DATE%"

:: 檢查是否安裝了 .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo 錯誤：未找到 .NET SDK，請先安裝 .NET 9.0 SDK
    exit /b 1
)

:: 建立發布目錄
if not exist "releases" mkdir "releases"
if exist "%BUILD_DIR%" (
    echo 警告：目錄 %BUILD_DIR% 已存在
    set /p "confirm=是否要覆蓋現有檔案? (y/N): "
    if /i not "!confirm!"=="y" (
        echo 取消發布
        exit /b 0
    )
    echo 清理現有目錄...
    rmdir /s /q "%BUILD_DIR%"
)
mkdir "%BUILD_DIR%"

echo.
echo 建立日期：%BUILD_DATE%
echo 發布目錄：%BUILD_DIR%
echo 開始建立發布包...
echo.

:: 定義目標平台陣列
set platforms[0]=win-x64
set platforms[1]=win-arm64
set platforms[2]=linux-x64
set platforms[3]=linux-arm64
set platforms[4]=osx-x64
set platforms[5]=osx-arm64

:: 定義平台顯示名稱
set display[0]=Windows x64
set display[1]=Windows ARM64
set display[2]=Linux x64
set display[3]=Linux ARM64
set display[4]=macOS x64 (Intel)
set display[5]=macOS ARM64 (Apple Silicon)

:: 建立建置資訊檔案
call :create_build_info "%BUILD_DIR%"

:: 遍歷所有平台
for /l %%i in (0,1,5) do (
    set "platform=!platforms[%%i]!"
    set "displayName=!display[%%i]!"
    
    echo [%%i/5] 正在建立 !displayName! 版本...
    
    :: 清理臨時目錄
    if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
    mkdir "%TEMP_DIR%"
    
    :: 發布專案 (單一執行檔)
    echo    - 編譯中...
    dotnet publish -c Release -r !platform! --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o "%TEMP_DIR%" >nul 2>&1
    
    if errorlevel 1 (
        echo    失敗：無法建立 !displayName! 版本
        continue
    )
    
    :: 建立發布檔案結構
    set "release_dir=%BUILD_DIR%\%PROJECT_NAME%-v%VERSION%-!platform!"
    mkdir "!release_dir!"
    
    :: 複製執行檔
    echo    - 打包檔案...
    if "!platform:~0,3!"=="win" (
        copy "%TEMP_DIR%\%PROJECT_NAME%.exe" "!release_dir!\" >nul
        set "exe_name=%PROJECT_NAME%.exe"
    ) else (
        copy "%TEMP_DIR%\%PROJECT_NAME%" "!release_dir!\" >nul
        set "exe_name=%PROJECT_NAME%"
    )
    
    :: 複製 Templates 目錄
    xcopy /E /I /Q "Templates" "!release_dir!\Templates\" >nul
    
    :: 建立範例設定檔
    copy "velo.config.json" "!release_dir!\velo.config.json.example" >nul
    
    :: 建立 README 檔案
    call :create_readme "!release_dir!" "!displayName!" "!exe_name!"
    
    :: 建立 ZIP 檔案
    set "zip_name=%PROJECT_NAME%-v%VERSION%-!platform!.zip"
    echo    - 建立 ZIP...
    
    :: 使用 PowerShell 建立 ZIP (Windows 內建)
    powershell -command "Compress-Archive -Path '!release_dir!\*' -DestinationPath '%BUILD_DIR%\!zip_name!' -Force" >nul 2>&1
    
    if errorlevel 1 (
        echo    警告：無法建立 ZIP 檔案，但檔案已準備就緒於 !release_dir!
    ) else (
        echo    完成：!zip_name!
        :: 清理解壓後的目錄 (保留 ZIP)
        rmdir /s /q "!release_dir!"
    )
    
    echo.
)

:: 清理臨時檔案
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"

echo ================================
echo 發布完成！
echo ================================
echo 發布日期：%BUILD_DATE%
echo 檔案位置：%BUILD_DIR%\
echo.

:: 顯示建立的檔案
if exist "%BUILD_DIR%" (
    echo 已建立的發布包：
    echo.
    for %%f in ("%BUILD_DIR%\*.zip") do (
        set "filesize="
        for %%s in ("%%f") do set "filesize=%%~zs"
        set /a "filesize_mb=!filesize!/1024/1024"
        echo    %%~nxf (!filesize_mb! MB)
    )
    
    :: 如果沒有 ZIP 檔案，顯示目錄
    dir /b "%BUILD_DIR%\*.zip" >nul 2>&1
    if errorlevel 1 (
        echo    沒有找到 ZIP 檔案，請檢查以下目錄：
        dir /b "%BUILD_DIR%\" 2>nul
    )
)

echo.
echo 提示：你可以在 releases\ 目錄中找到所有日期的發布版本
goto :eof

:: 建立 README 檔案的函數
:create_readme
set "readme_dir=%~1"
set "platform_name=%~2"
set "exe_name=%~3"

(
echo # Velo - 靜態部落格生成器
echo.
echo 版本：%VERSION%
echo 建置日期：%BUILD_DATE%
echo 平台：%platform_name%
echo.
echo ## 快速開始
echo.
echo 1. 建立 `velo.config.json` 設定檔（可參考 `velo.config.json.example`）
echo 2. 建立 `Posts` 目錄並放置 Markdown 檔案
echo 3. 執行程式：
if "%platform_name:~0,7%"=="Windows" (
echo    ```
echo    %exe_name%
echo    ```
) else (
echo    ```
echo    chmod +x %exe_name%
echo    ./%exe_name%
echo    ```
)
echo 4. 產生的網站檔案會在 `Output` 目錄中
echo.
echo ## 設定檔範例
echo.
echo ```json
echo {
echo   "PostsPath": "Posts",
echo   "OutputPath": "Output",
echo   "TemplatesPath": "Templates",
echo   "SiteTitle": "我的部落格"
echo }
echo ```
echo.
echo ## 自定義模板
echo.
echo - 修改 `Templates/index.html` 自定義首頁
echo - 修改 `Templates/post.html` 自定義文章頁面
echo.
echo ## 文章格式
echo.
echo ```markdown
echo ---
echo title: "文章標題"
echo date: 2024-01-01
echo categories: ["分類1", "分類2"]
echo tags: ["標籤1", "標籤2"]
echo ---
echo.
echo 這裡是文章內容...
echo ```
echo.
echo ## 支援與文件
echo.
echo 更多資訊請參考專案文件或 GitHub 頁面
) > "%readme_dir%\README.txt"

goto :eof

:: 建立建置資訊檔案
:create_build_info
set "build_dir=%~1"

:: 取得當前時間
for /f "tokens=1-3 delims=/: " %%a in ('time /t') do set "build_time=%%a:%%b"

(
echo # Velo 發布資訊
echo.
echo 建置日期：%BUILD_DATE%
echo 建置時間：%build_time%
echo 版本：%VERSION%
echo.
echo ## 包含的平台
echo.
echo - Windows x64
echo - Windows ARM64
echo - Linux x64
echo - Linux ARM64
echo - macOS x64 ^(Intel^)
echo - macOS ARM64 ^(Apple Silicon^)
echo.
echo ## 檔案說明
echo.
echo 每個 ZIP 包含：
echo - 單一執行檔 ^(無需額外依賴^)
echo - Templates 目錄 ^(模板檔案^)
echo - 範例設定檔
echo - README 使用說明
echo.
echo 直接解壓即可使用！
) > "%build_dir%\BUILD_INFO.txt"

goto :eof