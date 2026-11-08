<#
 commonnet.ps1 - обход геоблокировок через сервис commonnet.work (ASTRACAT).

 Почему hosts, а не DNS: сервер dns.commonnet.work работает только по DoH/DoT
 (порт 53 у него закрыт), а Windows 10 в правилах NRPT умеет лишь обычный DNS.
 Плюс его адрес 94.181.44.39 с этой сети недоступен вовсе. Поэтому используется
 вторая схема того же сервиса - их прокси-IP 217.60.179.6 в hosts.

 Использование (нужны права администратора):
   powershell -ExecutionPolicy Bypass -File commonnet.ps1 -Show
   powershell -ExecutionPolicy Bypass -File commonnet.ps1 -Apply
   powershell -ExecutionPolicy Bypass -File commonnet.ps1 -Revert
   powershell -ExecutionPolicy Bypass -File commonnet.ps1 -Revert -RestoreNrpt <backup.json>
#>
param(
    [switch]$Show,
    [switch]$Apply,
    [switch]$Revert,
    [string]$RestoreNrpt = ''
)

$HostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$Dir       = Split-Path -Parent $MyInvocation.MyCommand.Path
$Log       = Join-Path $Dir 'commonnet-log.txt'
$MarkBegin = '# commonnet-bypass BEGIN'
$MarkEnd   = '# commonnet-bypass END'
$ProxyIp   = '217.60.179.6'

$Domains = @(
    # OpenAI / ChatGPT
    'chatgpt.com', 'ab.chatgpt.com', 'operator.chatgpt.com', 'webrtc.chatgpt.com', 'sora.chatgpt.com',
    'api.openai.com', 'auth.openai.com', 'auth0.openai.com', 'openai.com', 'platform.openai.com',
    'developers.openai.com', 'cdn.platform.openai.com', 'videos.openai.com', 'android.chat.openai.com',
    'ios.chat.openai.com', 'tcr9i.chat.openai.com', 'cdn.oaistatic.com', 'files.oaiusercontent.com',
    # Gemini / Google AI
    'gemini.google.com', 'aistudio.google.com', 'notebooklm.google.com', 'generativelanguage.googleapis.com',
    # Spotify
    'spotify.com', 'open.spotify.com', 'accounts.spotify.com', 'www.spotify.com'
)

function Log($msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Write-Host $line
    Add-Content -Path $Log -Value $line -Encoding UTF8
}

function Write-Hosts([string[]]$lines) {
    $enc = New-Object System.Text.ASCIIEncoding
    [IO.File]::WriteAllLines($HostsPath, $lines, $enc)
}

function Find-Marker([string[]]$lines, [string]$marker) {
    # возвращает номер строки (с 1) или 0
    $hits = @($lines | Select-String -SimpleMatch $marker)
    if ($hits.Count -eq 0) { return 0 }
    return [int]$hits[0].LineNumber
}

if (-not ($Show -or $Apply -or $Revert)) {
    Write-Host "Укажите -Show, -Apply или -Revert."
    exit 1
}

# --- ПОКАЗАТЬ ---
if ($Show) {
    Log '--- состояние ---'
    $h = @(Get-Content $HostsPath)
    $i = Find-Marker $h $MarkBegin
    if ($i) {
        $j = Find-Marker $h $MarkEnd
        Log ("hosts: блок найден, строк {0}-{1}" -f $i, $j)
        $h[($i - 1)..($j - 1)] | ForEach-Object { Write-Host "  $_" }
    } else {
        Log 'hosts: блока commonnet нет'
    }
    $r = Get-DnsClientNrptRule -ErrorAction SilentlyContinue
    if ($r) {
        Log ("NRPT: правил {0}" -f @($r).Count)
        $r | ForEach-Object { Write-Host ("  " + ($_.Namespace -join ', ') + " -> " + ($_.NameServers -join ', ')) }
    } else {
        Log 'NRPT: правил нет'
    }
    $live = @()
    foreach ($d in @('chatgpt.com', 'gemini.google.com', 'open.spotify.com')) {
        $a = Resolve-DnsName $d -Type A -QuickTimeout -ErrorAction SilentlyContinue |
             Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress -Unique
        $live += ("{0} -> {1}" -f $d, $(if ($a) { ($a | Select-Object -First 4) -join ', ' } else { 'нет ответа' }))
    }
    Log 'резолв сейчас:'
    $live | ForEach-Object { Write-Host "  $_" }
    exit 0
}

