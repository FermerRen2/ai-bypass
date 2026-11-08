<#
  ai-unblock.ps1 — доступ к ChatGPT / Gemini через DNS-прокси (тот же приём,
  на котором работал Xbox DNS): DNS отдаёт не настоящий IP сайта, а IP прокси,
  который сам ходит на сайт из-за границы.

  ВАЖНО ПРО БЕЗОПАСНОСТЬ: трафик к этим доменам идёт через ЧУЖИЕ серверы
  (прокси-пул comss.one, PTR mr1/mr3.gusliakov.clodocd.com). Логин, пароль
  и cookies ChatGPT проходят через них. Это цена схемы; для Gemini она не нужна —
  Gemini работает по DoH с настоящими IP.

  Запуск без прав администратора (только показать, ничего не менять):
      powershell -ExecutionPolicy Bypass -File ai-unblock.ps1
  Применить (нужен запуск от администратора, меняет файл hosts):
      powershell -ExecutionPolicy Bypass -File ai-unblock.ps1 -Apply
  Откатить:
      powershell -ExecutionPolicy Bypass -File ai-unblock.ps1 -Revert
#>
param(
  [switch]$Apply,
  [switch]$Revert,
  [string]$Server = '212.109.195.93'   # dns.comss.one: отдаёт IP прокси-пула
)

$Domains = @(
  'chatgpt.com','ab.chatgpt.com','webrtc.chatgpt.com',
  'auth.openai.com','auth0.openai.com','api.openai.com','platform.openai.com',
  'cdn.oaistatic.com','files.oaiusercontent.com','cdn.auth0.com',
  'gemini.google.com','aistudio.google.com','claude.ai'
)
$HostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$Mark = '# --- ai-unblock.ps1 ---'

function Get-ProxyIp {
  param([string]$Name)
  # TCP/53: UDP-запросы провайдер подменяет, TCP проходит целым
  $out = & nslookup -vc $Name $Server 2>$null
  $ips = @($out | Select-String -Pattern '^\s+(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s*$' |
    ForEach-Object { $_.Matches[0].Groups[1].Value } |
    Where-Object { $_ -ne '0.0.0.0' } | Select-Object -Unique)
  if ($ips.Count -eq 0) { return $null }
  return $ips[0]
}

if ($Revert) {
  if (-not (Test-Path $HostsPath)) { Write-Output "hosts не найден"; exit 1 }
  $lines = Get-Content $HostsPath
  $before = $lines.Count
  $keep = @(); $skip = $false
  foreach ($l in $lines) {
    if ($l -match [regex]::Escape($Mark)) { $skip = $true; continue }
    if ($skip -and $l -match '^\s*#\s*---\s*end') { $skip = $false; continue }
    if (-not $skip) { $keep += $l }
  }
  try {
    Copy-Item $HostsPath "$HostsPath.bak" -Force
    if ($keep.Count -eq 0) { Write-Output 'ОТКАЗ: после очистки hosts пуст, запись не выполняю'; exit 1 }
    [IO.File]::WriteAllLines($HostsPath, $keep, [Text.Encoding]::ASCII)
    ipconfig /flushdns | Out-Null
    Write-Output "Записи ai-unblock удалены (было строк: $before, стало: $($keep.Count)). Бэкап: $HostsPath.bak"
  } catch { Write-Output "Не удалось записать hosts (нужны права администратора): $($_.Exception.Message)" }
  exit 0
}

Write-Output "=== Опрос $Server (TCP/53) ==="
$map = [ordered]@{}
foreach ($d in $Domains) {
  $ip = Get-ProxyIp $d
  if ($ip) { $map[$d] = $ip; Write-Output ("  {0,-24} -> {1}" -f $d, $ip) }
  else     { Write-Output ("  {0,-24} -> нет ответа (пропускаю)" -f $d) }
}

if ($map.Count -eq 0) { Write-Output "Прокси-IP не получены — сервис недоступен или сменил адрес."; exit 1 }

$lines = @($Mark) + ($map.GetEnumerator() | ForEach-Object { "{0}`t{1}" -f $_.Value, $_.Key }) + @('# --- end ai-unblock ---')

if (-not $Apply) {
  Write-Output "`n=== Так выглядели бы записи в $HostsPath (режим показа, ничего не изменено) ==="
  $lines | ForEach-Object { Write-Output "  $_" }
  Write-Output "`nЧтобы применить: запустить от администратора с ключом -Apply"
  Write-Output "Проверить после применения: ipconfig /flushdns; curl.exe -sS -o NUL -w `"%{http_code}`" https://chatgpt.com/"
  exit 0
}

# применяем
try {
  Copy-Item $HostsPath "$HostsPath.bak" -Force
  $existing = @(Get-Content $HostsPath | Where-Object { $_ -notmatch [regex]::Escape($Mark) })
  [IO.File]::WriteAllLines($HostsPath, ($existing + $lines), [Text.Encoding]::ASCII)
  ipconfig /flushdns | Out-Null
  Write-Output "`nЗаписи добавлены в hosts, кэш DNS очищен. Бэкап: $HostsPath.bak"
  Write-Output "Откат: -Revert"
} catch {
  Write-Output "`nНе удалось изменить hosts (нужны права администратора): $($_.Exception.Message)"
  exit 1
}
