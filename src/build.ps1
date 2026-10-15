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
$runtimeFiles = @('scrcpy.exe','adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','avcodec-63.dll','avformat-63.dll','avutil-61.dll','libusb-1.0.dll','SDL3.dll','swresample-7.dll','scrcpy-server','scrcpy.png','disconnected.png','LICENSE.txt')
foreach ($name in $runtimeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $engineDirectory $name))) { throw ('Missing bundled runtime: engine/' + $name) }
}

$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/out:' + (Join-Path $packageDirectory 'Android Mirror.exe')),
    ('/win32manifest:' + (Join-Path $sourceDirectory 'app.manifest')),
    ('/resource:' + (Join-Path $sourceDirectory 'MainWindow.xaml') + ',MainWindow.xaml'),
    ('/reference:' + (Join-Path $wpfDirectory 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $wpfDirectory 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpfDirectory 'WindowsBase.dll')),
    '/reference:System.Xaml.dll', '/reference:System.Core.dll', '/reference:System.Xml.Linq.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    (Join-Path $sourceDirectory 'AssemblyInfo.cs'), (Join-Path $sourceDirectory 'Connection.cs'), (Join-Path $sourceDirectory 'Devices.cs'), (Join-Path $sourceDirectory 'Wireless.cs'), (Join-Path $sourceDirectory 'WirelessUi.cs'), (Join-Path $sourceDirectory 'QrImage.cs'), (Join-Path $sourceDirectory 'SvgLogo.cs'), (Join-Path $sourceDirectory 'WindowsIntegration.cs'), (Join-Path $sourceDirectory 'Shell.cs'))
$qrLibrary = Join-Path $packageDirectory 'vendor\QRCoder\QRCoder.dll'
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
$arguments += '/win32icon:' + $iconPath
$arguments += '/resource:' + $iconPath + ',app.ico'
$arguments += '/resource:' + (Join-Path $assetDirectory 'brand.png') + ',app-artwork.png'
$arguments += '/resource:' + $logoPath + ',brand.svg'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output ('Built ' + (Join-Path $packageDirectory 'Android Mirror.exe'))

$testArguments = @('/nologo', '/target:exe', '/reference:System.Core.dll',
    ('/out:' + (Join-Path $testDirectory 'connection-tests.exe')),
    (Join-Path $sourceDirectory 'Connection.cs'), (Join-Path $sourceDirectory 'Devices.cs'), (Join-Path $sourceDirectory 'Wireless.cs'), (Join-Path $sourceDirectory 'ConnectionTests.cs'), (Join-Path $sourceDirectory 'MultiDeviceTests.cs'), (Join-Path $sourceDirectory 'WirelessTests.cs'))
& $compiler @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $testDirectory 'connection-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Connection tests failed.' }

# Both entry points open the UI. The actual mirroring engine lives in engine.
$redirectPath = Join-Path $assetDirectory 'redirect-launcher.exe'
& $compiler /nologo /target:winexe /optimize+ ('/out:' + $redirectPath) ('/win32icon:' + $iconPath) /reference:System.Core.dll /reference:System.Windows.Forms.dll (Join-Path $sourceDirectory 'AssemblyInfo.cs') (Join-Path $sourceDirectory 'Connection.cs') (Join-Path $sourceDirectory 'Devices.cs') (Join-Path $sourceDirectory 'Redirect.cs')
if ($LASTEXITCODE -ne 0) { throw 'Redirect launcher build failed.' }
foreach ($alias in @('scrcpy.exe','scrcpy-connect.exe')) { Copy-Item -LiteralPath $redirectPath -Destination (Join-Path $packageDirectory $alias) -Force }
