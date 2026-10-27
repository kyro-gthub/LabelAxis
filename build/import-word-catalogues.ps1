<#
    Makes the built-in sheet catalogues of other label makers (templates\sheets\<brand>.json)
    from Microsoft Word's own label database (Office16\PAGESIZE\PGLBL*.XML), the measurements
    each maker supplies to Microsoft for Mailings > Labels. Every number (label size, labels
    across and down, gaps, the first label's position, corner radius, shape) is read from
    Word's files and converted to millimetres; nothing is estimated.

    Only sheets laid out as one regular grid are taken; layouts Word places label by label
    (a few shaped stickers and media sets) cannot be drawn as a grid and are left out, as are
    business cards, greeting cards, posters and binder tabs. Formtec has its own script,
    build\import-word-labels.ps1. Each brand's catalogue is written again as a whole.

    Usage:  powershell -ExecutionPolicy Bypass -File build\import-word-catalogues.ps1
#>
param(
    [string]$PageSizeDir = 'C:\Program Files\Microsoft Office\root\Office16\PAGESIZE',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'templates\sheets')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $PageSizeDir)) { throw "Word's label folder was not found: $PageSizeDir" }

# Word's file, the brand name shown in LabelAxis, and the prefix Word puts before the code.
$brands = @(
    @{ File = 'PGLBL048.XML'; Brand = 'Avery'; Folder = 'avery'; Prefix = '' }                  # Avery A4/A5
    @{ File = 'PGLBL081.XML'; Brand = 'Avery'; Folder = 'avery'; Prefix = '' }                  # Avery US Letter
    @{ File = 'PGLBL090.XML'; Brand = 'Avery Zweckform'; Folder = 'avery-zweckform'; Prefix = '' }
    @{ File = 'PGLBL012.XML'; Brand = 'HERMA'; Folder = 'herma'; Prefix = 'HERMA' }
    @{ File = 'PGLBL002.XML'; Brand = 'APLI'; Folder = 'apli'; Prefix = 'APLI' }
    @{ File = 'PGLBL110.XML'; Brand = 'Agipa'; Folder = 'agipa'; Prefix = 'AGIPA' }
    @{ File = 'PGLBL111.XML'; Brand = 'Decadry'; Folder = 'decadry'; Prefix = '' }
    @{ File = 'PGLBL103.XML'; Brand = 'Sigel'; Folder = 'sigel'; Prefix = 'Sigel' }
    @{ File = 'PGLBL107.XML'; Brand = 'OnlineLabels'; Folder = 'onlinelabels'; Prefix = '' }
    @{ File = 'PGLBL044.XML'; Brand = 'TANEX'; Folder = 'tanex'; Prefix = 'TANEX' }
    @{ File = 'PGLBL104.XML'; Brand = '3M'; Folder = '3m'; Prefix = '3M' }                      # North America
    @{ File = 'PGLBL112.XML'; Brand = '3M'; Folder = '3m'; Prefix = '3M' }                      # Europe
    @{ File = 'PGLBL115.XML'; Brand = '3M'; Folder = '3m'; Prefix = '3M' }                      # Asia Pacific
    @{ File = 'PGLBL117.XML'; Brand = 'Brother'; Folder = 'brother'; Prefix = '' }
    @{ File = 'PGLBL083.XML'; Brand = 'DYMO'; Folder = 'dymo'; Prefix = '' }
)

