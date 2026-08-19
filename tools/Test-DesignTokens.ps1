param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$appRoot = Join-Path $ProjectRoot 'src\SubtitleTranslator.App'
$xamlFiles = Get-ChildItem -LiteralPath $appRoot -Filter '*.xaml' -Recurse |
    Where-Object { $_.FullName -notlike '*\Themes\DesignTokens.*.xaml' }

$legacyKeys = @(
    'AppBackgroundBrush', 'SidebarBrush', 'PrimaryBrush', 'PrimaryHoverBrush',
    'TextPrimaryBrush', 'TextSecondaryBrush', 'BorderBrush', 'SuccessBrush',
    'WarningBrush', 'DangerBrush', 'ControlCornerRadius', 'CardCornerRadius',
    'PageTitleStyle', 'SectionTitleStyle', 'FieldLabelStyle'
)

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($file in $xamlFiles) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        if ($line -match '#[0-9A-Fa-f]{6,8}') {
            $violations.Add("$($file.FullName):$lineNumber contains a hard-coded color")
        }
        foreach ($key in $legacyKeys) {
            if ($line -match [regex]::Escape("{StaticResource $key}")) {
                $violations.Add("$($file.FullName):$lineNumber uses legacy token $key")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Design Token audit passed for $($xamlFiles.Count) XAML files."
