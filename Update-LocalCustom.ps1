param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [System.Text.UTF8Encoding]::new()
$env:GIT_MERGE_AUTOEDIT = 'no'

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'FufuLauncher\FufuLauncher.csproj'
$executablePath = Join-Path $repoRoot 'FufuLauncher\bin\x64\Debug\net8.0-windows10.0.26100.0\FufuLauncher.exe'

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')"
    }
}

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

Set-Location -LiteralPath $repoRoot

try {
    Write-Step 'Checking repository'

    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git was not found.'
    }

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK was not found.'
    }

    $pendingChanges = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read the Git working tree.'
    }
    if ($pendingChanges.Count -gt 0) {
        throw 'There are uncommitted local changes. Commit them before updating.'
    }

    Write-Step 'Stopping the locally built FufuLauncher'
    Get-Process -Name 'FufuLauncher' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $_.Path -and $_.Path.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        } |
        Stop-Process -Force

    Write-Step 'Downloading updates from the official repository'
    Invoke-Git -Arguments @('fetch', 'upstream')

    Write-Step 'Updating the clean master branch'
    Invoke-Git -Arguments @('switch', 'master')
    Invoke-Git -Arguments @('pull', '--ff-only', 'origin', 'master')
    Invoke-Git -Arguments @('merge', '--ff-only', 'upstream/master')
    Invoke-Git -Arguments @('push', 'origin', 'master')

    Write-Step 'Merging the official update into local-custom'
    Invoke-Git -Arguments @('switch', 'local-custom')
    Invoke-Git -Arguments @('pull', '--ff-only', 'origin', 'local-custom')

    & git merge --no-edit master
    if ($LASTEXITCODE -ne 0) {
        & git merge --abort
        throw 'The official update conflicts with your customization. The merge was cancelled safely; send the conflict output to Codex.'
    }

    Write-Step 'Building the customized x64 Debug version'
    & dotnet build $projectPath -c Debug '-p:Platform=x64' '-p:WarningLevel=0' --no-restore --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed.'
    }

    Write-Step 'Uploading the updated customization to your Fork'
    Invoke-Git -Arguments @('push', 'origin', 'local-custom')

    if (-not $NoLaunch) {
        if (-not (Test-Path -LiteralPath $executablePath)) {
            throw "The built executable was not found: $executablePath"
        }

        Write-Step 'Starting FufuLauncher'
        Start-Process -FilePath $executablePath
    }

    Write-Host "`nFufuLauncher custom version is up to date." -ForegroundColor Green
}
catch {
    Write-Host "`nUpdate stopped: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
