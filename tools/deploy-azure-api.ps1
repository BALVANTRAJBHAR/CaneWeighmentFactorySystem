[CmdletBinding()]
param(
    [string]$ResourceGroup = 'AuthenticFoodFarm',
    [string]$AppName = 'authenticfoodfarmApi',
    [string]$PackagePath = '',
    [string]$HealthPath = '/api/health',
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'

$azCandidates = @(
    (Get-Command az -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    'C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin\az.cmd',
    'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$az = $azCandidates | Select-Object -First 1
if (!$az) { throw 'Azure CLI was not found. Install it and run az login first.' }

& $az account show --only-show-errors | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Azure CLI is not logged in. Run az login first.' }

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $publishDirectory = Join-Path $projectRoot "artifacts\azure-api-$stamp"
    $PackagePath = "$publishDirectory.zip"
    & (Join-Path $PSScriptRoot 'build-api-publish.ps1') `
        -AzureAppService `
        -OutputDirectory $publishDirectory `
        -ZipPath $PackagePath
    if ($LASTEXITCODE -ne 0) { throw 'Azure API package build failed.' }
}
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path

$subscriptionId = (& $az account show --query id -o tsv --only-show-errors).Trim()
$siteId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$AppName"
$scmPolicyId = "$siteId/basicPublishingCredentialsPolicies/scm"
$originalScmPolicy = (& $az resource show --ids $scmPolicyId --api-version 2022-03-01 --query properties.allow -o tsv --only-show-errors).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not read the App Service SCM publishing policy.' }

try {
    if ($originalScmPolicy -ne 'true') {
        Write-Host 'Temporarily enabling SCM publishing credentials for ZIP deployment...'
        & $az resource update --ids $scmPolicyId --api-version 2022-03-01 `
            --set properties.allow=true --only-show-errors | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not enable SCM publishing credentials.' }
        Start-Sleep -Seconds 3
    }

    # Authenticate only inside this process; neither publishing credentials nor
    # the Authorization header are written to output or disk.
    $profilesJson = & $az webapp deployment list-publishing-profiles `
        -g $ResourceGroup -n $AppName -o json --only-show-errors
    $profile = $profilesJson | ConvertFrom-Json |
        Where-Object { $_.publishMethod -eq 'MSDeploy' } |
        Select-Object -First 1
    if (!$profile) { throw 'App Service MSDeploy publishing profile was not found.' }
    $basicToken = [Convert]::ToBase64String(
        [Text.Encoding]::ASCII.GetBytes("$($profile.userName):$($profile.userPWD)"))
    $kuduHeaders = @{ Authorization = "Basic $basicToken" }
    $publishHost = ([Uri]("https://" + $profile.publishUrl)).Host
    $kuduBase = "https://$publishHost"
    $previousDeploymentId = $null
    try {
        $previousDeployment = Invoke-RestMethod -Uri "$kuduBase/api/deployments/latest" `
            -Headers $kuduHeaders -TimeoutSec 30
        $previousDeploymentId = $previousDeployment.id
    }
    catch {
        # A new/empty App Service has no prior deployment, which is valid.
    }

    & $az webapp config set -g $ResourceGroup -n $AppName `
        --use-32bit-worker-process false --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not select the 64-bit App Service worker.' }

    & $az webapp config appsettings set -g $ResourceGroup -n $AppName `
        --settings WEBSITE_RUN_FROM_PACKAGE=1 SCM_DO_BUILD_DURING_DEPLOYMENT=false `
        --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure App Service package execution.' }

    Write-Host "Deploying $PackagePath to $AppName..."
    & $az webapp deploy -g $ResourceGroup -n $AppName `
        --src-path $PackagePath --type zip --clean true --restart true --async true `
        --timeout ($TimeoutSeconds * 1000) --only-show-errors | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Azure accepted neither the ZIP upload nor the deployment request.' }

    # --async returns as soon as upload starts. Wait for Kudu to activate a new
    # deployment instead of accidentally accepting the previous healthy app.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $deploymentActivated = $false
    do {
        try {
            $deployment = Invoke-RestMethod -Uri "$kuduBase/api/deployments/latest" `
                -Headers $kuduHeaders -TimeoutSec 30
            if ($deployment.id -ne $previousDeploymentId -and $deployment.complete) {
                if ([int]$deployment.status -ne 4) {
                    throw "App Service deployment failed: $($deployment.message)"
                }
                $deploymentActivated = $true
                break
            }
        }
        catch {
            if ($_.Exception.Message -like 'App Service deployment failed:*') { throw }
        }
        Write-Host 'Waiting for App Service to activate the uploaded package...'
        Start-Sleep -Seconds 8
    } while ((Get-Date) -lt $deadline)
    if (!$deploymentActivated) {
        throw 'Timed out waiting for App Service to activate a new deployment.'
    }

    $hostName = (& $az webapp show -g $ResourceGroup -n $AppName --query defaultHostName -o tsv --only-show-errors).Trim()
    if (!$hostName) { throw 'Could not resolve the App Service host name.' }
    $healthUri = "https://$hostName$HealthPath"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = 'No health response received.'
    do {
        try {
            $response = Invoke-WebRequest -Uri $healthUri -UseBasicParsing -TimeoutSec 45
            if ($response.StatusCode -eq 200) {
                Write-Host "Deployment verified: $healthUri"
                Write-Host $response.Content
                return
            }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $lastError = if ($statusCode) { "HTTP $statusCode" } else { $_.Exception.Message }
        }
        Write-Host "Waiting for App Service startup ($lastError)..."
        Start-Sleep -Seconds 8
    } while ((Get-Date) -lt $deadline)

    throw "Deployment was uploaded but health verification timed out: $lastError"
}
finally {
    if ($originalScmPolicy -ne 'true') {
        Write-Host 'Restoring SCM publishing credentials to disabled...'
        & $az resource update --ids $scmPolicyId --api-version 2022-03-01 `
            --set properties.allow=false --only-show-errors | Out-Null
    }
}