# --- ПРИМЕНИТЬ ---
if ($Apply) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $bak = Join-Path $Dir "hosts-backup-commonnet-$stamp.txt"
    Copy-Item $HostsPath $bak -Force
    Log ("бэкап hosts: " + $bak)

    $h = @(Get-Content $HostsPath)
    # если прежний блок есть - вырезать, чтобы не дублировать
    $i = Find-Marker $h $MarkBegin
    if ($i) {
        $j = Find-Marker $h $MarkEnd
        if ($j -ge $i) {
            $clean = New-Object System.Collections.Generic.List[string]
            for ($k = 0; $k -lt $h.Count; $k++) {
                $lineNo = $k + 1
                if ($lineNo -lt $i -or $lineNo -gt $j) { $clean.Add($h[$k]) }
            }
            $h = $clean.ToArray()
            Log 'прежний блок удалён'
        }
    }

    $new = New-Object System.Collections.Generic.List[string]
    foreach ($l in $h) { $new.Add($l) }
    # убрать пустые строки в хвосте, чтобы блок не уползал вниз при повторных -Apply
    while ($new.Count -gt 0 -and [string]::IsNullOrWhiteSpace($new[$new.Count - 1])) {
        $new.RemoveAt($new.Count - 1)
    }
    $new.Add('')
    $new.Add("$MarkBegin (dns.commonnet.work / proxy $ProxyIp)")
    $new.Add("# added $stamp. Revert: commonnet.ps1 -Revert")
    foreach ($d in $Domains) { $new.Add("$ProxyIp`t$d") }
    $new.Add($MarkEnd)

    if ($new.Count -lt $Domains.Count) { Log 'что-то не так со списком доменов - не записываю'; exit 1 }
    Write-Hosts ([string[]]$new.ToArray())
    ipconfig /flushdns | Out-Null
    Log ("hosts обновлён: доменов {0}, строк стало {1}" -f $Domains.Count, $new.Count)

    foreach ($d in @('chatgpt.com', 'gemini.google.com', 'open.spotify.com')) {
        $a = Resolve-DnsName $d -Type A -QuickTimeout -ErrorAction SilentlyContinue |
             Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress -Unique
        Log ("  {0} -> {1}" -f $d, $(if ($a) { ($a | Select-Object -First 4) -join ', ' } else { 'нет ответа' }))
    }
    exit 0
}

# --- ОТКАТИТЬ ---
if ($Revert) {
    $h = @(Get-Content $HostsPath)
    $i = Find-Marker $h $MarkBegin
    if ($i) {
        $j = Find-Marker $h $MarkEnd
        if ($j -and $j -ge $i) {
            # $h индексируется с нуля: строке N соответствует $h[N-1].
            # Вырезаем строки $i..$j включительно.
            $new = New-Object System.Collections.Generic.List[string]
            for ($k = 0; $k -lt $h.Count; $k++) {
                $lineNo = $k + 1
                if ($lineNo -lt $i -or $lineNo -gt $j) { $new.Add($h[$k]) }
            }
            if ($new.Count -lt 5) {
                Log 'hosts: после вырезания осталось меньше 5 строк - не записываю (защита от затирания)'
                exit 1
            }
            Write-Hosts ([string[]]$new.ToArray())
            Log ("hosts: блок commonnet удалён (строк было {0}, стало {1})" -f $h.Count, $new.Count)
        } else {
            Log 'hosts: маркер END не найден, ничего не тронуто'
        }
    } else {
        Log 'hosts: блока commonnet нет, трогать нечего'
    }

    if ($RestoreNrpt) {
        if (-not (Test-Path $RestoreNrpt)) { Log "нет файла NRPT: $RestoreNrpt"; exit 1 }
        $rules = Get-Content $RestoreNrpt -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($r in $rules) {
            if (-not $r.Namespace) { continue }
            try {
                Add-DnsClientNrptRule -Namespace $r.Namespace -NameServers $r.NameServers -ErrorAction Stop
                Log ("NRPT: возвращено " + ($r.Namespace -join ', '))
            } catch {
                Log ("NRPT: не удалось вернуть " + ($r.Namespace -join ', ') + " - " + $_.Exception.Message)
            }
        }
    } else {
        Log 'NRPT: не трогаю (укажите -RestoreNrpt <файл>, если нужно вернуть правила)'
    }

    ipconfig /flushdns | Out-Null
    Log 'кэш DNS сброшен'
    exit 0
}
