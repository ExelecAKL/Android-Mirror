$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$packageDirectory = Split-Path -Parent $sourceDirectory
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$setupDirectory = Join-Path $packageDirectory 'build\setup'
$testDirectory = Join-Path $packageDirectory 'build\tests'
$installerDirectory = Join-Path $packageDirectory 'installer'
foreach ($folder in @($setupDirectory, $testDirectory, $installerDirectory)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
$iconPath = Join-Path $packageDirectory 'build\assets\app.ico'
$setupSource = Join-Path $sourceDirectory 'Setup.cs'
$references = @('/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll','/reference:Microsoft.CSharp.dll')
$shared = @('/nologo','/optimize+',('/win32icon:' + $iconPath),('/win32manifest:' + (Join-Path $sourceDirectory 'setup.manifest'))) + $references
$uninstaller = Join-Path $setupDirectory 'Uninstall Android Mirror.exe'
& $compiler @shared /target:winexe /define:UNINSTALLER ('/out:' + $uninstaller) $setupSource
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadPath = Join-Path $setupDirectory 'payload.zip'
if (Test-Path -LiteralPath $payloadPath) { Remove-Item -LiteralPath $payloadPath }
$archive = [IO.Compression.ZipFile]::Open($payloadPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Android Mirror.exe','scrcpy.exe','scrcpy-connect.exe','README.md','LICENSE.txt','NOTICE.txt','assets/Android Mirror logo.svg')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $packageDirectory $name), $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $uninstaller, 'Uninstall Android Mirror.exe', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageDirectory 'engine') -File) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, ('engine/' + $file.Name), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    foreach ($folder in @('licenses','docs')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageDirectory $folder) -File -Recurse) {
            $relative = $file.FullName.Substring($packageDirectory.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $packageDirectory 'assets\screenshots') -File -Filter '*.png') {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, ('assets/screenshots/' + $file.Name), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }

$setupPath = Join-Path $installerDirectory 'Android Mirror Setup.exe'
& $compiler @shared /target:winexe ('/out:' + $setupPath) ('/resource:' + $payloadPath + ',payload.zip') $setupSource
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }

# Same installer code and payload, tested as the current user in a workspace fixture.
$testPath = Join-Path $testDirectory 'setup-tests.exe'
& $compiler /nologo /target:exe /main:SetupTests ('/out:' + $testPath) @references ('/resource:' + $payloadPath + ',payload.zip') $setupSource (Join-Path $sourceDirectory 'SetupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer test build failed.' }
& $testPath
if ($LASTEXITCODE -ne 0) { throw 'Installer checks failed.' }
Write-Output ('Built ' + $setupPath)
