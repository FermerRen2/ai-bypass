#Requires -Version 5.1
<#
    geohide-nrpt.ps1 — переводит точечные правила NRPT с мёртвого xbox-dns
    (111.88.96.50/51) на серверы GeoHide DNS (dns.geohide.ru).

    Зачем: старые правила живы и указывают на неработающий DNS — из-за этого
    chatgpt.com, gemini.google.com, openai.com и др. отдавали 0.0.0.0 на всей машине.

    Что делает:
      1. сохраняет бэкап текущих правил (JSON + текст) и скрипт откатa;
      2. переписывает NameServers у существующих правил на серверы GeoHide (UDP/53);
      3. добавляет правила Spotify (spotify.com и .spotify.com), если их нет;
      4. чистит кэш DNS и печатает проверку резолва.

    Запуск (нужны права администратора):
        powershell -NoProfile -ExecutionPolicy Bypass -File geohide-nrpt.ps1 -Apply
        powershell -NoProfile -ExecutionPolicy Bypass -File geohide-nrpt.ps1 -Show
        powershell -NoProfile -ExecutionPolicy Bypass -File geohide-nrpt.ps1 -Revert

    -Show (по умолчанию) ничего не меняет. -Revert восстанавливает правила из
    последнего бэкапа. Лог: geohide-log.txt рядом со скриптом.
#>

[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Revert,
    [switch]$Show
)

$log = Join-Path $PSScriptRoot 'geohide-log.txt'
function W($m) {
    ($m | Out-String).TrimEnd() | Add-Content -Path $log -Encoding utf8
    Write-Host $m
}

# Серверы GeoHide DNS, регион RU (проверены с этой машины: отвечают по UDP/53 и TCP/53,
# отдают адреса своих прокси для доменов из списка). ru-5 = 37.230.192.51 не берём:
# в их же servers.json он помечен traffic:"no". США недоступны из нашей сети.
$GeoHide = @('159.194.200.33', '193.233.112.67', '193.233.112.68')

$SpotifyNs = @('spotify.com', '.spotify.com')
$SpotifyName = 'Spotify -> GeoHide DNS'

'' | Out-File -FilePath $log -Encoding utf8
W ('=== ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' | apply=' + [bool]$Apply + ' revert=' + [bool]$Revert + ' ===')

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
W ('администратор: ' + $admin)
if (($Apply -or $Revert) -and -not $admin) {
    W 'БЕЗ ПРАВ АДМИНИСТРАТОРА правила NRPT не изменить. Выход.'
    return
}

# -ErrorAction Stop обязателен: иначе «Отказано в доступе» — незавершающаяся ошибка,
# catch её не поймает, а пустой список будет выглядеть как «правил нет».
try { $before = @(Get-DnsClientNrptRule -ErrorAction Stop) }
catch { W ('Правила NRPT не прочитать: ' + $_.Exception.Message); return }

W ''
W '--- правила NRPT сейчас ---'
$before | Select-Object DisplayName, Namespace, NameServers |
    Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }

