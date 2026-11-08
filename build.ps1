# Сборка AiBypass одним файлом через csc.exe из состава .NET Framework.
# Ничего устанавливать не нужно: csc есть на любой Windows с .NET Framework 4.x.
#
#   .\build.ps1            - оконная сборка  -> dist\AiBypass.exe
#   .\build.ps1 -Cli       - консольная      -> dist\AiBypass-cli.exe (для проверок)
#   .\build.ps1 -Clean     - убрать dist и obj

param(
    [switch]$Cli,
    [switch]$Clean,
    [switch]$Release
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'

if ($Clean) {
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    Write-Host 'dist очищен.'
    exit 0
}

# ищем компилятор
$candidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw 'Не найден csc.exe — нужен .NET Framework 4.x (стоит в Windows по умолчанию).'
}

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$files = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }
$target = if ($Cli) { 'exe' } else { 'winexe' }
$out    = if ($Cli) { Join-Path $dist 'AiBypass-cli.exe' } else { Join-Path $dist 'AiBypass.exe' }

# UTF-8 BOM: без него csc читает русский текст в исходниках как ANSI и строки превращаются в кашу
$bom = New-Object System.Text.UTF8Encoding($true)
foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($f, $text, $bom)
}

$ico = Join-Path $src 'app.ico'
# Встроенный адрес прокси: берётся из proxy.txt в корне проекта при обычной сборке.
# При сборке с -Release вшивается пустая строка, чтобы в публичный exe ничего не попало.
$proxyFile = Join-Path $root 'proxy.txt'
$baked = ''
if (-not $Release -and (Test-Path $proxyFile)) { $baked = (Get-Content $proxyFile -Raw).Trim() }
$gen = Join-Path $src 'Generated.Defaults.cs'
$genCode = @"
// Файл создаётся сборкой, править руками не нужно.
namespace AiBypass
{
    internal static class Defaults
    {
        public const string ProxyUrl = "$baked";
    }
}
"@
[IO.File]::WriteAllText($gen, $genCode, (New-Object System.Text.UTF8Encoding($true)))

$refs = @('/r:System.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
if (Test-Path $ico) { $refs += "/win32icon:$ico"; $refs += "/resource:$ico,AiBypass.app.ico" }

$compileArgs = @(
    '/nologo',
    "/target:$target",
    '/platform:anycpu',
    '/optimize+',
    "/win32manifest:$(Join-Path $src 'app.manifest')",
    "/out:$out"
) + $refs + $files

Write-Host "Компилятор: $csc"
Write-Host "Сборка:     $([IO.Path]::GetFileName($out))"
& $csc @compileArgs
if ($LASTEXITCODE -ne 0) { throw "csc вернул $LASTEXITCODE" }

$info = Get-Item $out
Write-Host ("Готово: {0} ({1:N0} байт)" -f $info.FullName, $info.Length)
