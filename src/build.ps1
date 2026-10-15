$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$packageDirectory = Split-Path -Parent $sourceDirectory
$buildDirectory = Join-Path $packageDirectory 'build'
$assetDirectory = Join-Path $buildDirectory 'assets'
$testDirectory = Join-Path $buildDirectory 'tests'
$logoPath = Join-Path $packageDirectory 'assets\Android Mirror logo.svg'
foreach ($folder in @($assetDirectory, $testDirectory)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
$frameworkDirectory = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDirectory 'csc.exe'
$wpfDirectory = Join-Path $frameworkDirectory 'WPF'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler was not found.' }
$engineDirectory = Join-Path $packageDirectory 'engine'
$runtimeFiles = @('Android Mirror Runtime.bin','adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','avcodec-63.dll','avformat-63.dll','avutil-61.dll','libusb-1.0.dll','SDL3.dll','swresample-7.dll','scrcpy-server','scrcpy.png','disconnected.png','LICENSE.txt')
foreach ($name in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $engineDirectory $name))) { throw ('Missing bundled runtime: engine/' + $name) }
}

$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + (Join-Path $packageDirectory 'Android Mirror.exe')),
    ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')),
    ('/resource:' + (Join-Path $sourceDirectory 'MainWindow.xaml') + ',MainWindow.xaml'),
    ('/resource:' + (Join-Path $sourceDirectory 'engine-help.txt') + ',engine-help.txt'),
    ('/reference:' + (Join-Path $wpfDirectory 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $wpfDirectory 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpfDirectory 'WindowsBase.dll')),
    '/reference:System.Xaml.dll', '/reference:System.Core.dll', '/reference:System.Xml.Linq.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    (Join-Path $sourceDirectory 'AssemblyInfo.cs'), (Join-Path $sourceDirectory 'Connection.cs'), (Join-Path $sourceDirectory 'Devices.cs'), (Join-Path $sourceDirectory 'EngineOptions.cs'), (Join-Path $sourceDirectory 'EngineUi.cs'), (Join-Path $sourceDirectory 'EngineUpdates.cs'), (Join-Path $sourceDirectory 'UpdatesUi.cs'), (Join-Path $sourceDirectory 'Motion.cs'), (Join-Path $sourceDirectory 'Wireless.cs'), (Join-Path $sourceDirectory 'WirelessUi.cs'), (Join-Path $sourceDirectory 'QrImage.cs'), (Join-Path $sourceDirectory 'SvgLogo.cs'), (Join-Path $sourceDirectory 'WindowsIntegration.cs'), (Join-Path $sourceDirectory 'Shell.cs'))
$qrLibrary = Join-Path $packageDirectory 'vendor\QRCoder\QRCoder.dll'
$arguments += Join-Path $sourceDirectory 'Dialogs.cs'
$arguments += Join-Path $sourceDirectory 'ManageDevicesUi.cs'
$arguments += Join-Path $sourceDirectory 'InputFocus.cs'
$arguments += Join-Path $sourceDirectory 'EngineLayout.cs'
$arguments += Join-Path $sourceDirectory 'ScreenSharingWindow.cs'
$arguments += Join-Path $sourceDirectory 'AppUpdates.cs'
$arguments += Join-Path $sourceDirectory 'AppUpdatesUi.cs'
$arguments += Join-Path $sourceDirectory 'DiagnosticText.cs'
$arguments += Join-Path $sourceDirectory 'NativeNotifications.cs'
$arguments += Join-Path $sourceDirectory 'ReleaseNotesSummary.cs'
if ((Get-FileHash -LiteralPath $qrLibrary -Algorithm SHA256).Hash -ne '912272105F42564AC49693F997CDE1B05C3BB5D1A7640B555CD2BF6CD852348C') { throw 'The vendored QRCoder library does not match the pinned version.' }
$arguments += '/reference:' + $qrLibrary
$arguments += '/resource:' + $qrLibrary + ',QRCoder.dll'