if ($Revert) {
    $bak = Get-ChildItem -Path $PSScriptRoot -Filter 'nrpt-backup-geohide-*.json' -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $bak) { W 'Бэкапа не найдено — откат невозможен.'; return }
    W ('Откат из: ' + $bak.FullName)
    $saved = Get-Content $bak.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($r in $before) {
        try { Remove-DnsClientNrptRule -Name $r.Name -Force -ErrorAction Stop; W ('  удалено: ' + $r.DisplayName) }
        catch { W ('  ОШИБКА удаления ' + $r.DisplayName + ': ' + $_.Exception.Message) }
    }
    foreach ($r in $saved) {
        $ns = @($r.Namespace) -join ', '
        try {
            Add-DnsClientNrptRule -Namespace @($r.Namespace) -NameServers @($r.NameServers) `
                -DisplayName $r.DisplayName -Comment $r.Comment -ErrorAction Stop | Out-Null
            W ('  восстановлено: ' + $r.DisplayName + ' [' + $ns + '] -> ' + (@($r.NameServers) -join ', '))
        } catch { W ('  ОШИБКА восстановления ' + $r.DisplayName + ': ' + $_.Exception.Message) }
    }
    try { Clear-DnsClientCache -ErrorAction Stop; W 'кэш DNS очищен' } catch { W ('кэш НЕ очищен: ' + $_.Exception.Message) }
    W ''
    W '--- правила NRPT после отката ---'
    Get-DnsClientNrptRule -ErrorAction SilentlyContinue |
        Select-Object DisplayName, Namespace, NameServers | Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }
    W ('Готово. Лог: ' + $log)
    return
}

if (-not $Apply) {
    W ''
    W 'Режим просмотра — ничего не менялось. Для правки: -Apply'
    return
}

# --- бэкап ---
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bakJson = Join-Path $PSScriptRoot ('nrpt-backup-geohide-' + $stamp + '.json')
$bakTxt  = Join-Path $PSScriptRoot ('nrpt-backup-geohide-' + $stamp + '.txt')
$before | Select-Object DisplayName, Namespace, NameServers, Comment, DnsSecValidationRequired |
    ConvertTo-Json -Depth 5 | Out-File $bakJson -Encoding utf8
$before | Format-List * | Out-String | Out-File $bakTxt -Encoding utf8
W ''
W ('бэкап: ' + $bakTxt)
W ('бэкап для откатa: ' + $bakJson + ' (правил: ' + $before.Count + ')')

# --- замена серверов у существующих правил ---
W ''
W ('--- NameServers -> ' + ($GeoHide -join ', ') + ' ---')
foreach ($r in $before) {
    $ns = (@($r.Namespace) -join ', ')
    # Скобки обязательны: без них PowerShell разбирает `-join ',' -eq $GeoHide` как часть
    # оператора -join, сравнение выходит всегда истинным и правило молча пропускается.
    $cur  = ((@($r.NameServers) | Sort-Object) -join ',')
    $want = (($GeoHide | Sort-Object) -join ',')
    if ($cur -eq $want) { W ('  уже так: ' + $ns); continue }
    try {
        Set-DnsClientNrptRule -Name $r.Name -NameServers $GeoHide `
            -Comment 'Обход геоблокировок: DNS-прокси GeoHide (dns.geohide.ru)' -ErrorAction Stop
        W ('  обновлено: ' + $ns + ' -> ' + ($GeoHide -join ', '))
    } catch { W ('  ОШИБКА для ' + $ns + ': ' + $_.Exception.Message) }
}

# --- правила Spotify ---
W ''
W '--- правила Spotify ---'
foreach ($ns in $SpotifyNs) {
    if ($before | Where-Object { @($_.Namespace) -contains $ns }) { W ('  уже есть: ' + $ns); continue }
    try {
        Add-DnsClientNrptRule -Namespace $ns -NameServers $GeoHide -DisplayName ($SpotifyName + ' (' + $ns + ')') `
            -Comment 'Обход для Spotify: DNS-прокси GeoHide' -ErrorAction Stop | Out-Null
        W ('  добавлено: ' + $ns)
    } catch { W ('  ОШИБКА для ' + $ns + ': ' + $_.Exception.Message) }
}

try { Clear-DnsClientCache -ErrorAction Stop; W 'кэш DNS очищен' } catch { W ('кэш НЕ очищен: ' + $_.Exception.Message) }

W ''
W '--- правила NRPT после правки ---'
Get-DnsClientNrptRule -ErrorAction SilentlyContinue |
    Select-Object DisplayName, Namespace, NameServers | Format-Table -AutoSize | Out-String | ForEach-Object { W $_ }

W ''
W '--- проверка резолва (обходные домены -> адреса GeoHide, контрольные -> реальные) ---'
foreach ($d in 'chatgpt.com', 'gemini.google.com', 'openai.com', 'api.openai.com', 'auth.openai.com',
               'sora.com', 'spotify.com', 'open.spotify.com', 'accounts.spotify.com',
               'ya.ru', 'github.com', 'youtube.com') {
    try {
        $ips = (Resolve-DnsName -Name $d -Type A -DnsOnly -QuickTimeout -ErrorAction Stop |
                Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress) -join ', '
    } catch { $ips = 'НЕТ ОТВЕТА' }
    W ('{0,-24} -> {1}' -f $d, $ips)
}

W ''
W ('Готово. Лог: ' + $log)
