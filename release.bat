
@echo off
setlocal enabledelayedexpansion

echo ================================
echo     Velo �o���u�� v1.0.2
echo ================================

:: �]�w�ܼ�
set "PROJECT_NAME=Velo"
set "VERSION=1.0.2"
set "TEMP_DIR=temp_build"

:: ���ͤ���r�� (YYYYMMDD)
for /f "tokens=2 delims==" %%a in ('"wmic OS Get localdatetime /value"') do set "dt=%%a"
set "BUILD_DATE=%dt:~0,8%"
set "BUILD_DIR=releases\%BUILD_DATE%"

:: �ˬd�O�_�w�ˤF .NET SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ���~�G����� .NET SDK�A�Х��w�� .NET 9.0 SDK
    exit /b 1
)

:: �إߵo���ؿ�
if not exist "releases" mkdir "releases"
if exist "%BUILD_DIR%" (
    echo ĵ�i�G�ؿ� %BUILD_DIR% �w�s�b
    set /p "confirm=�O�_�n�л\�{���ɮ�? (y/N): "
    if /i not "!confirm!"=="y" (
        echo �����o��
        exit /b 0
    )
    echo �M�z�{���ؿ�...
    rmdir /s /q "%BUILD_DIR%"
)
mkdir "%BUILD_DIR%"

echo.
echo �إߤ���G%BUILD_DATE%
echo �o���ؿ��G%BUILD_DIR%
echo �}�l�إߵo���]...
echo.

:: �w�q�ؼХ��x�}�C
set platforms[0]=win-x64
set platforms[1]=win-arm64
set platforms[2]=linux-x64
set platforms[3]=linux-arm64
set platforms[4]=osx-x64
set platforms[5]=osx-arm64

:: �w�q���x��ܦW��
set display[0]=Windows x64
set display[1]=Windows ARM64
set display[2]=Linux x64
set display[3]=Linux ARM64
set display[4]=macOS x64 (Intel)
set display[5]=macOS ARM64 (Apple Silicon)

:: �إ߫ظm��T�ɮ�
call :create_build_info "%BUILD_DIR%"

:: �M���Ҧ����x
for /l %%i in (0,1,5) do (
    set "platform=!platforms[%%i]!"
    set "displayName=!display[%%i]!"
    
    echo [%%i/5] ���b�إ� !displayName! ����...
    
    :: �M�z�{�ɥؿ�
    if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
    mkdir "%TEMP_DIR%"
    
    :: �o���M�� (��@������)
    echo    - �sĶ��...
    dotnet publish -c Release -r !platform! --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -o "%TEMP_DIR%" >nul 2>&1
    
    if errorlevel 1 (
        echo    ���ѡG�L�k�إ� !displayName! ����
        continue
    )
    
    :: �إߵo���ɮ׵��c
    set "release_dir=%BUILD_DIR%\%PROJECT_NAME%-v%VERSION%-!platform!"
    mkdir "!release_dir!"
    
    :: �ƻs������
    echo    - ���]�ɮ�...
    if "!platform:~0,3!"=="win" (
        copy "%TEMP_DIR%\%PROJECT_NAME%.exe" "!release_dir!\" >nul
        set "exe_name=%PROJECT_NAME%.exe"
    ) else (
        copy "%TEMP_DIR%\%PROJECT_NAME%" "!release_dir!\" >nul
        set "exe_name=%PROJECT_NAME%"
    )
    
    :: �ƻs Templates �ؿ�
    xcopy /E /I /Q "Templates" "!release_dir!\Templates\" >nul
    
    :: �إ߽d�ҳ]�w��
    copy "velo.config.json" "!release_dir!\velo.config.json.example" >nul
    
    :: �إ� README �ɮ�
    call :create_readme "!release_dir!" "!displayName!" "!exe_name!"
    
    :: �إ� ZIP �ɮ�
    set "zip_name=%PROJECT_NAME%-v%VERSION%-!platform!.zip"
    echo    - �إ� ZIP...
    
    :: �ϥ� PowerShell �إ� ZIP (Windows ����)
    powershell -command "Compress-Archive -Path '!release_dir!\*' -DestinationPath '%BUILD_DIR%\!zip_name!' -Force" >nul 2>&1
    
    if errorlevel 1 (
        echo    ĵ�i�G�L�k�إ� ZIP �ɮסA���ɮפw�ǳƴN���� !release_dir!
    ) else (
        echo    �����G!zip_name!
        :: �M�z�����᪺�ؿ� (�O�d ZIP)
        rmdir /s /q "!release_dir!"
    )
    
    echo.
)

