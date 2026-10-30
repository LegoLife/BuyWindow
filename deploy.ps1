#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes BuyWindow here for ARM64 and runs it in Docker on the home server, using SSH keys.
    The Pi is short on memory for compiling, so it only builds an image from the published files.
    Uploads the local .env. Use -Rollback to return to the previous image.
#>
[CmdletBinding()]
param(
    [string] $Server = 'jake@192.168.1.137',
    [string] $RemotePath = '~/buywindow',
    [string] $Url = 'http://192.168.1.137:8085',
    [switch] $Rollback
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Ok([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)." }
}

function Invoke-Remote([string] $Command, [string] $Step) {
    ssh $Server $Command
    Assert-Ok $Step
}

Set-Location $PSScriptRoot

if ($Rollback) {
    Invoke-Remote "docker tag buywindow:prev buywindow:latest && cd $RemotePath && docker compose -f docker-compose.prod.yml up -d --no-build" 'Rollback'
}
else {
    if (-not (Test-Path .env)) { throw 'No .env file. Copy .env.example to .env and fill it in.' }

    dotnet publish src/BuyWindow.Web/BuyWindow.Web.csproj -c Release -r linux-arm64 --self-contained false -o publish
    Assert-Ok 'Publish'

    $archive = Join-Path ([IO.Path]::GetTempPath()) "buywindow-$([guid]::NewGuid()).tar.gz"
    try {
        # Git's tar can come first on PATH, and it reads the C: in the archive path as a remote host.
        & "$env:SystemRoot\System32\tar.exe" -czf $archive publish Dockerfile.prebuilt docker-compose.prod.yml
        Assert-Ok 'Packaging'

        Invoke-Remote "mkdir -p $RemotePath && rm -rf $RemotePath/publish" 'Preparing the server'
        scp -q $archive "${Server}:$RemotePath/payload.tar.gz"
        Assert-Ok 'Upload'
        scp -q .env "${Server}:$RemotePath/.env"
        Assert-Ok 'Uploading .env'

        # Windows editors can save CRLF, which would end up inside the values.
        Invoke-Remote "cd $RemotePath && tar xzf payload.tar.gz && rm payload.tar.gz && chmod 600 .env && sed -i 's/\r$//' .env && (docker image inspect buywindow:latest >/dev/null 2>&1 && docker tag buywindow:latest buywindow:prev || true) && docker compose -f docker-compose.prod.yml up -d --build" 'Starting the container'
    }
    finally {
        if (Test-Path $archive) { Remove-Item $archive -Force }
    }
}

foreach ($attempt in 1..15) {
    try {
        if ((Invoke-WebRequest $Url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) {
            Write-Host "BuyWindow is running at $Url" -ForegroundColor Green
            exit 0
        }
    }
    catch { Start-Sleep -Seconds 2 }
}
Invoke-Remote "cd $RemotePath && docker compose -f docker-compose.prod.yml logs --tail 30" 'Reading logs'
throw "BuyWindow didn't respond at $Url."
