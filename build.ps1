param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path "$framework\csc.exe")) { $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
New-Item -ItemType Directory -Force build | Out-Null
# Generate a simple vector-drawn app icon, without external graphics packages.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(67, 84, 108))
$pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 3.5)
$graphics.DrawRectangle($pen, 16, 16, 32, 32)
$points = [System.Drawing.Point[]]@((New-Object System.Drawing.Point(22, 32)), (New-Object System.Drawing.Point(29, 39)), (New-Object System.Drawing.Point(43, 24)))
$graphics.DrawLines($pen, $points)
$png = New-Object System.IO.MemoryStream
$bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $png.ToArray()
$stream = [IO.File]::Create((Join-Path $PSScriptRoot 'build\icon.ico'))
$writer = New-Object IO.BinaryWriter($stream)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]22); $writer.Write($bytes)
$writer.Dispose(); $png.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001', '/win32manifest:src\app.manifest', '/win32icon:build\icon.ico', '/resource:build\icon.ico,icon.ico', '/out:build\LightWeekTodo.exe')
foreach ($ref in $refs) { $arguments += '/reference:' + (Join-Path $framework $ref) }
$arguments += @('src\Model.cs', 'src\App.cs', 'src\SelfTest.cs')
& "$framework\csc.exe" $arguments
if ($LASTEXITCODE -ne 0) { throw 'App build failed' }
$setupArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output', '/codepage:65001','/win32manifest:src\app.manifest','/win32icon:build\icon.ico','/resource:build\icon.ico,icon.ico','/resource:build\LightWeekTodo.exe,LightWeekTodo.exe','/resource:src\README.txt,README.txt','/out:build\LightWeekTodo-Setup.exe')
foreach ($ref in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')) { $setupArgs += '/reference:' + (Join-Path $framework $ref) }
$setupArgs += 'src\Setup.cs'
& "$framework\csc.exe" $setupArgs
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
if ($Test) {
    foreach ($app in @('LightWeekTodo.exe','LightWeekTodo-Setup.exe')) {
        $process = Start-Process (Join-Path $PSScriptRoot ('build\' + $app)) -ArgumentList '--self-test', 'test-output' -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "$app self-test failed. See test-output." }
    }
}
Get-Item build\LightWeekTodo.exe,build\LightWeekTodo-Setup.exe | Select-Object Name,Length
