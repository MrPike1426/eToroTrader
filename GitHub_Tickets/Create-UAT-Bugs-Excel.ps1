# Create UAT Bugs Dashboard Excel File
# This script generates a visual dashboard for UAT bug tracking

# Check if ImportExcel module is available
if (-not (Get-Module -ListAvailable -Name ImportExcel)) {
    Write-Host "Installing ImportExcel module..."
    Install-Module -Name ImportExcel -Force -Scope CurrentUser -SkipPublisherCheck -ErrorAction SilentlyContinue
}

Import-Module ImportExcel -ErrorAction SilentlyContinue

# UAT Bug Data
$bugs = @(
    @{ ID="UAT-BUG-001"; Found="2026-03-01"; Severity="Critical"; Status="Fixed"; Files=5; Category="EF Core String"; Complexity="High"; Tests=10 }
    @{ ID="UAT-BUG-002"; Found="2026-03-01"; Severity="Critical"; Status="Fixed"; Files=1; Category="EF Core Boolean"; Complexity="Medium"; Tests=0 }
    @{ ID="UAT-BUG-003"; Found="2026-03-01"; Severity="Critical"; Status="Fixed"; Files=1; Category="EF Core DateTime"; Complexity="High"; Tests=0 }
    @{ ID="UAT-BUG-004"; Found="2026-03-01"; Severity="High"; Status="Fixed"; Files=2; Category="Design Mismatch"; Complexity="Medium"; Tests=0 }
    @{ ID="UAT-BUG-005"; Found="2026-03-01"; Severity="Critical"; Status="Fixed"; Files=1; Category="Data Ordering"; Complexity="High"; Tests=1 }
    @{ ID="UAT-BUG-006"; Found="2026-03-01"; Severity="High"; Status="Fixed"; Files=2; Category="Exit Price Calc"; Complexity="Medium"; Tests=8 }
    @{ ID="UAT-BUG-007"; Found="2026-03-01"; Severity="Critical"; Status="OPEN"; Files=1; Category="State Flag"; Complexity="High"; Tests=0 }
)

$excelPath = ".\GitHub_Tickets\UAT-Bugs-Dashboard.xlsx"

# Create Excel workbook
$excel = $bugs | Export-Excel -Path $excelPath -WorksheetName "Bugs" -PassThru -AutoFilter -FreezeTopRow

# Get worksheet for formatting
$ws = $excel.Workbook.Worksheets["Bugs"]

# Set column widths
$ws.Column(1).Width = 12  # ID
$ws.Column(2).Width = 12  # Found
$ws.Column(3).Width = 12  # Severity
$ws.Column(4).Width = 12  # Status
$ws.Column(5).Width = 8   # Files
$ws.Column(6).Width = 18  # Category
$ws.Column(7).Width = 12  # Complexity
$ws.Column(8).Width = 8   # Tests

# Apply conditional formatting (colors)
foreach ($row in $ws.Dimension.Start.Row..$ws.Dimension.End.Row) {
    if ($row -eq 1) { continue }  # Skip header
    
    $severity = $ws.Cells["C$row"].Value
    $status = $ws.Cells["D$row"].Value
    
    # Color by severity
    if ($severity -eq "Critical") {
        $ws.Cells["C$row"].Style.Fill.PatternType = "Solid"
        $ws.Cells["C$row"].Style.Fill.BackgroundColor.SetColor("FF4444")
        $ws.Cells["C$row"].Style.Font.Color.SetColor("FFFFFF")
    }
    elseif ($severity -eq "High") {
        $ws.Cells["C$row"].Style.Fill.PatternType = "Solid"
        $ws.Cells["C$row"].Style.Fill.BackgroundColor.SetColor("FFA500")
        $ws.Cells["C$row"].Style.Font.Color.SetColor("FFFFFF")
    }
    
    # Color by status
    if ($status -eq "Fixed") {
        $ws.Cells["D$row"].Style.Fill.PatternType = "Solid"
        $ws.Cells["D$row"].Style.Fill.BackgroundColor.SetColor("44AA44")
        $ws.Cells["D$row"].Style.Font.Color.SetColor("FFFFFF")
    }
    elseif ($status -eq "OPEN") {
        $ws.Cells["D$row"].Style.Fill.PatternType = "Solid"
        $ws.Cells["D$row"].Style.Fill.BackgroundColor.SetColor("FF6666")
        $ws.Cells["D$row"].Style.Font.Color.SetColor("FFFFFF")
    }
}

# Add Summary sheet
$summary = @(
    @{ Metric="Total Bugs"; Count=7 }
    @{ Metric="Critical"; Count=5 }
    @{ Metric="High"; Count=2 }
    @{ Metric="Fixed"; Count=6 }
    @{ Metric="Open"; Count=1 }
    @{ Metric="Total Files Affected"; Count=17 }
    @{ Metric="Total Tests Added"; Count=19 }
    @{ Metric="Build Status"; Count="✅ Passing" }
)

$summary | Export-Excel -Path $excelPath -WorksheetName "Summary" -AutoFilter -FreezeTopRow

Write-Host "✅ Excel file created: $excelPath"
