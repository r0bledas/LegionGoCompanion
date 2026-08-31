$file = "C:\Users\T450\Utilities\LegionGoCompanion\HandheldCompanion\HandheldCompanion.csproj"
[xml]$xml = Get-Content $file -Raw

$nodesToRemove = @()
foreach ($itemGroup in $xml.Project.ItemGroup) {
    foreach ($child in $itemGroup.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        
        $path = ""
        if ($child.HasAttribute("Update")) {
            $path = $child.GetAttribute("Update")
        } elseif ($child.HasAttribute("Include")) {
            $path = $child.GetAttribute("Include")
        } elseif ($child.HasAttribute("Remove")) {
            $path = $child.GetAttribute("Remove")
        }
        
        if ($path) {
            $match = $false
            if ($path -match "3DModels\\") { $match = $true }
            if ($path -match "^Models\\") { $match = $true } 
            
            if ($path -match "Model8BitDoLite2|ModelDS4|ModelDualSense|ModelN64|ModelSteamDeck|ModelToyController|ModelXBOX360|ModelXBOXOne") { $match = $true }
            
            if ($path -match "Devices\\AOKZOE|Devices\\ASUS|Devices\\AYANEO|Devices\\Ayn|Devices\\GPD|Devices\\MSI|Devices\\Minisforum|Devices\\OneXPlayer|Devices\\SuiPlay|Devices\\Valve|Devices\\Zotac") { $match = $true }
            
            if ($path -match "OneXAOKZOE") { $match = $true }
            if ($path -match "LegionGoSZ1|LegionGoSZ2|LegionGoTablet2") { $match = $true }
            
            if ($match) {
                $nodesToRemove += $child
            }
        }
    }
}

Write-Output "Removing $($nodesToRemove.Count) nodes"

foreach ($node in $nodesToRemove) {
    if ($node.ParentNode) {
        $node.ParentNode.RemoveChild($node) | Out-Null
    }
}

$emptyGroups = @()
foreach ($itemGroup in $xml.Project.ItemGroup) {
    $hasElement = $false
    foreach ($child in $itemGroup.ChildNodes) {
        if ($child.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $hasElement = $true
        }
    }
    if (-not $hasElement) {
        $emptyGroups += $itemGroup
    }
}

Write-Output "Removing $($emptyGroups.Count) empty ItemGroups"

foreach ($group in $emptyGroups) {
    if ($group.ParentNode) {
        $group.ParentNode.RemoveChild($group) | Out-Null
    }
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($file, $settings)
$xml.Save($writer)
$writer.Close()
