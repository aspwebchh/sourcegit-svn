<#
.SYNOPSIS
    编译并运行 SourceGit。

.DESCRIPTION
    1. 检查 .NET SDK 版本是否满足 global.json 的要求
    2. 如果子模块 depends/AvaloniaEdit 未初始化，则自动初始化
    3. 关闭上次从本地编译输出目录启动的 SourceGit（否则 DLL 被占用，编译会失败）
    4. 执行 dotnet build
    5. 启动编译出的 SourceGit.exe

    SourceGit 是单实例程序：如果已经有一个使用同一数据目录的 SourceGit 在运行，
    新启动的进程会直接退出并激活已有窗口。为了能和本机已安装的 SourceGit 同时运行，
    默认会在输出目录下创建 data 文件夹，让本地编译版以便携模式运行，
    配置和缓存都放在 src\bin\<配置>\<TFM>\data 中，不影响已安装版本。

.PARAMETER Configuration
    编译配置，Debug（默认）或 Release。

.PARAMETER Clean
    编译前先执行 dotnet clean。

.PARAMETER NoBuild
    跳过编译，直接运行上一次的编译结果。

.PARAMETER Wait
    等待程序退出后脚本再结束。

.PARAMETER SharedData
    不使用便携模式，与已安装的 SourceGit 共用 %APPDATA%\SourceGit 中的配置。
    注意：此时需要先关闭已安装的 SourceGit，否则本地编译版会直接退出。

.EXAMPLE
    .\run.ps1
    .\run.ps1 -Configuration Release
    .\run.ps1 -Clean
    .\run.ps1 -NoBuild
    .\run.ps1 -SharedData
    .\run.ps1 -- G:\path\to\repo    # "--" 之后的参数原样传给 SourceGit
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Clean,

    [switch]$NoBuild,

    [switch]$Wait,

    [switch]$SharedData,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$project = Join-Path $root 'src\SourceGit.csproj'

function Write-Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Exit-WithError([string]$msg) {
    Write-Host "错误: $msg" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------------------
# 1. 检查 .NET SDK
# ---------------------------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Exit-WithError '未找到 dotnet 命令，请先安装 .NET SDK: https://dotnet.microsoft.com/download'
}

$globalJson    = Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
$requiredMajor = [int]($globalJson.sdk.version.Split('.')[0])

$sdkMajors = @(& dotnet --list-sdks | ForEach-Object {
    if ($_ -match '^(\d+)\.') { [int]$Matches[1] }
})

if (-not ($sdkMajors | Where-Object { $_ -ge $requiredMajor })) {
    $installed = if ($sdkMajors.Count -gt 0) { (($sdkMajors | Sort-Object -Unique) -join ', ') } else { '无' }
    Exit-WithError (@(
        "需要 .NET SDK $requiredMajor.0 或更高版本（见 global.json），当前已安装的主版本: $installed",
        "可通过以下任一方式安装:",
        "  winget install Microsoft.DotNet.SDK.$requiredMajor",
        "  https://dotnet.microsoft.com/download/dotnet/$requiredMajor.0"
    ) -join [Environment]::NewLine)
}

# ---------------------------------------------------------------------------
# 2. 初始化子模块
# ---------------------------------------------------------------------------
$textMateProj = Join-Path $root 'depends\AvaloniaEdit\src\AvaloniaEdit.TextMate\AvaloniaEdit.TextMate.csproj'
if (-not (Test-Path $textMateProj)) {
    Write-Step '初始化子模块 depends/AvaloniaEdit'
    & git -C $root submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { Exit-WithError '子模块初始化失败' }
}

# ---------------------------------------------------------------------------
# 3. 计算输出路径
# ---------------------------------------------------------------------------
[xml]$csproj = Get-Content $project -Raw
$tfm = @($csproj.Project.PropertyGroup | ForEach-Object { $_.TargetFramework } | Where-Object { $_ })[0]
if (-not $tfm) { Exit-WithError '无法从 SourceGit.csproj 中读取 TargetFramework' }

$outDir = Join-Path $root "src\bin\$Configuration\$tfm"
$exe    = Join-Path $outDir 'SourceGit.exe'

# ---------------------------------------------------------------------------
# 4. 编译
# ---------------------------------------------------------------------------
if (-not $NoBuild) {
    # 关闭从本输出目录启动的 SourceGit，避免文件被占用导致编译失败
    $running = @(Get-Process -Name SourceGit -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($outDir, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($running.Count -gt 0) {
        Write-Step "关闭正在运行的本地编译版本 SourceGit (PID: $(($running | ForEach-Object Id) -join ', '))"
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }

    if ($Clean) {
        Write-Step "dotnet clean ($Configuration)"
        & dotnet clean $project -c $Configuration --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { Exit-WithError 'dotnet clean 失败' }
    }

    Write-Step "dotnet build ($Configuration)"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & dotnet build $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { Exit-WithError "编译失败 (exit code $LASTEXITCODE)" }
    Write-Host ("编译完成，耗时 {0:N1} 秒" -f $sw.Elapsed.TotalSeconds) -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 5. 运行
# ---------------------------------------------------------------------------
if (-not (Test-Path $exe)) {
    Exit-WithError "未找到可执行文件: $exe$([Environment]::NewLine)请先去掉 -NoBuild 参数进行编译"
}

# 通过 data / data.disabled 目录切换便携模式，切换时只重命名，不删除已有数据
$portableDir         = Join-Path $outDir 'data'
$disabledPortableDir = Join-Path $outDir 'data.disabled'
if ($SharedData) {
    if (Test-Path $portableDir) {
        if (Test-Path $disabledPortableDir) {
            Exit-WithError "$portableDir 和 $disabledPortableDir 同时存在，请手动处理后重试"
        }
        Rename-Item $portableDir 'data.disabled'
    }

    $others = @(Get-Process -Name SourceGit -ErrorAction SilentlyContinue | Where-Object {
        try { -not ($_.Path -and $_.Path.StartsWith($outDir, [StringComparison]::OrdinalIgnoreCase)) } catch { $true }
    })
    if ($others.Count -gt 0) {
        Write-Warning "检测到其他 SourceGit 正在运行 (PID: $(($others | ForEach-Object Id) -join ', '))，本地编译版会直接退出。请先关闭它，或去掉 -SharedData 参数。"
    }
} elseif (-not (Test-Path $portableDir)) {
    if (Test-Path $disabledPortableDir) {
        Rename-Item $disabledPortableDir 'data'
    } else {
        New-Item -ItemType Directory -Path $portableDir | Out-Null
    }
}

Write-Step "启动 $exe"
$startArgs = @{ FilePath = $exe; WorkingDirectory = $outDir; PassThru = $true }
if ($AppArgs) { $startArgs.ArgumentList = $AppArgs }

$proc = Start-Process @startArgs

# 单实例检测失败或启动崩溃时进程会很快退出，给出提示
if (-not $Wait -and $proc.WaitForExit(3000)) {
    Write-Warning "SourceGit 启动后立即退出 (exit code $($proc.ExitCode))，可能是已有实例在运行，或启动时崩溃（崩溃日志见数据目录下的 crashes 文件夹）"
    exit 1
}

if ($Wait) {
    $proc.WaitForExit()
    Write-Host "SourceGit 已退出 (exit code $($proc.ExitCode))"
}
