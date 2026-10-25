# Publishes the version in src\Program.cs: builds Nook.exe, puts it on a GitHub release (with a WinGroups.exe copy,
# which is what versions before 0.7 look for when they update), then points the website's download buttons at it
# and republishes the site. Needs the GitHub CLI (gh), signed in.
#   .\release.ps1 -Notes notes.md            a pre-release
#   .\release.ps1 -Notes notes.md -Final     a full release
#   .\release.ps1 -SiteOnly                  just stamp and republish the site for the newest release
param([string]$Notes, [switch]$Final, [switch]$SiteOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = 'ValidRed/nook'
$version = [regex]::Match([IO.File]::ReadAllText("$root\src\Program.cs"), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "Couldn't read the version from src\Program.cs" }
$tag = "v$version"

if (-not $SiteOnly) {
  if (-not $Notes -or -not (Test-Path $Notes)) { throw "Pass -Notes with a file of release notes" }
  # The release is tagged on GitHub's main, so the code goes up first.
  git -C $root push -q origin main
  if ($LASTEXITCODE -ne 0) { throw "Push failed" }
  & "$root\build.ps1"
  $out = Join-Path $env:TEMP "nook-release-$version"
  Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory $out | Out-Null
  Copy-Item "$root\dist\Nook.exe" "$out\Nook.exe"
  Copy-Item "$root\dist\Nook.exe" "$out\WinGroups.exe"
  $flags = @('--target', 'main', '--title', "Nook $version", '--notes-file', $Notes)
  if (-not $Final) { $flags += '--prerelease' }
  gh release create $tag "$out\Nook.exe" "$out\WinGroups.exe" --repo $repo @flags
  if ($LASTEXITCODE -ne 0) { throw "Couldn't create the release" }
}

# The site looks up the newest release when it loads; these are what it shows before that (or if GitHub can't be reached).
$assets = (gh release view $tag --repo $repo --json assets | ConvertFrom-Json).assets
$size = ($assets | Where-Object { $_.name -eq 'Nook.exe' } | Select-Object -First 1).size
if (-not $size) { throw "$tag has no Nook.exe" }
$kb = [math]::Round([int64]$size / 1024)
$page = "$root\site\index.html"
$html = [IO.File]::ReadAllText($page)
$html = [regex]::Replace($html, '<span data-version>[^<]*</span>', "<span data-version>$version</span>")
$html = [regex]::Replace($html, '<span data-size>[^<]*</span>', "<span data-size>$kb KB</span>")
$html = [regex]::Replace($html, '(data-download href=")[^"]*(")', "`${1}https://github.com/$repo/releases/download/$tag/Nook.exe`${2}")
# A new version of the page always loads its own styles and script, never a copy a browser kept from before.
$html = [regex]::Replace($html, '((?:styles\.css|main\.js))(\?v=[^"]*)?"', "`${1}?v=$version`"")
[IO.File]::WriteAllText($page, $html, (New-Object Text.UTF8Encoding $false))

Push-Location $root
try {
  git add site/index.html
  git diff --cached --quiet
  if ($LASTEXITCODE -ne 0) { git commit -q -m "Site: download button for Nook $version"; if ($LASTEXITCODE -ne 0) { throw "Commit failed" } }
  git push -q origin main
  if ($LASTEXITCODE -ne 0) { throw "Push failed" }
  # The site is served from the gh-pages branch, which holds just the site folder.
  git subtree push --prefix site origin gh-pages
  if ($LASTEXITCODE -ne 0) { throw "Publishing the site failed" }
}
finally { Pop-Location }
Write-Host "Released $tag and pointed the website at it."
