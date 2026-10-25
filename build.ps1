# Builds dist\Nook.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No SDK needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$wm = Join-Path $env:WINDIR 'System32\WinMetadata'
$out = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @(
  "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll",
  # UI Automation, to read where the taskbar's own buttons are (read only)
  "$wpf\UIAutomationClient.dll", "$wpf\UIAutomationTypes.dll",
  "$fw\System.Xaml.dll", "$fw\System.Web.Extensions.dll", "$fw\Microsoft.VisualBasic.dll",
  "$fw\System.Core.dll", "$fw\System.Xml.dll", "$fw\System.dll", "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll",
  # Windows Runtime, for the island: what's playing and the compositor's blur. Windows ships these .winmd files.
  "$fw\System.Runtime.dll", "$fw\System.Runtime.InteropServices.WindowsRuntime.dll", "$fw\System.Numerics.Vectors.dll", "$fw\System.Numerics.dll",
  "$wm\Windows.Foundation.winmd", "$wm\Windows.Media.winmd", "$wm\Windows.Storage.winmd", "$wm\Windows.UI.winmd"
) | ForEach-Object { "/r:$_" }

$resources = Get-ChildItem (Join-Path $root 'src\*.xaml') | ForEach-Object { "/resource:$($_.FullName),WinGroups.$($_.Name)" }
$sources = Get-ChildItem (Join-Path $root 'src\*.cs') | ForEach-Object { $_.FullName }
$icon = Join-Path $root 'assets\Nook.ico'

$cscArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:5',
  "/out:$out\Nook.exe", "/win32manifest:$root\src\app.manifest") + $refs + $resources
if (Test-Path $icon) { $cscArgs += "/win32icon:$icon" }
$cscArgs += $sources

& (Join-Path $fw 'csc.exe') @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\Nook.exe"
