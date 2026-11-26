param([string]$OutputPath = (Join-Path $PSScriptRoot 'FindClick2.1.exe'), [string]$TestFile, [string]$TestMain)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Xml.Linq.dll','System.Runtime.WindowsRuntime.dll','WPF\WindowsBase.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll') | ForEach-Object { '/r:' + (Join-Path $framework $_) }
$target = '/target:winexe'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
if ($TestFile) { $target = '/target:exe'; $sources += (Resolve-Path -LiteralPath $TestFile).Path }
$extra = @('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest'))
if ($TestMain) { $extra += '/main:' + $TestMain }
$icon = Join-Path $PSScriptRoot 'app.ico'
if (Test-Path -LiteralPath $icon) { $extra += '/win32icon:' + $icon }
& (Join-Path $framework 'csc.exe') /nologo /optimize+ /platform:anycpu /codepage:65001 $target "/out:$OutputPath" $references $extra $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }


