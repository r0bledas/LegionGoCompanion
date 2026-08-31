$file = "C:\Users\T450\Utilities\LegionGoCompanion\HandheldCompanion\HandheldCompanion.csproj"
$lines = Get-Content $file
$filtered = $lines | Where-Object {
    $line = $_
    $exclude = $false
    if ($line -match "3DModels\\") { $exclude = $true }
    if ($line -match "Model8BitDoLite2|ModelDS4|ModelDualSense|ModelN64|ModelSteamDeck|ModelToyController|ModelXBOX360|ModelXBOXOne") { $exclude = $true }
    if ($line -match "Devices\\AOKZOE|Devices\\ASUS|Devices\\AYANEO|Devices\\Ayn|Devices\\GPD|Devices\\MSI|Devices\\Minisforum|Devices\\OneXPlayer|Devices\\SuiPlay|Devices\\Valve|Devices\\Zotac") { $exclude = $true }
    if ($line -match "OneXAOKZOE") { $exclude = $true }
    if ($line -match "LegionGoSZ1|LegionGoSZ2|LegionGoTablet2") { $exclude = $true }
    -not $exclude
}

# Clean up empty ItemGroups
$cleaned = @()
$skipNext = $false
for ($i=0; $i -lt $filtered.Count; $i++) {
    if ($filtered[$i].Trim() -eq "<ItemGroup>" -and ($i+1) -lt $filtered.Count -and $filtered[$i+1].Trim() -eq "</ItemGroup>") {
        $skipNext = $true
    } elseif ($skipNext) {
        $skipNext = $false
    } else {
        $cleaned += $filtered[$i]
    }
}

# Second pass for remaining empty ItemGroups that might have had whitespace between tags originally, or just in case
$finalCleaned = @()
$skipNext2 = $false
for ($i=0; $i -lt $cleaned.Count; $i++) {
    if ($cleaned[$i].Trim() -eq "<ItemGroup>" -and ($i+1) -lt $cleaned.Count -and $cleaned[$i+1].Trim() -eq "</ItemGroup>") {
        $skipNext2 = $true
    } elseif ($skipNext2) {
        $skipNext2 = $false
    } else {
        $finalCleaned += $cleaned[$i]
    }
}

$finalCleaned | Set-Content $file -Encoding UTF8
# Verify XML
try {
    [xml]$xml = Get-Content $file
    Write-Output "XML is valid."
} catch {
    Write-Output "XML is invalid: $_"
}
