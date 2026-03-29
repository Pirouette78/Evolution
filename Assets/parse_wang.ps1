Add-Type -AssemblyName System.Drawing
$url = "https://www.boristhebrave.com/permanent/24/06/cr31/stagecast/art/atlas/blob/wangbl.png"
$file = "C:\unityProjects\Evolution\Assets\wangbl.png"
Invoke-WebRequest -Uri $url -OutFile $file
$img = [System.Drawing.Bitmap]::FromFile($file)
$w = $img.Width
$h = $img.Height
$cols = 7
$rows = 7
$tw = [math]::Floor($w / $cols)
$th = [math]::Floor($h / $rows)

$mappings = @{}

for ($ty=0; $ty -lt $rows; $ty++) {
    for ($tx=0; $tx -lt $cols; $tx++) {
        $cx = $tx * $tw
        $cy = $ty * $th
        
        function IsFg($dx, $dy) {
            if ($dx -lt 0 -or $dy -lt 0 -or $dx -ge $tw -or $dy -ge $th) { return $false }
            $pixel = $img.GetPixel($cx + $dx, $cy + $dy)
            if ($pixel.A -lt 128) { return $false }
            # If the pixel is close to pure white, it's background
            if ($pixel.R -gt 240 -and $pixel.G -gt 240 -and $pixel.B -gt 240) { return $false }
            return $true
        }
        
        $c = IsFg [math]::Floor($tw/2) [math]::Floor($th/2)
        $n = IsFg [math]::Floor($tw/2) 2
        $s = IsFg [math]::Floor($tw/2) ($th-3)
        $e = IsFg ($tw-3) [math]::Floor($th/2)
        $w2 = IsFg 2 [math]::Floor($th/2)
        
        if (-not $c -and -not $n -and -not $s -and -not $e -and -not $w2) {
            continue
        }
        
        $ne = IsFg ($tw-3) 2
        $se = IsFg ($tw-3) ($th-3)
        $sw = IsFg 2 ($th-3)
        $nw = IsFg 2 2
        
        $ortho = 0
        if ($n) { $ortho += 1 }
        if ($e) { $ortho += 2 }
        if ($s) { $ortho += 4 }
        if ($w2){ $ortho += 8 }
        
        $diag = 0
        if ($ne -and $n -and $e) { $diag += 1 }
        if ($se -and $s -and $e) { $diag += 2 }
        if ($sw -and $s -and $w2){ $diag += 4 }
        if ($nw -and $n -and $w2){ $diag += 8 }
        
        $mask = $ortho + ($diag * 16)
        if (-not $mappings.ContainsKey($mask)) {
            $mappings[$mask] = "$tx, $ty"
        }
    }
}

Write-Host ("Found " + $mappings.Count + " valid mappings")

Write-Host "private Vector2Int GetWangBorisPosition(int mask) {"
Write-Host "    int ortho = mask & 15;"
Write-Host "    int diag = mask >> 4;"

$keys = [System.Collections.ArrayList]($mappings.Keys)
$keys.Sort()

foreach ($k in $keys) {
    $ortho = $k -band 15
    $diag = $k -shr 4
    $pos = $mappings[$k]
    Write-Host "    if (ortho == $ortho && diag == $diag) return new Vector2Int($pos);"
}
Write-Host "    return new Vector2Int(0, 0);"
Write-Host "}"
$img.Dispose()
