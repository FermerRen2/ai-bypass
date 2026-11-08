@echo off
chcp 65001 >nul
rem ============================================================================
rem  set-dns.bat — переключение системного DNS в Windows
rem  ВАЖНО: на этом провайдере (Билайн-подобный, IP ВНЕШНИЙ-IP) смена DNS
rem  НЕ помогает: UDP/53 перехватывается и любой сервер отдаёт 0.0.0.0.
rem  Скрипт полезен на других сетях (мобильный интернет, другой провайдер).
rem
rem  Запуск: set-dns.bat cloudflare | adguard | google | quad9 | dhcp | show
rem  Применение требует прав администратора (клик правой кнопкой -> от админа).
rem ============================================================================
setlocal
set MODE=%1
if "%MODE%"=="" set MODE=show

if /i "%MODE%"=="show"   goto :show
if /i "%MODE%"=="dhcp"   goto :dhcp
if /i "%MODE%"=="cloudflare" set DNS1=1.1.1.1& set DNS2=1.0.0.1& goto :apply
if /i "%MODE%"=="adguard"    set DNS1=94.140.14.14& set DNS2=94.140.15.15& goto :apply
if /i "%MODE%"=="google"     set DNS1=8.8.8.8& set DNS2=8.8.4.4& goto :apply
if /i "%MODE%"=="quad9"      set DNS1=9.9.9.9& set DNS2=149.112.112.112& goto :apply
echo Неизвестный режим: %MODE%
echo Доступно: cloudflare ^| adguard ^| google ^| quad9 ^| dhcp ^| show
exit /b 1

:show
echo --- Текущие DNS ---
powershell -NoProfile -Command "Get-DnsClientServerAddress -AddressFamily IPv4 | Where-Object {$_.ServerAddresses} | Format-Table -AutoSize InterfaceAlias,ServerAddresses"
echo --- Что отвечает система на заблокированные домены ---
for %%d in (chatgpt.com gemini.google.com) do (
  echo === %%d ===
  powershell -NoProfile -Command "(Resolve-DnsName %%d -Type A -QuickTimeout -ErrorAction SilentlyContinue).IPAddress" 2>nul
)
echo 0.0.0.0 = ответ подменён провайдером, смена DNS не поможет.
exit /b 0

:dhcp
net session >nul 2>&1 || (echo Нужны права администратора. & exit /b 1)
for /f "tokens=*" %%i in ('powershell -NoProfile -Command "(Get-NetRoute -DestinationPrefix 0.0.0.0/0 | Sort-Object {$_.RouteMetric + $_.ifMetric} | Select-Object -First 1).InterfaceAlias"') do (
  echo Возвращаю автоматический DNS на "%%i"
  netsh interface ip set dns name="%%i" source=dhcp
)
ipconfig /flushdns >nul
exit /b 0

:apply
net session >nul 2>&1 || (echo Нужны права администратора: запустите файл от имени администратора. & exit /b 1)
for /f "tokens=*" %%i in ('powershell -NoProfile -Command "(Get-NetRoute -DestinationPrefix 0.0.0.0/0 | Sort-Object {$_.RouteMetric + $_.ifMetric} | Select-Object -First 1).InterfaceAlias"') do (
  echo Ставлю на "%%i" : %DNS1% / %DNS2%
  netsh interface ip set dns name="%%i" static %DNS1% primary
  netsh interface ip add dns name="%%i" %DNS2% index=2
)
ipconfig /flushdns >nul
echo Готово. Проверка: set-dns.bat show
exit /b 0
