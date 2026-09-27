<#
.SYNOPSIS
    Publish a Windows SourceGit executable and its required native libraries.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Runtime win-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\SourceGit.csproj'
$artifactRoot = Join-Path $PSScriptRoot 'build\SourceGit'
$output = Join-Path $artifactRoot $Runtime
$staging = Join-Path $artifactRoot ('.{0}.staging.{1}' -f $Runtime, [guid]::NewGuid().ToString('N'))
$backup = Join-Path $artifactRoot ('.{0}.backup.{1}' -f $Runtime, [guid]::NewGuid().ToString('N'))

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '未找到 dotnet，请先安装 .NET 10 SDK。'
}

$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or [version]$sdkVersion -lt [version]'10.0') {
    throw "需要 .NET 10 SDK，当前版本: $sdkVersion"
}

$submoduleProject = Join-Path $PSScriptRoot 'depends\AvaloniaEdit\src\AvaloniaEdit.TextMate\AvaloniaEdit.TextMate.csproj'
if (-not (Test-Path -LiteralPath $submoduleProject -PathType Leaf)) {
    Write-Host '初始化 AvaloniaEdit 子模块...'
    & git -C $PSScriptRoot submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw '子模块初始化失败。' }
}

[void](New-Item -ItemType Directory -Path $artifactRoot -Force)
if ((Get-Item -LiteralPath $artifactRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw "构建输出目录不能是链接: $artifactRoot"
}

$published = $false
try {
    Write-Host "编译 SourceGit ($Runtime)..."
    & dotnet publish $project -c Release -r $Runtime --self-contained true -o $staging --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit code $LASTEXITCODE)。" }

    $executable = Join-Path $staging 'SourceGit.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "编译完成但未找到可执行文件: $executable"
    }

    if (Test-Path -LiteralPath $output) {
        if (-not (Test-Path -LiteralPath $output -PathType Container) -or
            ((Get-Item -LiteralPath $output).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "现有输出路径不是普通目录: $output"
        }
        Move-Item -LiteralPath $output -Destination $backup
    }

    try {
        Move-Item -LiteralPath $staging -Destination $output
        $published = $true
    }
    catch {
        if (Test-Path -LiteralPath $backup) {
            Move-Item -LiteralPath $backup -Destination $output
        }
        throw
    }

    Write-Host "编译完成: $(Join-Path $output 'SourceGit.exe')" -ForegroundColor Green
}
finally {
    # Both paths are unique children of the repository's build/SourceGit directory.
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    if ($published -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}
