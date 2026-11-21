[CmdletBinding()]
param(
    [ValidateSet("CompleteDay", "MissingTrade1", "MissingTrade2", "MissingBoth", "Trade1Only", "Trade2Only", "SingleHour")]
    [string]$Scenario = "CompleteDay",
    [string]$OutputDirectory = "C:\Users\Moon\Temp_test",
    [datetime]$Date = (Get-Date),
    [ValidateRange(0,23)][int]$StartHour = 8,
    [ValidateRange(0,23)][int]$EndHour = 23,
    [ValidateRange(1,1440)][int]$IntervalMinutes = 60,
    [ValidateRange(0,23)][int]$ScenarioHour = 18,
    [ValidateSet("HHmm0", "HHmmss")][string]$TimeFormat = "HHmm0",
    [switch]$CleanGeneratedFiles
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($EndHour -lt $StartHour) { throw "EndHour doit être supérieur ou égal à StartHour." }
if ($Scenario -in @("MissingTrade1", "MissingTrade2", "MissingBoth", "SingleHour") -and ($ScenarioHour -lt $StartHour -or $ScenarioHour -gt $EndHour)) { throw "ScenarioHour doit être compris entre StartHour et EndHour." }
if (-not (Test-Path -LiteralPath $OutputDirectory)) { New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null }
$dateToken = $Date.ToString("yyyyMMdd")
$hours = [System.Collections.Generic.List[TimeSpan]]::new()
for ($minuteOfDay = ($StartHour * 60); $minuteOfDay -le ($EndHour * 60); $minuteOfDay += $IntervalMinutes) { $hours.Add([TimeSpan]::FromMinutes($minuteOfDay)) }
if ($Scenario -eq "SingleHour") { $hours.Clear(); $hours.Add([TimeSpan]::FromHours($ScenarioHour)) }
if ($CleanGeneratedFiles) {
    $pattern = "^(Trade1|Trade2)_" + $dateToken + "_.*\.csv$"
    Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -match $pattern } | Remove-Item -Force
}
$created = [System.Collections.Generic.List[string]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
foreach ($time in $hours) {
    $hourValue = [int]$time.TotalHours
    if ($TimeFormat -eq "HHmmss") { $timeToken = $time.ToString("hhmmss") }
    else { $timeToken = $time.ToString("hhmm") + "0" }
    foreach ($trade in @("Trade1", "Trade2")) {
        $omit = switch ($Scenario) {
            "MissingTrade1" { ($trade -eq "Trade1" -and $hourValue -eq $ScenarioHour) }
            "MissingTrade2" { ($trade -eq "Trade2" -and $hourValue -eq $ScenarioHour) }
            "MissingBoth" { ($hourValue -eq $ScenarioHour) }
            "Trade1Only" { ($trade -eq "Trade2") }
            "Trade2Only" { ($trade -eq "Trade1") }
            default { $false }
        }
        if ($omit) { $skipped.Add($trade + "_" + $dateToken + "_" + $timeToken + ".csv"); continue }
        $fileName = $trade + "_" + $dateToken + "_" + $timeToken + ".csv"
        $fullPath = Join-Path $OutputDirectory $fileName
        $content = @(
            "FileType,TradeDate,ExpectedTime,GeneratedAt,Scenario"
            ($trade + "," + $dateToken + "," + $time.ToString("hh\:mm") + "," + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss") + "," + $Scenario)
            ("TEST,100,200," + $trade + ",FileFlowMonitor")
        ) -join [Environment]::NewLine
        Set-Content -LiteralPath $fullPath -Value $content -Encoding UTF8
        $created.Add($fullPath)
    }
}
Write-Host ""
Write-Host "FileFlowMonitor - Génération des fichiers de test" -ForegroundColor Cyan
Write-Host ("Scénario       : " + $Scenario)
Write-Host ("Date           : " + $dateToken)
Write-Host ("Dossier        : " + $OutputDirectory)
Write-Host ("Plage          : " + $StartHour + ":00 -> " + $EndHour + ":00")
Write-Host ("Intervalle     : " + $IntervalMinutes + " minute(s)")
Write-Host ("Format horaire : " + $TimeFormat)
Write-Host ""
Write-Host ("Fichiers créés : " + $created.Count) -ForegroundColor Green
$created | ForEach-Object { Write-Host ("  + " + (Split-Path $_ -Leaf)) }
if ($skipped.Count -gt 0) {
    Write-Host ""
    Write-Host ("Fichiers volontairement absents : " + $skipped.Count) -ForegroundColor Yellow
    $skipped | ForEach-Object { Write-Host ("  - " + $_) }
}
Write-Host ""
Write-Host "Les fichiers sont prêts à être copiés dans le dossier surveillé." -ForegroundColor Cyan
