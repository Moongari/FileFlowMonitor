[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$FileName,
    [Parameter(Mandatory = $true)][string]$WatchDirectory,
    [string]$SourceDirectory = "C:\Users\Moon\Temp_test",
    [ValidateRange(0,86400)][int]$DelaySeconds = 0,
    [switch]$Overwrite
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$source = Join-Path $SourceDirectory $FileName
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ("Fichier source introuvable : " + $source) }
if (-not (Test-Path -LiteralPath $WatchDirectory -PathType Container)) { throw ("Dossier surveillé introuvable : " + $WatchDirectory) }
$destination = Join-Path $WatchDirectory $FileName
if ((Test-Path -LiteralPath $destination) -and -not $Overwrite) { throw "Le fichier existe déjà dans le dossier surveillé. Utilisez -Overwrite pour le remplacer." }
if ($DelaySeconds -gt 0) {
    Write-Host ("Attente de " + $DelaySeconds + " seconde(s) avant l'envoi...") -ForegroundColor Yellow
    Start-Sleep -Seconds $DelaySeconds
}
Copy-Item -LiteralPath $source -Destination $destination -Force:$Overwrite
Write-Host ("Fichier déposé : " + $destination) -ForegroundColor Green
