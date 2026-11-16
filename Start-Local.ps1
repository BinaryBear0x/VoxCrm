$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$pgBin = 'C:\Program Files\PostgreSQL\18\bin'
$databasePath = Join-Path $projectRoot '.local-db'
& "$pgBin\pg_isready.exe" -h 127.0.0.1 -p 55432 -U voxcrm | Out-Null
if ($LASTEXITCODE -ne 0) {
    & "$pgBin\pg_ctl.exe" -D $databasePath -l (Join-Path $projectRoot 'local-postgres.log') -o '-p 55432 -h 127.0.0.1' start
    if ($LASTEXITCODE -ne 0) { throw 'Yerel PostgreSQL başlatılamadı.' }
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5114'
$env:ConnectionStrings__DefaultConnection = 'Host=127.0.0.1;Port=55432;Database=voxcrm_snapshot_20261029;Username=voxcrm'
$env:DataSeeding__SeedDemoData = 'false'
$env:DataSeeding__SystemAdmin__Enabled = 'false'
$env:DataSeeding__ProductionDealer__Enabled = 'false'
$env:BackgroundJobs__Enabled = 'false'
$env:SystemHealth__EnableDockerStatus = 'false'
$env:WhatsAppGateway__BaseUrl = 'http://127.0.0.1:8088'
$env:VoxCrmApi__BaseUrl = 'http://127.0.0.1:5072'
$env:PiiEncryption__KeyFile = Join-Path (Split-Path $projectRoot) 'VoxCRM-local-private\pii.key'
if (Get-NetTCPConnection -LocalPort 5114 -State Listen -ErrorAction SilentlyContinue) {
    Write-Output '5114 portu kullanılıyor. Mevcut yerel uygulamayı kontrol edin: http://127.0.0.1:5114'
    exit 0
}
Push-Location $projectRoot
try {
    dotnet build VoxCrm.Web/VoxCrm.Web.csproj --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
    $process = Start-Process 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList (Join-Path $projectRoot 'VoxCrm.Web\bin\Debug\net10.0\VoxCrm.Web.dll') -WorkingDirectory (Join-Path $projectRoot 'VoxCrm.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $projectRoot 'local-web.log') -RedirectStandardError (Join-Path $projectRoot 'local-web-error.log') -PassThru
    Write-Output "VoxCRM yerel PID: $($process.Id) | http://127.0.0.1:5114/Finance"
} finally { Pop-Location }