$inv = [Globalization.CultureInfo]::InvariantCulture
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Mm([string]$value, [string]$units) {
    if (-not $value) { return 0.0 }
    $v = [double]::Parse($value, $inv)
    switch ($units) {
        'in'  { return [Math]::Round($v * 25.4, 3) }
        'mm'  { return [Math]::Round($v, 3) }
        'emu' { return [Math]::Round($v / 36000.0, 3) }
        default { return [Math]::Round($v * 10, 3) }       # cm
    }
}
function Num([double]$v) { $v.ToString('0.###', $inv) }
function Json([string]$s) { ($s -replace '\\', '\\' -replace '"', '\"' -replace "[`r`n`t]+", ' ').Trim() }
function Slug([string]$s) { ($s.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-') }

# The English text of a name or description, else the one for all languages.
function English($nodes) {
    foreach ($locale in 'en', 'en-us', 'en-gb', 'allOthers', 'allOther', 'all') {
        foreach ($n in $nodes) { if ($n.locale -eq $locale -and $n.InnerText.Trim()) { return $n.InnerText.Trim() } }
    }
    foreach ($n in $nodes) { if ($n.InnerText.Trim()) { return $n.InnerText.Trim() } }
    return ''
}

$groups = @{ mailingLabel = 'Address'; mediaLabel = 'CD/DVD & media'; otherLabel = 'General' }
$written = @{}
$skipped = @{}
$byFolder = @{}

foreach ($b in $brands) {
    $path = Join-Path $PageSizeDir $b.File
    [xml]$xml = [IO.File]::ReadAllText($path)
    foreach ($p in $xml.SelectNodes('//*[local-name()="product"]')) {
        $name = English $p.SelectNodes('.//*[local-name()="locName"]')
        $category = $groups[[string]$p.layoutGroup]
        if (-not $category) { $skipped["$($b.Brand): not a label ($($p.layoutGroup))"]++; continue }

        $panel = $p.SelectSingleNode('.//*[local-name()="masterPanel"]')
        $sheet = $p.SelectSingleNode('.//*[local-name()="sheet"]')
        $grid = $p.SelectSingleNode('.//*[local-name()="sheetGrid"]')
        $units = $p.units
        $w = Mm $panel.width $units; $h = Mm $panel.height $units

        if ($sheet -and $grid) {
            $pw = Mm $sheet.width $units; $ph = Mm $sheet.height $units
            $across = [int]$grid.numAcross; $down = [int]$grid.numDown
            $hgap = Mm $grid.horizGap $units; $vgap = Mm $grid.vertGap $units
            $x = Mm $grid.posX $units; $y = Mm $grid.posY $units
            $margin = 4.0
        } elseif (-not $sheet) {
            # A single label on a roll (DYMO): the page is the label.
            $pw = $w; $ph = $h; $across = 1; $down = 1; $hgap = 0; $vgap = 0; $x = 0; $y = 0; $margin = 0.0
        } else {
            $skipped["$($b.Brand): labels placed one by one, not a grid"]++; continue
        }

        if ($across -lt 1 -or $down -lt 1 -or $w -le 0 -or $h -le 0 -or $pw -le 0 -or $ph -le 0) {
            $skipped["$($b.Brand): incomplete measurements"]++; continue
        }
        if ($pw -lt 60 -or $ph -lt 60) { $margin = 0.0 }

        $right = $x + ($across * $w) + (($across - 1) * $hgap)
        $bottom = $y + ($down * $h) + (($down - 1) * $vgap)
        if ($right -gt $pw + 0.5 -or $bottom -gt $ph + 0.5) {
            $skipped["$($b.Brand): labels run off the sheet"]++; continue
        }

        # Shape: Word's cutout or panel shape, and the corner radius it gives.
        $cutout = $p.SelectSingleNode('.//*[local-name()="cutout"]')
        $ellipse = ($cutout -and $cutout.shape -eq 'ellipse') -or $panel.shape -eq 'ellipse'
        $radius = Mm $panel.cornerRadius $units
        $radius = [Math]::Min($radius, [Math]::Min($w, $h) / 2)
        $shape = if ($ellipse) { 'Ellipse' } elseif ($radius -gt 0.05) { 'RoundRectangle' } else { 'Rectangle' }
        if ($shape -ne 'RoundRectangle') { $radius = 0.0 }

        # Code and description from Word's name: "HERMA 4103 - Film labels A4", "30252, 30320 Address Label".
        $rest = $name
        $note = ''
        if ($rest -match '^Post-it\xAE?\s+(.*)$') { $rest = $Matches[1]; $note = 'Post-it' + [char]0xAE }
        if ($b.Prefix -and $rest.StartsWith($b.Prefix + ' ', [StringComparison]::OrdinalIgnoreCase)) { $rest = $rest.Substring($b.Prefix.Length).Trim() }
        $tokens = @($rest -split '\s+' | Where-Object { $_ })
        $code = $tokens[0]; $i = 1
        while ($code.EndsWith(',') -and $i -lt $tokens.Count) { $code = $code.TrimEnd(',') + ' / ' + $tokens[$i]; $i++ }
        # "Tab 5615MW2", "OML 102", "Tab1 107.23": a short word, then the number that tells them apart.
        if ($i -eq 1 -and $tokens.Count -gt 1 -and $tokens[1] -match '^\d' -and ($code -notmatch '\d' -or $code.Length -le 4)) { $code = "$code $($tokens[1])"; $i = 2 }
        $code = $code.TrimEnd(',')
        $tail = (($tokens | Select-Object -Skip $i) -join ' ').Trim().TrimStart('-', [char]0x2013).Trim()
        $desc = English $p.SelectNodes('.//*[local-name()="locDesc"]')
        if (-not $desc) { $desc = $tail }
        $desc = ($desc -replace '\s+', ' ').Trim()
        if ($note) { $desc = if ($desc) { "$note $([char]0xB7) $desc" } else { $note } }

        $pageName = if ([Math]::Abs($pw - 210) -lt 1 -and [Math]::Abs($ph - 297) -lt 1) { 'A4' }
                    elseif ([Math]::Abs($pw - 148) -lt 1 -and [Math]::Abs($ph - 210) -lt 1) { 'A5' }
                    elseif ([Math]::Abs($pw - 215.9) -lt 1 -and [Math]::Abs($ph - 279.4) -lt 1) { 'Letter' }
                    elseif ([Math]::Abs($pw - 215.9) -lt 1 -and [Math]::Abs($ph - 355.6) -lt 1) { 'Legal' }
                    elseif ([Math]::Abs($pw - 105) -lt 1 -and [Math]::Abs($ph - 148) -lt 1) { 'A6' }
                    else { 'Custom' }
        $orientation = if ($pw -gt $ph) { 'Landscape' } else { 'Portrait' }

        $id = (Slug $b.Brand) + '-' + (Slug $code)
        $key = $id.ToLowerInvariant()
        $geometry = "$pw|$ph|$across|$down|$w|$h|$x|$y|$hgap|$vgap|$shape|$radius"
        if ($written.ContainsKey($key)) {
            if ($written[$key] -eq $geometry) { $skipped["$($b.Brand): same code and size listed twice"]++; continue }
            $id = $id + '-' + (Slug $pageName); $key = $id.ToLowerInvariant()
            if ($written.ContainsKey($key)) { $skipped["$($b.Brand): same code listed twice"]++; continue }
        }
        $written[$key] = $geometry

        $count = $across * $down
        $label = if ($ellipse -and [Math]::Abs($w - $h) -lt 0.05) { "$([char]0x2300) $(Num $w) mm" } else { "$(Num $w) $([char]0xD7) $(Num $h) mm" }
        $json = "{`n" +
            "  `"schema`": `"labelmaker.template/1`",`n" +
            "  `"id`": `"$(Json $id)`",`n" +
            "  `"brand`": `"$(Json $b.Brand)`",`n" +
            "  `"productCode`": `"$(Json $code)`",`n" +
            "  `"name`": `"$(Json $b.Brand) $(Json $code) $([char]0x2014) $label, $count-up`",`n" +
            "  `"categories`": [`"$category`"],`n" +
            "  `"page`": { `"size`": `"$pageName`", `"widthMm`": $(Num $pw), `"heightMm`": $(Num $ph) },`n" +
            "  `"orientation`": `"$orientation`",`n" +
            "  `"printableMarginMm`": { `"top`": $(Num $margin), `"right`": $(Num $margin), `"bottom`": $(Num $margin), `"left`": $(Num $margin) },`n" +
            "  `"grid`": {`n" +
            "    `"columns`": $across,`n" +
            "    `"rows`": $down,`n" +
            "    `"labelWidthMm`": $(Num $w),`n" +
            "    `"labelHeightMm`": $(Num $h),`n" +
            "    `"marginTopMm`": $(Num $y),`n" +
            "    `"marginLeftMm`": $(Num $x),`n" +
            "    `"hGapMm`": $(Num $hgap),`n" +
            "    `"vGapMm`": $(Num $vgap),`n" +
            "    `"cornerRadiusMm`": $(Num $radius),`n" +
            "    `"labelShape`": `"$shape`"`n" +
            "  },`n" +
            "  `"fillOrder`": `"RowMajor`",`n" +
            "  `"notes`": `"$(Json $desc)`",`n" +
            "  `"source`": `"Microsoft Word label database ($(Json $b.Brand), $($b.File))`"`n" +
            "}`n"

        if (-not $byFolder.ContainsKey($b.Folder)) { $byFolder[$b.Folder] = New-Object System.Collections.Generic.List[object] }
        $byFolder[$b.Folder].Add(@{ Id = $id; Json = $json })
    }
}

# One catalogue per brand (a JSON array of templates): LabelAxis opens a handful of files
# when it starts instead of thousands.
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
foreach ($folder in $byFolder.Keys | Sort-Object) {
    $entries = $byFolder[$folder] | Sort-Object { $_.Id } | ForEach-Object { '  ' + ($_.Json.TrimEnd("`n") -replace "`n", "`n  ") }
    [IO.File]::WriteAllText((Join-Path $OutDir "$folder.json"), "[`n" + ($entries -join ",`n") + "`n]`n", $utf8)
    Write-Host ("{0,-16} {1,5} sheets" -f $folder, $byFolder[$folder].Count)
}

foreach ($reason in $skipped.Keys | Sort-Object) { Write-Host ("  left out: {0} ({1})" -f $reason, $skipped[$reason]) }
