#Requires -RunAsAdministrator
param(
    [string]$Root = 'C:\PublishToolTest'
)

$appcmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'

foreach ($name in 'PublishToolTest.CoreWeb', 'PublishToolTest.FrameworkWeb') {
    if (& $appcmd list site "/name:$name") {
        & $appcmd delete site $name | Out-Null
    }
    if (& $appcmd list apppool "/name:$name") {
        & $appcmd delete apppool $name | Out-Null
    }
}

foreach ($name in 'PublishToolTest.CoreService', 'PublishToolTest.FrameworkService') {
    if (Get-Service $name -ErrorAction SilentlyContinue) {
        Stop-Service $name -ErrorAction SilentlyContinue
        sc.exe delete $name | Out-Null
    }
}

Write-Host "IIS sites and services removed. Delete $Root and C:\ProgramData\PublishTool\server.json by hand if you no longer need them."
