#Requires -RunAsAdministrator
param(
    [string]$Root = 'C:\PublishToolTest',
    [int]$CoreWebPort = 8081,
    [int]$FrameworkWebPort = 8082
)

$ErrorActionPreference = 'Stop'
$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'
if (-not (Test-Path $appcmd)) {
    throw 'IIS is not installed (appcmd.exe not found).'
}

$targets = @{
    CoreWebApp          = Join-Path $Root 'targets\CoreWebApp'
    FrameworkWebApp     = Join-Path $Root 'targets\FrameworkWebApp'
    CoreServiceApp      = Join-Path $Root 'targets\CoreServiceApp'
    FrameworkServiceApp = Join-Path $Root 'targets\FrameworkServiceApp'
}
foreach ($path in $targets.Values) {
    New-Item -ItemType Directory -Force $path | Out-Null
}

function New-TestSite([string]$Name, [string]$Path, [int]$Port, [string]$Runtime) {
    if (-not (& $appcmd list apppool "/name:$Name")) {
        & $appcmd add apppool "/name:$Name" "/managedRuntimeVersion:$Runtime" | Out-Null
    }
    if (-not (& $appcmd list site "/name:$Name")) {
        & $appcmd add site "/name:$Name" "/physicalPath:$Path" "/bindings:http/*:${Port}:" | Out-Null
        & $appcmd set app "$Name/" "/applicationPool:$Name" | Out-Null
    }
    Write-Host "IIS site $Name -> $Path (http://localhost:$Port)"
}

New-TestSite 'PublishToolTest.CoreWeb' $targets.CoreWebApp $CoreWebPort ''
New-TestSite 'PublishToolTest.FrameworkWeb' $targets.FrameworkWebApp $FrameworkWebPort 'v4.0'

function New-TestService([string]$Name, [string]$Exe) {
    if (-not (Get-Service $Name -ErrorAction SilentlyContinue)) {
        New-Service -Name $Name -BinaryPathName "`"$Exe`"" -StartupType Manual | Out-Null
    }
    Write-Host "Service $Name -> $Exe"
}

New-TestService 'PublishToolTest.CoreService' (Join-Path $targets.CoreServiceApp 'CoreServiceApp.exe')
New-TestService 'PublishToolTest.FrameworkService' (Join-Path $targets.FrameworkServiceApp 'FrameworkServiceApp.exe')

Write-Host ''
Write-Host 'Target directories for the deploy wizard:'
$targets.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name): $($_.Value)" }
Write-Host 'Service names: PublishToolTest.CoreService (CoreServiceApp), PublishToolTest.FrameworkService (FrameworkServiceApp)'
