<#
  doh-check.ps1 — диагностика DNS-блокировок для chatgpt.com / gemini.google.com
  --------------------------------------------------------------------------
  Зачем: обычные DNS-серверы (любые, хоть 15 штук) бессмысленно перебирать,
  если провайдер перехватывает UDP/53 и подменяет ответ. Этот скрипт сравнивает
  ответ системного DNS с ответом DoH (шифрованный DNS) и проверяет, доходит ли
  трафик до настоящего IP.

  Запуск:  powershell -ExecutionPolicy Bypass -File doh-check.ps1
           powershell -ExecutionPolicy Bypass -File doh-check.ps1 -Domains chatgpt.com,claude.ai
#>
param(
  [string[]]$Domains = @('chatgpt.com','gemini.google.com'),
  [string[]]$DohUrls = @(
    'https://cloudflare-dns.com/dns-query',
    'https://1.1.1.1/dns-query',
    'https://dns.google/resolve',
    'https://dns.adguard-dns.com/resolve',
    'https://dns.quad9.net:5053/dns-query',
    'https://doh.sb/dns-query',
    'https://dns.mullvad.net/dns-query',
    'https://freedns.controld.com/p0',
    'https://dns0.eu/'
  )
)

function Get-DohA {
  param([string]$Url, [string]$Name)
  try {
    $r = Invoke-RestMethod "$Url`?name=$Name&type=A" -Headers @{ accept = 'application/dns-json' } -TimeoutSec 8
    $a = @($r.Answer | Where-Object { $_.type -eq 1 } | ForEach-Object { $_.data })
    if ($a.Count -eq 0) { return @('нет A-записи') }
    return $a
  } catch { return @('FAIL') }
}

# Заглушки, которыми провайдер подменяет ответ: 0.0.0.0 и «облачные» 8.47.69.x / 8.6.112.x
function Test-Stub {
  param([string[]]$Ips)
  foreach ($ip in $Ips) {
    if ($ip -eq '0.0.0.0' -or $ip -like '127.*' -or $ip -like '8.47.69.*' -or $ip -like '8.6.112.*') { return $true }
  }
  return $false
}

function Test-HttpViaIp {
  param([string]$Name, [string]$Ip)
  $code = & curl.exe -sS -o NUL -w '%{http_code}' --resolve "${Name}:443:$Ip" "https://$Name/" --max-time 15 2>$null
  if (-not $code) { return '000 (нет соединения)' }
  return $code
}

Write-Output "=== СИСТЕМНЫЙ DNS vs DoH ==="
$real = @{}
foreach ($d in $Domains) {
  $sys = @('FAIL')
  try { $sys = @((Resolve-DnsName $d -Type A -QuickTimeout -ErrorAction Stop | Where-Object { $_.IPAddress }).IPAddress) } catch {}
  if ($sys.Count -eq 0) { $sys = @('пусто') }
  $sysStub = Test-Stub $sys
  Write-Output ("`n[{0}]" -f $d)
  Write-Output ("  системный DNS : {0}  {1}" -f ($sys -join ','), $(if ($sysStub) { '<-- ПОДМЕНА' } else { '' }))

  $good = @()
  foreach ($u in $DohUrls) {
    $a = @(Get-DohA -Url $u -Name $d)   # @() обязательно: иначе строка 'FAIL' даёт $a[0] = 'F'
    $stub = Test-Stub $a
    $mark = if ($a[0] -eq 'FAIL') { 'FAIL' } elseif ($a[0] -eq 'нет A-записи') { 'нет A' } elseif ($stub) { 'ПОДМЕНА' } else { 'OK' }
    Write-Output ("  DoH {0,-42} {1,-46} {2}" -f ($u -replace '^https://'), ($a -join ','), $mark)
    if ($mark -eq 'OK') { $good += ,@($u, $a) }
  }

  if ($good.Count -gt 0) {
    $ip = $good[0][1][0]
    $via = $good[0][0] -replace '^https://'
    Write-Output ("  -> настоящий IP: {0} (взят у {1})" -f $ip, $via)
    $code = Test-HttpViaIp -Name $d -Ip $ip
    Write-Output ("  -> HTTPS к настоящему IP: {0}" -f $code)
    switch -Regex ($code) {
      '^(200|301|302|308)' { Write-Output "  ВЕРДИКТ: сайт доступен, если DNS отдавать через DoH. Простая смена DNS не поможет." }
      '^403'               { Write-Output "  ВЕРДИКТ: блок не в DNS — сервер отказывает по IP/стране. Нужен выход вне РФ (VPN/прокси)." }
      default              { Write-Output "  ВЕРДИКТ: соединение не проходит — вероятна блокировка по SNI/IP на стороне провайдера." }
    }
  } else {
    Write-Output "  ВЕРДИКТ: ни один DoH-сервер не отдал нормальный ответ."
  }
}

Write-Output "`n=== ГОТОВЫЕ ПАРЫ DNS + DoH (для настроек) ==="
Write-Output @'
  Cloudflare   1.1.1.1        1.0.0.1        https://cloudflare-dns.com/dns-query
  Cloudflare   1.1.1.1        1.0.0.1        https://1.1.1.1/dns-query
  AdGuard      94.140.14.14   94.140.15.15   https://dns.adguard-dns.com/dns-query
  Quad9        9.9.9.9        149.112.112.112 https://dns.quad9.net/dns-query
  Google       8.8.8.8        8.8.4.4        https://dns.google/dns-query
  DNS.SB       185.222.222.222 45.11.45.11   https://doh.sb/dns-query
  Mullvad      194.242.2.2    194.242.2.3    https://dns.mullvad.net/dns-query
  ControlD     76.76.2.0      76.76.10.0     https://freedns.controld.com/p0
  dns0.eu      193.110.81.0   185.253.5.0    https://dns0.eu/
'@
