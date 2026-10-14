<#
.SYNOPSIS
Verify publication UI with synthetic data in an isolated user-data directory on the local desktop.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ArtifactDirectory = (Join-Path $env:TEMP ('re-ui-publication-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath($RepositoryRoot)
$artifacts = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$data = Join-Path $artifacts 'user-data'
$originalRoot = $env:RE_ECOSYSTEM_USER_DATA_ROOT
$originalWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:RE_ECOSYSTEM_USER_DATA_ROOT = $data
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appProcessId = 0
$results = [Collections.Generic.List[object]]::new()
$template = Join-Path $repo 'RE.WinUI/AgentConfigs/Global_Settings.json'
$originalHash = (Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash
function Checked([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments[0]) (exit $LASTEXITCODE)" }
    $output
}
function Ui([string[]]$Arguments) { Checked (@('ui') + $Arguments + @('-a', [string]$appProcessId)) }
function Start-App {
    $output = Checked @('run',(Join-Path $repo 'RE.WinUI/RE.WinUI.csproj'),'--arch','x64','--detach','--json')
    $text = $output -join "`n"
    $match = [regex]::Match($text, '(?s)\{\s*"ProcessId"\s*:\s*(\d+)\s*\}')
    if (-not $match.Success) { throw 'No application PID returned.' }
    $script:appProcessId = [int]$match.Groups[1].Value
    Ui @('wait-for','ChatInputBox','-t','10000','--json') | Out-Null
}
function Stop-App {
    if ($appProcessId -gt 0) {
        # Only the PID created by this script is stopped; isolated data is already persisted.
        Stop-Process -Id $appProcessId -ErrorAction SilentlyContinue
        $script:appProcessId = 0
    }
}
function Open-AI {
    Ui @('invoke','SettingsItem','--json') | Out-Null
    Ui @('wait-for','NavAICard','-t','5000','--json') | Out-Null
    Ui @('focus','SettingsItem','--json') | Out-Null
    try { Ui @('click','NavAICard','--json') | Out-Null }
    catch { Ui @('click','NavAICard','--json') | Out-Null } # One retry after the navigation transition.
    Ui @('wait-for','OllamaPortTextBox','-t','5000','--json') | Out-Null
}
function Assert-True([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Inspect-Nodes($Value) {
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string] -and $Value -isnot [pscustomobject]) {
        foreach ($v in $Value) { Inspect-Nodes $v }
    } elseif ($Value -is [pscustomobject]) {
        $Value
        foreach ($p in $Value.PSObject.Properties) {
            if ($p.Value -is [pscustomobject] -or $p.Value -is [array]) { Inspect-Nodes $p.Value }
        }
    }
}
try {
    Start-App
    Ui @('invoke','ChatNewConversationBtn','--json') | Out-Null
    Ui @('set-value','ChatInputBox','/md5 C:\Windows\notepad.exe','--json') | Out-Null
    Ui @('invoke','ChatSendButton','--json') | Out-Null
    $matched = $false
    for ($attempt=0; $attempt -lt 12; $attempt++) {
        $tree = (Ui @('inspect','ChatListView','--depth','12','--json')) -join "`n" | ConvertFrom-Json
        if (@(Inspect-Nodes $tree | Where-Object { $_.name -eq 'md5: notepad.exe' }).Count) { $matched=$true; break }
        Start-Sleep -Milliseconds 250
    }
    Assert-True $matched 'Updated chat title was not rendered.'
    $results.Add(@{name='Dynamic chat title';status='PASS'})
    Open-AI
    Ui @('set-value','OllamaPortTextBox','11435','--json') | Out-Null
    $settingsPath = Join-Path $data 'Settings/Global_Settings.json'
    $saved = $false
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        if ((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).ollama_port -eq '11435') { $saved=$true; break }
        Start-Sleep -Milliseconds 100
    }
    Assert-True $saved 'Preference did not persist.'
    Stop-App; Start-App; Open-AI
    $port = (Ui @('get-value','OllamaPortTextBox','--json')) -join "`n" | ConvertFrom-Json
    Assert-True ($port.text -eq '11435') 'Preference was lost after restart.'
    $results.Add(@{name='Preferences survive restart';status='PASS'})
    Ui @('invoke','AddApiKeyButton','--json') | Out-Null
    Ui @('wait-for','ApiKeySecretInput','-t','5000','--json') | Out-Null
    Ui @('set-value','ApiKeySecretInput','synthetic-ui-credential-for-publication','--json') | Out-Null
    Ui @('invoke','PrimaryButton','--json') | Out-Null
    $settings = $null
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        if ($settings.api_keys.Count -eq 1) { break }
        Start-Sleep -Milliseconds 100
    }
    Assert-True ($settings.api_keys.Count -eq 1) 'API key metadata missing.'
    Assert-True (-not (Get-Content -LiteralPath $settingsPath -Raw).Contains('synthetic-ui-credential-for-publication')) 'Credential appeared in JSON.'
    $secretPath = Join-Path $data ("Settings/Secrets/$($settings.api_keys[0].secret_reference).bin")
    Assert-True (Test-Path -LiteralPath $secretPath) 'DPAPI ciphertext missing.'
    $results.Add(@{name='API key stored outside JSON';status='PASS'})
    Ui @('invoke',("DeleteApiKey_"+$settings.api_keys[0].id),'--json') | Out-Null
    Ui @('wait-for','PrimaryButton','-t','5000','--json') | Out-Null
    Ui @('invoke','PrimaryButton','--json') | Out-Null
    $deleted = $false
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        if ((Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).api_keys.Count -eq 0 -and -not (Test-Path -LiteralPath $secretPath)) { $deleted=$true; break }
        Start-Sleep -Milliseconds 100
    }
    Assert-True $deleted 'Credential metadata/ciphertext not removed.'
    Assert-True ((Get-FileHash -LiteralPath $template -Algorithm SHA256).Hash -eq $originalHash) 'Source template changed during UI operations.'
    $results.Add(@{name='API key deletion and unchanged source';status='PASS'})
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'results.json')
    Write-Host 'Publication UI checks passed using isolated synthetic data.'
} finally {
    Stop-App
    # The CLI releases its idle workflow lease automatically after the final UI command.
    $env:RE_ECOSYSTEM_USER_DATA_ROOT=$originalRoot
    $env:WINAPP_UI_WORKFLOW_ID=$originalWorkflow
}
