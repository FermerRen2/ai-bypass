<#
  gemini-hosts.ps1 — прописать в hosts НАСТОЯЩИЕ IP Google-сервисов, полученные
  через DoH (шифрованный DNS). Нужен потому, что провайдер подменяет обычный DNS
  и отдаёт 0.0.0.0, а Windows 10 (сборка 19044) не умеет DoH на уровне системы.

  Отличие от схемы с чужим прокси: IP здесь настоящие, трафик идёт напрямую
  в Google. Никакие третьи стороны его не видят. Минус: IP Google иногда меняются,
  поэтому скрипт надо перезапускать (или ставить DoH-клиент).

  Показ без изменений:  powershell -ExecutionPolicy Bypass -File gemini-hosts.ps1
  Применить:            ... -Apply     (нужны права администратора)
  Откатить всё:         ... -Revert
#>
param(
  [switch]$Apply,
  [switch]$Revert,
  [string[]]$Domains = @('gemini.google.com','aistudio.google.com','notebooklm.google.com'),
  [string]$DohUrl = 'https://cloudflare-dns.com/dns-query'
)

$HostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$Mark      = '# --- gemini-hosts ---'
$OldMark   = '# --- ai-unblock.ps1 ---'   # подчищаем прошлую (прокси) схему
$StubPattern = '^(0\.0\.0\.0|127\.|8\.47\.69\.|8\.6\.112\.)'

function Clear-Blocks {
  param([string[]]$Lines)
  $keep = @(); $skip = $false
  foreach ($l in $Lines) {
    if ($l -match [regex]::Escape($Mark) -or $l -match [regex]::Escape($OldMark)) { $skip = $true; continue }
    if ($skip -and $l -match '^\s*#\s*---\s*end') { $skip = $false; continue }
    if (-not $skip) { $keep += $l }
  }
  return $keep
}

if ($Revert) {
  try {
    Copy-Item $HostsPath "$HostsPath.bak" -Force
    [IO.File]::WriteAllLines($HostsPath, (Clear-Blocks (Get-Content $HostsPath)), [Text.Encoding]::ASCII)
    ipconfig /flushdns | Out-Null
    Write-Output "Записи удалены, кэш очищен. Бэкап: $HostsPath.bak"
  } catch { Write-Output "Ошибка записи hosts: $($_.Exception.Message)"; exit 1 }
  exit 0
}

function Get-RealIp {
  param([string]$Name)
  foreach ($try in 1..4) {
    try {
      $r = Invoke-RestMethod "$DohUrl`?name=$Name&type=A&cb=$try" -Headers @{ accept = 'application/dns-json' } -TimeoutSec 8
      $ips = @($r.Answer | Where-Object { $_.type -eq 1 } | ForEach-Object { $_.data } |
        Where-Object { $_ -notmatch $StubPattern } | Select-Object -Unique)
      if ($ips.Count -gt 0) { return $ips[0] }
    } catch { Start-Sleep -Milliseconds 300 }
  }
  return $null
}

Write-Output "=== Настоящие IP через DoH ($DohUrl) ==="
$map = [ordered]@{}
foreach ($d in $Domains) {
  $ip = Get-RealIp $d
  if ($ip) {
    $code = & curl.exe -sS -o NUL -w '%{http_code}' --resolve "${d}:443:$ip" "https://$d/" --max-time 20 2>$null
    Write-Output ("  {0,-26} -> {1,-16} HTTPS={2}" -f $d, $ip, $(if ($code) { $code } else { 'нет ответа' }))
    if ($code -match '^(200|301|302|308)') { $map[$d] = $ip } else { Write-Output "     (не беру: сайт не отвечает по этому IP)" }
  } else {
    Write-Output ("  {0,-26} -> DoH не дал настоящий IP" -f $d)
  }
}

if ($map.Count -eq 0) { Write-Output "Нечего записывать."; exit 1 }
$block = @($Mark) + ($map.GetEnumerator() | ForEach-Object { "{0}`t{1}" -f $_.Value, $_.Key }) + @('# --- end gemini-hosts ---')

if (-not $Apply) {
  Write-Output "`n=== Записи, которые были бы добавлены (режим показа) ==="
  $block | ForEach-Object { Write-Output "  $_" }
  Write-Output "`nПрименить: запустить от администратора с -Apply"
  exit 0
}

try {
  Copy-Item $HostsPath "$HostsPath.bak" -Force
  $existing = Clear-Blocks (Get-Content $HostsPath)
  [IO.File]::WriteAllLines($HostsPath, ($existing + $block), [Text.Encoding]::ASCII)
  ipconfig /flushdns | Out-Null
  Write-Output "`nЗаписи добавлены, кэш очищен. Бэкап: $HostsPath.bak"
  Write-Output "`n=== Контроль: резолв через систему (уже без DoH) ==="
  foreach ($d in $map.Keys) {
    $a = (Resolve-DnsName $d -Type A -QuickTimeout -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress }).IPAddress -join ','
    Write-Output ("  {0,-26} -> {1}" -f $d, $a)
  }
} catch { Write-Output "Ошибка записи hosts: $($_.Exception.Message)"; exit 1 }
