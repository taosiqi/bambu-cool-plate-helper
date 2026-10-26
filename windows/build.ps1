$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force dist/windows | Out-Null
$common = @('/nologo', '/utf8output', '/codepage:65001', '/platform:x64', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', '/win32icon:assets/AppIcon.ico')
& $csc @common /target:exe /out:dist/HelperTests.exe windows/Program.cs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& ./dist/HelperTests.exe --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self-test failed' }
& $csc @common /target:winexe /out:dist/windows/BambuCoolPlateHelper.exe windows/Program.cs
if ($LASTEXITCODE -ne 0) { throw 'App compilation failed' }
$p = Start-Process -FilePath './dist/windows/BambuCoolPlateHelper.exe' -ArgumentList '--render-preview' -PassThru -Wait
if ($p.ExitCode -ne 0) { throw 'UI preview failed' }
Copy-Item README.md dist/windows/README.md
Compress-Archive -Path dist/windows/* -DestinationPath dist/BambuCoolPlateHelper-v1.2.0-windows-x64.zip -Force
