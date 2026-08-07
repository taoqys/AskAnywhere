param(
    [switch]$Patch,
    [string]$Version,
    [string]$NotesFile = "release-notes.md"
)
$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# ---------- sanity checks ----------
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne "main") { throw "必须在 main 分支发布（当前: $branch）" }

$status = git status --porcelain
$unexpected = $status | Where-Object { $_ -notmatch '^\s*M\s+release-notes\.md$' -and $_ -notmatch '^\?\?' }
if ($unexpected) { throw "工作区有未提交的改动，请先提交: `n$($unexpected -join "`n")" }

if (-not (Test-Path $NotesFile)) { throw "缺少 $NotesFile，请先编写当前版本的 changelog" }

# ---------- version ----------
$csproj = "src/AskAnywhere/AskAnywhere.csproj"
$csprojPath = Join-Path $repo $csproj
$csprojText = [System.IO.File]::ReadAllText($csprojPath)
$current = [regex]::Match($csprojText, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $current) { throw "无法从 csproj 读取当前版本" }

if ($Version) {
    $new = $Version
} else {
    $parts = $current.Split('.')
    if ($parts.Count -lt 3) { throw "版本号格式异常: $current" }
    if ($Patch) { $new = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)" }
    else       { $new = "$($parts[0]).$([int]$parts[1] + 1).0" }
}
Write-Host "版本: $current -> $new"

# ---------- bump & commit ----------
$csprojText = $csprojText -replace '<Version>[^<]+</Version>', "<Version>$new</Version>"
[System.IO.File]::WriteAllText($csprojPath, $csprojText, (New-Object System.Text.UTF8Encoding($false)))

git add $csproj $NotesFile
git commit -m "Bump version to $new"
git push origin main

# ---------- tag & push ----------
$tag = "v$new"
if (git tag -l $tag) { throw "tag $tag 已存在" }
git tag $tag
git push origin $tag

# ---------- wait for the tag-triggered build/release ----------
Write-Host "等待 Actions 构建并发布 $tag ..."
$runId = $null
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 5
    $runsJson = gh run list --repo taoqys/AskAnywhere --limit 10 --json databaseId,headBranch,event,status 2>$null
    if ($runsJson) {
        $r = $runsJson | ConvertFrom-Json | Where-Object { $_.headBranch -eq $tag -and $_.event -eq "push" } | Select-Object -First 1
        if ($r) { $runId = $r.databaseId; break }
    }
}
if (-not $runId) { throw "找不到 tag 触发的 Actions run（$tag）" }

gh run watch $runId --repo taoqys/AskAnywhere --exit-status --interval 10

# ---------- write the changelog into the release ----------
Start-Sleep -Seconds 5
gh release edit $tag --repo taoqys/AskAnywhere --notes-file $NotesFile

Write-Host ""
Write-Host "✅ $tag 已发布: https://github.com/taoqys/AskAnywhere/releases/tag/$tag"
