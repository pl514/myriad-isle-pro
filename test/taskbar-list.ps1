Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd')
$tray = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $tray) { Write-Output 'no tray'; exit }

$all = $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$rows = @()
foreach ($e in $all) {
    $n = $e.Current.Name
    if (-not $n) { continue }
    $r = $e.Current.BoundingRectangle
    $rows += [pscustomobject]@{ Name = $n; Type = $e.Current.ControlType.ProgrammaticName; X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
}
$rows | Format-Table -AutoSize