:: �M�z�{���ɮ�
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"

echo ================================
echo �o�������I
echo ================================
echo �o������G%BUILD_DATE%
echo �ɮצ�m�G%BUILD_DIR%\
echo.

:: ��ܫإߪ��ɮ�
if exist "%BUILD_DIR%" (
    echo �w�إߪ��o���]�G
    echo.
    for %%f in ("%BUILD_DIR%\*.zip") do (
        set "filesize="
        for %%s in ("%%f") do set "filesize=%%~zs"
        set /a "filesize_mb=!filesize!/1024/1024"
        echo    %%~nxf (!filesize_mb! MB)
    )
    
    :: �p�G�S�� ZIP �ɮסA��ܥؿ�
    dir /b "%BUILD_DIR%\*.zip" >nul 2>&1
    if errorlevel 1 (
        echo    �S����� ZIP �ɮסA���ˬd�H�U�ؿ��G
        dir /b "%BUILD_DIR%\" 2>nul
    )
)

echo.
echo ���ܡG�A�i�H�b releases\ �ؿ������Ҧ�������o������
goto :eof

:: �إ� README �ɮת����
:create_readme
set "readme_dir=%~1"
set "platform_name=%~2"
set "exe_name=%~3"

(
echo # Velo - �R�A������ͦ���
echo.
echo �����G%VERSION%
echo �ظm����G%BUILD_DATE%
echo ���x�G%platform_name%
echo.
echo ## �ֳt�}�l
echo.
echo 1. �إ� `velo.config.json` �]�w�ɡ]�i�Ѧ� `velo.config.json.example`�^
echo 2. �إ� `Posts` �ؿ��é�m Markdown �ɮ�
echo 3. ����{���G
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
echo 4. ���ͪ������ɮ׷|�b `Output` �ؿ���
echo.
echo ## �]�w�ɽd��
echo.
echo ```json
echo {
echo   "PostsPath": "Posts",
echo   "OutputPath": "Output",
echo   "TemplatesPath": "Templates",
echo   "SiteTitle": "�ڪ�������"
echo }
echo ```
echo.
echo ## �۩w�q�ҪO
echo.
echo - �ק� `Templates/index.html` �۩w�q����
echo - �ק� `Templates/post.html` �۩w�q�峹����
echo.
echo ## �峹�榡
echo.
echo ```markdown
echo ---
echo title: "�峹���D"
echo date: 2024-01-01
echo categories: ["����1", "����2"]
echo tags: ["����1", "����2"]
echo ---
echo.
echo �o�̬O�峹���e...
echo ```
echo.
echo ## �䴩�P���
echo.
echo ��h��T�аѦұM�פ��� GitHub ����
) > "%readme_dir%\README.txt"

goto :eof

:: �إ߫ظm��T�ɮ�
:create_build_info
set "build_dir=%~1"

:: ���o��e�ɶ�
for /f "tokens=1-3 delims=/: " %%a in ('time /t') do set "build_time=%%a:%%b"

(
echo # Velo �o����T
echo.
echo �ظm����G%BUILD_DATE%
echo �ظm�ɶ��G%build_time%
echo �����G%VERSION%
echo.
echo ## �]�t�����x
echo.
echo - Windows x64
echo - Windows ARM64
echo - Linux x64
echo - Linux ARM64
echo - macOS x64 ^(Intel^)
echo - macOS ARM64 ^(Apple Silicon^)
echo.
echo ## �ɮ׻���
echo.
echo �C�� ZIP �]�t�G
echo - ��@������ ^(�L���B�~�̿�^)
echo - Templates �ؿ� ^(�ҪO�ɮ�^)
echo - �d�ҳ]�w��
echo - README �ϥλ���
echo.
echo ���������Y�i�ϥΡI
) > "%build_dir%\BUILD_INFO.txt"

goto :eof