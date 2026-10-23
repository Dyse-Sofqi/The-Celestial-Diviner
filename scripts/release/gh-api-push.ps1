# GitHub REST API release (bypasses the blocked git channel):
# replicate local commits byte-for-byte -> update main -> create annotated tag -> create Release + upload asset
# One-off record for v1.7.5 (hardcoded SHAs/tag/paths); superseded by the parameterized gh-api-release.ps1.
# Note: deliberately ASCII-only so Windows PowerShell 5.1 parses it regardless of BOM.
$ErrorActionPreference = 'Stop'
$repo  = 'F:\_Workspace\The-Celestial-Diviner'
$owner = 'Dyse-Sofqi'
$name  = 'The-Celestial-Diviner'
$api   = "https://api.github.com/repos/$owner/$name"
$tok   = (gh auth token).Trim()
$utf8  = New-Object System.Text.UTF8Encoding($false)

function Invoke-Gh([string]$method, [string]$url, [string]$json) {
    $in  = [System.IO.Path]::GetTempFileName()
    $out = [System.IO.Path]::GetTempFileName()
    if ($null -ne $json) { [System.IO.File]::WriteAllText($in, $json, $utf8) }
    $curlArgs = @('-sS', '-m', '180', '-X', $method,
                  '-H', "Authorization: Bearer $tok",
                  '-H', 'Accept: application/vnd.github+json',
                  '-H', 'X-GitHub-Api-Version: 2022-11-28',
                  '-o', $out)
    if ($null -ne $json) { $curlArgs += @('-H', 'Content-Type: application/json', '--data-binary', "@$in") }
    $curlArgs += $url
    & curl.exe @curlArgs | Out-Null
    $text = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
    Remove-Item $in, $out -Force -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($text)) { throw "empty response: $method $url" }
    return ($text | ConvertFrom-Json)
}

# read a git object body (commit / tag) byte-exactly, keeping trailing newlines
function Get-ObjectBody([string]$kind, [string]$rev) {
    $bin = [System.IO.Path]::GetTempFileName()
    & cmd.exe /c "git -C `"$repo`" cat-file $kind $rev > `"$bin`"" | Out-Null
    $raw = [System.IO.File]::ReadAllText($bin, [System.Text.Encoding]::UTF8)
    Remove-Item $bin -Force
    $i = $raw.IndexOf("`n`n")
    if ($i -lt 0) { throw "cannot parse object body: $kind $rev" }
    return $raw.Substring($i + 2)
}

function Get-BlobSha([string]$commit, [string]$path) {
    $line = (git -C $repo ls-tree $commit -- $path)
    if (-not $line) { throw "path missing in ${commit}: $path" }
    return ($line -split '\s+')[2]
}

function New-Blob([string]$blobSha) {
    $bin = [System.IO.Path]::GetTempFileName()
    & cmd.exe /c "git -C `"$repo`" cat-file blob $blobSha > `"$bin`"" | Out-Null
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($bin))
    Remove-Item $bin -Force
    $body = @{ content = $b64; encoding = 'base64' } | ConvertTo-Json -Compress
    $res = Invoke-Gh 'POST' "$api/git/blobs" $body
    if ($res.sha -ne $blobSha) { throw "blob sha mismatch: local=$blobSha remote=$($res.sha)" }
    return $res.sha
}

function Copy-Commit([string]$localSha) {
    $parentSha = (git -C $repo rev-parse "$localSha^").Trim()
    $treeSha   = (git -C $repo rev-parse "$localSha^{tree}").Trim()
    $baseTree  = (git -C $repo rev-parse "$parentSha^{tree}").Trim()
    $paths     = @((git -C $repo diff --name-only "$localSha^" $localSha) -split "`n" | Where-Object { $_.Trim() })
    $aName  = (git -C $repo log -1 --format='%an' $localSha).Trim()
    $aEmail = (git -C $repo log -1 --format='%ae' $localSha).Trim()
    $aDate  = (git -C $repo log -1 --format='%aI' $localSha).Trim()
    $cName  = (git -C $repo log -1 --format='%cn' $localSha).Trim()
    $cEmail = (git -C $repo log -1 --format='%ce' $localSha).Trim()
    $cDate  = (git -C $repo log -1 --format='%cI' $localSha).Trim()
    $msg    = Get-ObjectBody 'commit' $localSha

    Write-Host "  files=$($paths.Count) parent=$parentSha"
    $entries = @()
    foreach ($p in $paths) {
        $leaf = $p.Trim()
        $remoteBlob = New-Blob (Get-BlobSha $localSha $leaf)
        $entries += @{ path = $leaf; mode = '100644'; type = 'blob'; sha = $remoteBlob }
    }
    $treeBody = @{ base_tree = $baseTree; tree = $entries } | ConvertTo-Json -Depth 6 -Compress
    $tree = Invoke-Gh 'POST' "$api/git/trees" $treeBody
    if ($tree.sha -ne $treeSha) { throw "tree sha mismatch: local=$treeSha remote=$($tree.sha)" }
    Write-Host "  tree ok: $($tree.sha)"

    $commitBody = @{
        message   = $msg
        tree      = $tree.sha
        parents   = @($parentSha)
        author    = @{ name = $aName; email = $aEmail; date = $aDate }
        committer = @{ name = $cName; email = $cEmail; date = $cDate }
    } | ConvertTo-Json -Depth 6 -Compress
    $commit = Invoke-Gh 'POST' "$api/git/commits" $commitBody
    return [pscustomobject]@{ Local = $localSha; Remote = [string]$commit.sha }
}