# Generate native icon sizes directly from the user's SVG; keep its paths intact.
$assetTool = Join-Path $assetDirectory 'logo-assets.exe'
& $compiler /nologo /target:exe /define:ASSET_TOOL ('/out:' + $assetTool) ('/reference:' + (Join-Path $wpfDirectory 'PresentationCore.dll')) ('/reference:' + (Join-Path $wpfDirectory 'WindowsBase.dll')) /reference:System.Xml.Linq.dll /reference:System.Xaml.dll /reference:System.Core.dll (Join-Path $sourceDirectory 'SvgLogo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Logo asset tool build failed.' }
& $assetTool $logoPath $assetDirectory
if ($LASTEXITCODE -ne 0) { throw 'SVG rendering failed.' }
$iconPath = Join-Path $assetDirectory 'app.ico'
$mirrorIconDirectory = Join-Path $packageDirectory 'assets\icons'
New-Item -ItemType Directory -Path $mirrorIconDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $assetDirectory 'brand.png') -Destination (Join-Path $mirrorIconDirectory 'scrcpy.png') -Force
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $mirrorIconDirectory 'Android Mirror.ico') -Force
# Only known branding changes may refresh the trusted engine inventory.
$brandTool = Join-Path $assetDirectory 'brand-engine.exe'
& $compiler /nologo /target:exe /define:BRANDING_TOOL ('/out:' + $brandTool) (Join-Path $sourceDirectory 'EngineBranding.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine icon tool build failed.' }
$inventoryPath = Join-Path $engineDirectory 'engine-manifest.json'
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
if ($inventory.Source -in @('upstream','upstream-branded')) {
    foreach ($file in $inventory.Files) {
        if ((Get-FileHash -LiteralPath (Join-Path $engineDirectory $file) -Algorithm SHA256).Hash -ne $inventory.Hashes.$file) { throw 'Local engine modifications detected; keep them in a custom engine build before updating branding.' }
    }
    $iconHash = (Get-FileHash -LiteralPath $iconPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($inventory.BrandIconSha256 -ne $iconHash) {
        $enginePath = Join-Path $engineDirectory 'Android Mirror Runtime.bin'
        $originalHash = if ($inventory.OriginalExecutableSha256) { $inventory.OriginalExecutableSha256 } else { (Get-FileHash -LiteralPath $enginePath -Algorithm SHA256).Hash.ToLowerInvariant() }
        & $brandTool $enginePath $iconPath
        if ($LASTEXITCODE -ne 0) { throw 'Engine icon update failed.' }
        $inventory.Source = 'upstream-branded'
        $inventory | Add-Member -NotePropertyName OriginalExecutableSha256 -NotePropertyValue $originalHash -Force
        $inventory | Add-Member -NotePropertyName BrandIconSha256 -NotePropertyValue $iconHash -Force
        $inventory.Hashes.'Android Mirror Runtime.bin' = (Get-FileHash -LiteralPath $enginePath -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($inventoryPath, ($inventory | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    }
}
$arguments += '/win32icon:' + $iconPath
$arguments += '/resource:' + $iconPath + ',app.ico'
$arguments += '/resource:' + (Join-Path $assetDirectory 'brand.png') + ',app-artwork.png'
$arguments += '/resource:' + $logoPath + ',brand.svg'
$engineEntryPath = Join-Path $engineDirectory 'Android Mirror Engine.exe'
& $compiler /nologo /target:winexe /optimize+ ('/out:' + $engineEntryPath) ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')) /reference:System.Core.dll (Join-Path $sourceDirectory 'EngineEntry.cs') (Join-Path $sourceDirectory 'AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine UI entry build failed.' }
& $engineEntryPath --refresh-icon
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built ' + (Join-Path $packageDirectory 'Android Mirror.exe'))

$testArguments = @('/nologo', '/target:exe', '/reference:System.Core.dll', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    ('/out:' + (Join-Path $testDirectory 'connection-tests.exe')),
    (Join-Path $sourceDirectory 'AssemblyInfo.cs'),
    ('/resource:' + (Join-Path $sourceDirectory 'engine-help.txt') + ',engine-help.txt'),
    (Join-Path $sourceDirectory 'Connection.cs'), (Join-Path $sourceDirectory 'Devices.cs'), (Join-Path $sourceDirectory 'EngineOptions.cs'), (Join-Path $sourceDirectory 'EngineUpdates.cs'), (Join-Path $sourceDirectory 'Wireless.cs'), (Join-Path $sourceDirectory 'ConnectionTests.cs'), (Join-Path $sourceDirectory 'MultiDeviceTests.cs'), (Join-Path $sourceDirectory 'WirelessTests.cs'), (Join-Path $sourceDirectory 'EngineFeatureTests.cs'), (Join-Path $sourceDirectory 'EngineUpdateTests.cs'))
$testArguments += @('/reference:System.Drawing.dll',(Join-Path $sourceDirectory 'EngineLayout.cs'),(Join-Path $sourceDirectory 'EngineBranding.cs'))
$testArguments += @('/reference:System.Windows.Forms.dll',(Join-Path $sourceDirectory 'ScreenSharingWindow.cs'))
$testArguments += @((Join-Path $sourceDirectory 'AppUpdates.cs'),(Join-Path $sourceDirectory 'AppUpdateTests.cs'))
$testArguments += Join-Path $sourceDirectory 'DiagnosticText.cs'
$testArguments += Join-Path $sourceDirectory 'ReleaseNotesSummary.cs'
& $compiler @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $testDirectory 'connection-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Connection tests failed.' }

$toolsDirectory = Join-Path $packageDirectory 'tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
& $compiler /nologo /target:winexe /optimize+ ('/out:' + (Join-Path $toolsDirectory 'Engine Updater.exe')) ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')) /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $sourceDirectory 'AssemblyInfo.cs') (Join-Path $sourceDirectory 'EngineUpdates.cs') (Join-Path $sourceDirectory 'EngineUpdater.cs') (Join-Path $sourceDirectory 'EngineLayout.cs') (Join-Path $sourceDirectory 'EngineBranding.cs')
if ($LASTEXITCODE -ne 0) { throw 'Engine updater build failed.' }
& $compiler /nologo /target:winexe /optimize+ ('/out:' + (Join-Path $toolsDirectory 'App Updater.exe')) ('/win32icon:' + $iconPath) ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')) /reference:System.Core.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $sourceDirectory 'AssemblyInfo.cs') (Join-Path $sourceDirectory 'AppUpdates.cs') (Join-Path $sourceDirectory 'AppUpdater.cs') (Join-Path $sourceDirectory 'EngineUpdates.cs') (Join-Path $sourceDirectory 'EngineLayout.cs')
if ($LASTEXITCODE -ne 0) { throw 'App updater build failed.' }

# Android Mirror.exe is the single public app launcher.
