#Requires -Version 5.1
<#
.SYNOPSIS
    Publica una version del plugin: sube el numero, empaqueta, actualiza el manifiesto y empuja el tag.

.DESCRIPTION
    Sube la version en Directory.Build.props, Properties/AssemblyInfo.cs y build.yaml, compila y
    empaqueta con package.ps1, anota la version en manifest.json, hace el commit, crea el tag v<version>
    y lo empuja. El push del tag dispara .github/workflows/release.yaml, que vuelve a compilar, publica
    la release de GitHub con el .zip y refresca el manifiesto; Jellyfin lee ese manifiesto como catalogo
    de plugins, asi que la version aparece en el panel de control sin tocar nada mas.

.PARAMETER Version
    Version a publicar, por ejemplo 1.1.0.0.

.PARAMETER Changelog
    Texto de la entrada del manifiesto. Si se omite y la version ya existe en manifest.json se conserva
    su changelog; si no existe, se usa "Version <Version>".

.PARAMETER Owner
    Usuario u organizacion de GitHub. Por defecto, el que ya tenga el manifiesto.

.PARAMETER Repository
    Nombre del repositorio. Por defecto, el que ya tenga el manifiesto.

.PARAMETER NoPush
    Hace todo menos el push: deja el commit y el tag en local para revisarlos.

.EXAMPLE
    pwsh ./scripts/Release.ps1 -Version 1.1.0.0 -Changelog "Corrige la numeracion de los informativos."

.EXAMPLE
    # Ensayo: actualiza archivos, compila y empaqueta, pero no empuja nada.
    pwsh ./scripts/Release.ps1 -Version 1.1.0.0 -NoPush
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Changelog,
    [string]$Owner,
    [string]$Repository,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$assemblyPath = Join-Path $root 'Jellyfin.Plugin.Ib3\Properties\AssemblyInfo.cs'
$buildPath = Join-Path $root 'build.yaml'
$manifestPath = Join-Path $root 'manifest.json'

if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "La version '$Version' no tiene la forma de Jellyfin (cuatro numeros, por ejemplo 1.1.0.0)."
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'No hay git en el PATH.'
}

# El repositorio tiene que estar limpio: el commit de la release solo lleva la version y el manifiesto.
$dirty = git -C $root status --porcelain
if ($dirty) {
    throw "Hay cambios sin commitear. Commitealos antes de publicar:`n$($dirty -join "`n")"
}

Write-Host "Actualizando la version a $Version..." -ForegroundColor Cyan

$props = [System.IO.File]::ReadAllText($propsPath)
$props = [regex]::Replace($props, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
$props = [regex]::Replace($props, '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>")
$props = [regex]::Replace($props, '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$Version</FileVersion>")
[System.IO.File]::WriteAllText($propsPath, $props, (New-Object System.Text.UTF8Encoding($false)))

$assembly = [System.IO.File]::ReadAllText($assemblyPath)
$assembly = [regex]::Replace($assembly, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version`")")
$assembly = [regex]::Replace($assembly, 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version`")")
[System.IO.File]::WriteAllText($assemblyPath, $assembly, (New-Object System.Text.UTF8Encoding($false)))

$build = [System.IO.File]::ReadAllText($buildPath)
$build = [regex]::Replace($build, '(?m)^version: "[^"]+"', "version: `"$Version`"")
[System.IO.File]::WriteAllText($buildPath, $build, (New-Object System.Text.UTF8Encoding($false)))

Write-Host 'Empaquetando...' -ForegroundColor Cyan
& (Join-Path $root 'package.ps1') -Configuration Release -Version $Version
if (-not (Test-Path (Join-Path (Join-Path $root 'dist') "ib3_$Version.zip"))) {
    throw 'package.ps1 no ha dejado el paquete en dist.'
}

$manifestArgs = @{ Version = $Version; TargetAbi = '10.11.0.0' }
if ($Changelog) { $manifestArgs['Changelog'] = $Changelog }
if ($Owner) { $manifestArgs['Owner'] = $Owner }
if ($Repository) { $manifestArgs['Repository'] = $Repository }

& (Join-Path $PSScriptRoot 'Update-Manifest.ps1') @manifestArgs | Out-Host

$tag = "v$Version"
git -C $root add --all
git -C $root commit -m "IB3 $Version" | Out-Host

$existingTag = git -C $root tag --list $tag
if ($existingTag) {
    throw "El tag $tag ya existe. Borralo (git tag -d $tag) o publica otra version."
}

git -C $root tag -a $tag -m "IB3 $Version" | Out-Host

if ($NoPush) {
    Write-Host "Sin push (-NoPush): revisa el commit y el tag $tag, y empuja con 'git push --follow-tags'." -ForegroundColor Yellow
    return
}

git -C $root push --follow-tags | Out-Host
Write-Host "Publicado: el tag $tag lanza la release de GitHub y actualiza el manifiesto." -ForegroundColor Green
