# Performance profiling script for wave-compare algorithm
# Tests with different max levels and strategies

$source = "myheritage.ged"
$destination = "geni.ged"
$anchorSource = "@I1@"
$anchorDest = "@I6000000206529622827@"

Write-Host "=== Wave Compare Performance Profiling ===" -ForegroundColor Cyan
Write-Host ""

# Test 1: Adaptive strategy with different levels
$levels = @(1, 3, 5, 10)
Write-Host "Test 1: Adaptive Strategy with Different Levels" -ForegroundColor Yellow
foreach ($level in $levels) {
    Write-Host "  Testing max-level=$level..." -NoNewline
    $startTime = Get-Date
    $output = & ".\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe" wave-compare `
        --source $source `
        --destination $destination `
        --anchor-source $anchorSource `
        --anchor-destination $anchorDest `
        --max-level $level `
        --threshold-strategy adaptive `
        --base-threshold 60 `
        --output "profile_adaptive_level$level.json" `
        2>&1 | Out-String

    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds

    # Parse statistics from output
    if ($output -match "Mapped: (\d+)/(\d+)") {
        $mapped = $Matches[1]
        $total = $Matches[2]
        Write-Host " Duration: $([math]::Round($duration, 2))s, Mapped: $mapped/$total" -ForegroundColor Green
    } else {
        Write-Host " Duration: $([math]::Round($duration, 2))s" -ForegroundColor Green
    }
}

Write-Host ""

# Test 2: Different strategies with fixed level
$strategies = @("fixed", "adaptive", "aggressive", "conservative")
$fixedLevel = 3

Write-Host "Test 2: Different Strategies (max-level=$fixedLevel)" -ForegroundColor Yellow
foreach ($strategy in $strategies) {
    Write-Host "  Testing $strategy..." -NoNewline
    $startTime = Get-Date
    $output = & ".\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe" wave-compare `
        --source $source `
        --destination $destination `
        --anchor-source $anchorSource `
        --anchor-destination $anchorDest `
        --max-level $fixedLevel `
        --threshold-strategy $strategy `
        --base-threshold 60 `
        --output "profile_${strategy}_level$fixedLevel.json" `
        2>&1 | Out-String

    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds

    # Parse statistics from output
    if ($output -match "Mapped: (\d+)/(\d+)") {
        $mapped = $Matches[1]
        $total = $Matches[2]
        Write-Host " Duration: $([math]::Round($duration, 2))s, Mapped: $mapped/$total" -ForegroundColor Green
    } else {
        Write-Host " Duration: $([math]::Round($duration, 2))s" -ForegroundColor Green
    }
}

Write-Host ""

# Test 3: Different base thresholds
$thresholds = @(40, 50, 60, 70, 80)
Write-Host "Test 3: Different Base Thresholds (adaptive, level=3)" -ForegroundColor Yellow
foreach ($threshold in $thresholds) {
    Write-Host "  Testing threshold=$threshold..." -NoNewline
    $startTime = Get-Date
    $output = & ".\GedcomGeniSync.Cli\bin\Debug\net8.0\GedcomGeniSync.Cli.exe" wave-compare `
        --source $source `
        --destination $destination `
        --anchor-source $anchorSource `
        --anchor-destination $anchorDest `
        --max-level 3 `
        --threshold-strategy adaptive `
        --base-threshold $threshold `
        --output "profile_threshold$threshold.json" `
        2>&1 | Out-String

    $endTime = Get-Date
    $duration = ($endTime - $startTime).TotalSeconds

    # Parse statistics from output
    if ($output -match "Mapped: (\d+)/(\d+)") {
        $mapped = $Matches[1]
        $total = $Matches[2]
        Write-Host " Duration: $([math]::Round($duration, 2))s, Mapped: $mapped/$total" -ForegroundColor Green
    } else {
        Write-Host " Duration: $([math]::Round($duration, 2))s" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Profiling Complete ===" -ForegroundColor Cyan
Write-Host "Result files: profile_*.json"