Write-Host '=== 1/4 replicate commits ==='
$commits = @()
foreach ($sha in @('f805061', '685d750', '6e629b1')) {
    Write-Host "commit $sha :"
    $commits += Copy-Commit $sha
}
foreach ($c in $commits) {
    $same = if ($c.Remote -eq $c.Local) { 'MATCH' } else { 'DIFFERENT' }
    Write-Host ("  {0} -> {1}  {2}" -f $c.Local, $c.Remote, $same)
}

Write-Host '=== 2/4 update main ==='
$head = [string]$commits[-1].Remote
$r = Invoke-Gh 'PATCH' "$api/git/refs/heads/main" (@{ sha = $head; force = $false } | ConvertTo-Json -Compress)
Write-Host ("  main -> {0}" -f $r.object.sha)

Write-Host '=== 3/4 create annotated tag v1.7.5 ==='
$tagObj      = (git -C $repo rev-parse 'v1.7.5').Trim()
$taggerName  = (git -C $repo for-each-ref --format='%(taggername)' refs/tags/v1.7.5).Trim()
$taggerEmail = (git -C $repo for-each-ref --format='%(taggeremail)' refs/tags/v1.7.5).Trim().Trim([char]'<', [char]'>')
$taggerDate  = (git -C $repo for-each-ref --format='%(taggerdate:iso-strict)' refs/tags/v1.7.5).Trim()
$tagBody = @{
    tag     = 'v1.7.5'
    message = (Get-ObjectBody 'tag' 'v1.7.5')
    object  = [string]$commits[0].Remote
    type    = 'commit'
    tagger  = @{ name = $taggerName; email = $taggerEmail; date = $taggerDate }
} | ConvertTo-Json -Depth 6 -Compress
$tagRes = Invoke-Gh 'POST' "$api/git/tags" $tagBody
$sameTag = if ($tagRes.sha -eq $tagObj) { 'MATCH' } else { 'DIFFERENT' }
Write-Host ("  tag object {0} (local {1}) {2}" -f $tagRes.sha, $tagObj, $sameTag)
try {
    $refRes = Invoke-Gh 'POST' "$api/git/refs" (@{ ref = 'refs/tags/v1.7.5'; sha = $tagRes.sha } | ConvertTo-Json -Compress)
} catch {
    Write-Host '  tag ref exists, patching'
    $refRes = Invoke-Gh 'PATCH' "$api/git/refs/tags/v1.7.5" (@{ sha = $tagRes.sha; force = $true } | ConvertTo-Json -Compress)
}
Write-Host ("  refs/tags/v1.7.5 -> {0}" -f $refRes.object.sha)

Write-Host '=== 4/4 create Release + upload asset ==='
$notes = [System.IO.File]::ReadAllText("$repo\docs\release-notes\v1.7.5-release-notes.md", [System.Text.Encoding]::UTF8)
$assetName = 'TheCelestialDiviner-1.7.5-win64.zip'
$zip   = "$repo\dist\$assetName"
# idempotent: reuse an existing release instead of failing on 422
$existing = $null
try { $existing = Invoke-Gh 'GET' "$api/releases/tags/v1.7.5" $null } catch { $existing = $null }
if ($existing -and $existing.id) {
    $rel = $existing
    Write-Host ("  release exists id={0}" -f $rel.id)
} else {
    $rel = Invoke-Gh 'POST' "$api/releases" (@{ tag_name = 'v1.7.5'; name = 'v1.7.5'; target_commitish = 'main'; draft = $false; prerelease = $false; body = $notes } | ConvertTo-Json -Compress)
    Write-Host ("  release created id={0}" -f $rel.id)
}
Write-Host ("  url={0}" -f $rel.html_url)

# idempotent: skip upload when the asset is already attached
$already = $null
if ($rel.assets) { $already = $rel.assets | Where-Object { $_.name -eq $assetName } }
if ($already) {
    Write-Host ("  asset already present: {0} ({1} bytes)" -f $already.name, $already.size)
    Write-Host ("  url: {0}" -f $already.browser_download_url)
} else {
    $upOut = [System.IO.Path]::GetTempFileName()
    & curl.exe -sS -m 600 -X POST `
        -H "Authorization: Bearer $tok" `
        -H 'Accept: application/vnd.github+json' `
        -H 'Content-Type: application/zip' `
        --data-binary "@$zip" `
        -o $upOut `
        "https://uploads.github.com/repos/$owner/$name/releases/$($rel.id)/assets?name=$assetName" | Out-Null
    $up = [System.IO.File]::ReadAllText($upOut, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    Remove-Item $upOut -Force
    if ($up.state -eq 'uploaded') {
        Write-Host ("  asset: {0} ({1} bytes)" -f $up.name, $up.size)
        Write-Host ("  url: {0}" -f $up.browser_download_url)
    } else {
        Write-Host ("  upload response: " + ($up | ConvertTo-Json -Compress))
    }
}
Write-Host 'DONE.'
