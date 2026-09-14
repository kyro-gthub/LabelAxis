<#
    Regenerates the Formtec sheet templates from Microsoft Word's own label database
    (Office16\PAGESIZE\PGLBL010.XML), which is what Word uses for Mailings > Labels.
    Every number (label size, labels across/down, gaps, position of the first label)
    comes straight from that file, converted to millimetres.

    Existing formtec-*.json files are replaced; ids listed in -Keep (sheets Word does
    not know) are left alone.

    Usage:  powershell -ExecutionPolicy Bypass -File build\import-word-labels.ps1
#>
param(
    [string]$LabelFile = 'C:\Program Files\Microsoft Office\root\Office16\PAGESIZE\PGLBL010.XML',
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'templates\formtec'),
    [string[]]$Keep = @('formtec-gs-1084', 'formtec-gs-1096', 'formtec-gs-1136', 'formtec-gs-1145')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $LabelFile)) { throw "Word's label file was not found: $LabelFile" }

$inv = [Globalization.CultureInfo]::InvariantCulture
$utf8 = New-Object System.Text.UTF8Encoding($false)
[xml]$xml = [IO.File]::ReadAllText($LabelFile)

function Mm([string]$value, [string]$units) {
    $v = [double]::Parse($value, $inv)
    switch ($units) { 'in' { return [Math]::Round($v * 25.4, 3) } 'mm' { return [Math]::Round($v, 3) } default { return [Math]::Round($v * 10, 3) } }
}
function Num([double]$v) { $v.ToString('0.###', $inv) }
function Json([string]$s) { ($s -replace '\\', '\\' -replace '"', '\"') }

$written = @()
foreach ($p in $xml.SelectNodes('//*[local-name()="product"]')) {
    $name = $p.SelectSingleNode('.//*[local-name()="locName"]').InnerText.Trim()
    if ($name -notlike 'Formtec *') { continue }

    $code = $name.Substring(8).Trim()
    $id = 'formtec-' + ($code.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
    $units = $p.units
    $panel = $p.SelectSingleNode('.//*[local-name()="masterPanel"]')
    $sheet = $p.SelectSingleNode('.//*[local-name()="sheet"]')
    $grid = $p.SelectSingleNode('.//*[local-name()="sheetGrid"]')
    $cutout = $p.SelectSingleNode('.//*[local-name()="cutout"]')
    $desc = ($p.SelectSingleNode('.//*[local-name()="locDesc"]')).InnerText

    $w = Mm $panel.width $units; $h = Mm $panel.height $units
    $pw = Mm $sheet.width $units; $ph = Mm $sheet.height $units
    $across = [int]$grid.numAcross; $down = [int]$grid.numDown
    $hgap = Mm $grid.horizGap $units; $vgap = Mm $grid.vertGap $units
    $x = Mm $grid.posX $units; $y = Mm $grid.posY $units
    $shape = if ($cutout -and $cutout.shape -eq 'ellipse') { 'Ellipse' } else { 'Rectangle' }

    $pageName = if ([Math]::Abs($pw - 210) -lt 1 -and [Math]::Abs($ph - 297) -lt 1) { 'A4' }
                elseif ([Math]::Abs($pw - 215.9) -lt 1 -and [Math]::Abs($ph - 279.4) -lt 1) { 'Letter' }
                elseif ([Math]::Abs($pw - 105) -lt 1 -and [Math]::Abs($ph - 148) -lt 1) { 'A6' }
                else { 'Custom' }
    $orientation = if ($pw -gt $ph) { 'Landscape' } else { 'Portrait' }

    $category = switch ($p.layoutGroup) { 'mailingLabel' { 'Address' } 'mediaLabel' { 'CD/DVD & media' } default { 'General' } }
    $categories = @($category)
    if ($code -like 'GS-*') { $categories += 'Formtec GS' }
    $categoryJson = ($categories | ForEach-Object { '"' + (Json $_) + '"' }) -join ', '

    $right = $x + ($across * $w) + (($across - 1) * $hgap)
    $bottom = $y + ($down * $h) + (($down - 1) * $vgap)
    if ($right -gt $pw + 0.6 -or $bottom -gt $ph + 0.6) {
        Write-Warning "$name does not fit its sheet (right $right of $pw, bottom $bottom of $ph mm)"
    }

    $count = $across * $down
    $json = "{`n" +
        "  `"schema`": `"labelmaker.template/1`",`n" +
        "  `"id`": `"$id`",`n" +
        "  `"brand`": `"Formtec`",`n" +
        "  `"productCode`": `"$(Json $code)`",`n" +
        "  `"name`": `"Formtec $(Json $code) $([char]0x2014) $(Num $w) $([char]0xD7) $(Num $h) mm, $count-up`",`n" +
        "  `"categories`": [$categoryJson],`n" +
        "  `"page`": { `"size`": `"$pageName`", `"widthMm`": $(Num $pw), `"heightMm`": $(Num $ph) },`n" +
        "  `"orientation`": `"$orientation`",`n" +
        "  `"printableMarginMm`": { `"top`": 4.0, `"right`": 4.0, `"bottom`": 4.0, `"left`": 4.0 },`n" +
        "  `"grid`": {`n" +
        "    `"columns`": $across,`n" +
        "    `"rows`": $down,`n" +
        "    `"labelWidthMm`": $(Num $w),`n" +
        "    `"labelHeightMm`": $(Num $h),`n" +
        "    `"marginTopMm`": $(Num $y),`n" +
        "    `"marginLeftMm`": $(Num $x),`n" +
        "    `"hGapMm`": $(Num $hgap),`n" +
        "    `"vGapMm`": $(Num $vgap),`n" +
        "    `"cornerRadiusMm`": 0.0,`n" +
        "    `"labelShape`": `"$shape`"`n" +
        "  },`n" +
        "  `"fillOrder`": `"RowMajor`",`n" +
        "  `"notes`": `"$(Json $desc)`",`n" +
        "  `"source`": `"Microsoft Word label database (Formtec, PGLBL010.XML)`"`n" +
        "}`n"

    [IO.File]::WriteAllText((Join-Path $OutDir "$id.json"), $json, $utf8)
    $written += "$id.json"
}

# Old estimated Formtec files that Word does not define are removed, except those kept on purpose.
Get-ChildItem $OutDir -Filter 'formtec-*.json' |
    Where-Object { $written -notcontains $_.Name -and $Keep -notcontains $_.BaseName } |
    ForEach-Object { [IO.File]::Delete($_.FullName); Write-Host "removed $($_.Name)" }

Write-Host "Wrote $($written.Count) Formtec templates from Word's label database."
