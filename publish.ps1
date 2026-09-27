<#
.SYNOPSIS
    Copy a local SourceGit build into G:\SourceGit.

.DESCRIPTION
    Run build.ps1 first. Copies the executable and its required files while
    preserving other files in the destination, including portable data and the
    existing updater script. If a copy fails, overwritten files are restored.

.EXAMPLE
    .\publish.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',

    [string] $Destination = 'G:\SourceGit'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot "build\SourceGit\$Runtime"
$sourceExe = Join-Path $source 'SourceGit.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw "未找到编译结果: $sourceExe。请先运行 .\build.ps1 -Runtime $Runtime。"
}

$destinationPath = [IO.Path]::GetFullPath($Destination)
$sourcePath = [IO.Path]::GetFullPath($source)
if ($destinationPath -eq [IO.Path]::GetPathRoot($destinationPath) -or
    $destinationPath.Equals($sourcePath, [StringComparison]::OrdinalIgnoreCase) -or
    $sourcePath.StartsWith($destinationPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "无效的发布目录: $destinationPath"
}
if (Test-Path -LiteralPath $destinationPath) {
    if (-not (Test-Path -LiteralPath $destinationPath -PathType Container) -or
        ((Get-Item -LiteralPath $destinationPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "发布目录不是普通目录: $destinationPath"
    }
}

$destinationExe = Join-Path $destinationPath 'SourceGit.exe'
$running = @(Get-Process -Name SourceGit -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and [IO.Path]::GetFullPath($_.Path).Equals($destinationExe, [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
})
if ($running.Count -gt 0) {
    throw "请先关闭从 $destinationExe 启动的 SourceGit，再运行发布脚本。"
}

$sourcePrefix = $sourcePath.TrimEnd('\') + '\'
$files = @(Get-ChildItem -LiteralPath $sourcePath -File -Recurse -Force | Where-Object { $_.Extension -ine '.pdb' })
if ($files.Count -eq 0) { throw "编译结果为空: $sourcePath" }

$plan = @()
foreach ($file in $files) {
    $relative = $file.FullName.Substring($sourcePrefix.Length)
    $firstPart = ($relative -split '[\\/]')[0]
    if ($firstPart -ieq 'data' -or $relative -ieq 'Update-SourceGit.ps1') {
        throw "编译结果包含受保护的文件或目录: $relative"
    }
    $plan += [pscustomobject]@{
        Source = $file.FullName
        Relative = $relative
        Target = Join-Path $destinationPath $relative
    }
}

[void](New-Item -ItemType Directory -Path $destinationPath -Force)
$backupRoot = Join-Path ([IO.Path]::GetTempPath()) ('sourcegit-publish-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $backupRoot)
$backups = @()
$newFiles = @()
$keepBackup = $false
try {
    foreach ($item in $plan) {
        $targetParent = Split-Path -Parent $item.Target
        [void](New-Item -ItemType Directory -Path $targetParent -Force)
        if (Test-Path -LiteralPath $item.Target) {
            if (-not (Test-Path -LiteralPath $item.Target -PathType Leaf)) {
                throw "目标文件路径不是普通文件: $($item.Target)"
            }
            $backupPath = Join-Path $backupRoot $item.Relative
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force)
            Copy-Item -LiteralPath $item.Target -Destination $backupPath -Force
            $backups += [pscustomobject]@{ Backup = $backupPath; Target = $item.Target }
        }
        else {
            $newFiles += $item.Target
        }
        Copy-Item -LiteralPath $item.Source -Destination $item.Target -Force
    }
    Write-Host "发布完成: $destinationExe ($($plan.Count) 个文件)" -ForegroundColor Green
}
catch {
    $failure = $_
    $rollbackErrors = @()
    foreach ($path in $newFiles) {
        try { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
        catch { $rollbackErrors += $_.Exception.Message }
    }
    foreach ($item in $backups) {
        try { Copy-Item -LiteralPath $item.Backup -Destination $item.Target -Force }
        catch { $rollbackErrors += $_.Exception.Message }
    }
    if ($rollbackErrors.Count -gt 0) {
        $keepBackup = $true
        Write-Warning "回滚未完成，备份保存在 $backupRoot：$($rollbackErrors -join '; ')"
    }
    throw $failure
}
finally {
    if (-not $keepBackup -and (Test-Path -LiteralPath $backupRoot)) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
    }
}
