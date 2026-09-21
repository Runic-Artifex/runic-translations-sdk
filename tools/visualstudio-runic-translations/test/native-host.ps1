# Run through the repository's interactive Windows UI Automation runner after
# installing the VSIX and starting the isolated /RootSuffix RunicRmf2 instance.
param([string]$Executable, [string]$ReceiptPath)
$ErrorActionPreference = 'Stop'
trap { ($_.Exception.ToString() + "`n" + $_.ScriptStackTrace + "`n" + $_.InvocationInfo.PositionMessage) | Set-Content "$ReceiptPath.error"; exit 1 }
Add-Type -Path "$PSScriptRoot\VisualStudioAutomation.cs"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$ide = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $Executable -and $_.CommandLine -match '/RootSuffix\s+RunicRmf2(?:\s|$)' })
if ($ide.Count -ne 1) { throw 'Start exactly one isolated RunicRmf2 IDE instance.' }
$ideId = [int]$ide[0].ProcessId
$dte = [VisualStudioAutomation]::FindDte($ideId)
if (!$dte) { throw 'IDE automation is not ready.' }
function Wait-For([scriptblock]$read, [string]$failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do { $value = & $read; if ($value) { return $value }; Start-Sleep -Milliseconds 250 } while ([DateTime]::UtcNow -lt $deadline)
    throw $failure
}
function Preview([string]$key) {
    $handle = Wait-For { $h = [VisualStudioAutomation]::FindTitle("Runic $([char]0xb7) $key"); if ($h -ne [IntPtr]::Zero) { $h } } "Preview $key did not open."
    return [Windows.Automation.AutomationElement]::FromHandle($handle)
}
function Named($window, [string]$name, $type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $name),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $type))
    return $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Texts($window) {
    return (@($window.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)) | ForEach-Object {$_.Current.Name}) -join '|')
}
function Render($window) { (Named $window 'Render preview' ([Windows.Automation.ControlType]::Button)).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Close-Preview($window) { $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close() }
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$fixture = Join-Path $env:TEMP ('runic-vs-host-' + [Guid]::NewGuid().ToString('N'))
Copy-Item "$root\specs\translations\examples\rmf2" $fixture -Recurse
$project = Get-Content "$fixture\runic.json" -Raw | ConvertFrom-Json
$project | Add-Member -NotePropertyName executionProfile -NotePropertyValue 'rmf2-execution-v2' -Force
[IO.File]::WriteAllText("$fixture\runic.json", ($project | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$priorDocument = $dte.ActiveDocument
$document = $null; $preview = $null
try {
    # Close only previews belonging to this IDE before starting the checks.
    foreach ($key in @('payment','plain')) {
        $handle = [VisualStudioAutomation]::FindTitle("Runic $([char]0xb7) $key")
        if ($handle -ne [IntPtr]::Zero) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($handle)
            if ($window.Current.ProcessId -eq $ideId) { Close-Preview $window }
        }
    }
    $opened = $dte.ItemOperations.OpenFile("$fixture\en.rmf2")
    $opened.Activate()
    $document = Wait-For {
        $candidate = $dte.ActiveDocument
        if ($candidate -and $candidate.FullName -eq "$fixture\en.rmf2" -and $candidate.Selection) { $candidate }
    } 'The temporary RMF2 editor did not activate.'
    $document.Selection.GotoLine(5)
    [VisualStudioAutomation]::SetForegroundWindow((Get-Process -Id $ideId).MainWindowHandle) | Out-Null
    $dte.ExecuteCommand('Tools.RunicPreviewMessage', '')
    $preview = Preview 'payment'
    Wait-For { if ((Texts $preview) -like '*[[]shop:badge[]] Ready*') { $true } } 'Payment content did not render.' | Out-Null
    $retry = Named $preview 'Retry' ([Windows.Automation.ControlType]::Button)
    if (!$retry -or $retry.Current.IsEnabled) { throw 'Preview action must be present and inert.' }
    $count = Named $preview 'count' ([Windows.Automation.ControlType]::Edit)
    $count.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('invalid')
    Render $preview
    Wait-For { if ((Texts $preview) -match 'integer|number|numeric|input string.*invalid') { $true } } 'Invalid numeric sample did not report an error.' | Out-Null
    $count.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('1')
    Render $preview
    Wait-For { if ((Texts $preview) -like '*[[]shop:badge[]] Ready*') { $true } } 'Preview did not recover.' | Out-Null
    $locale = Named $preview 'Preview locale' ([Windows.Automation.ControlType]::ComboBox)
    $locale.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $german = Wait-For { Named $locale 'de' ([Windows.Automation.ControlType]::ListItem) } 'German locale option is missing.'
    $german.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $locale.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    Render $preview
    Wait-For { if ((Texts $preview) -like '*Bereit*') { $true } } 'German preview did not render.' | Out-Null
    Close-Preview $preview; $preview = $null
    $original = [IO.File]::ReadAllText("$fixture\en.rmf2")
    $document.Activate(); $document.Selection.SelectAll(); $document.Selection.Insert($original.Replace('plain = Payment details', 'plain = UNSAVED preview marker'))
    $document.Selection.GotoLine(11)
    $before = @(Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $ideId -and $_.Name -eq 'dotnet.exe'} | ForEach-Object {$_.ProcessId})
    $dte.ExecuteCommand('Tools.RunicRestartLanguageServer', '')
    $after = Wait-For { Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $ideId -and $_.Name -eq 'dotnet.exe' -and $_.ProcessId -notin $before} } 'Server did not restart.'
    Start-Sleep -Seconds 2
    $dte.ExecuteCommand('Tools.RunicPreviewMessage', '')
    $preview = Preview 'plain'
    Wait-For { if ((Texts $preview) -like '*UNSAVED preview marker*') { $true } } 'Restart lost the unsaved buffer.' | Out-Null
    $receipt = @{sessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;windowName='Visual Studio RMF2';snapshotName='execution-v2 preview, validation, locale, unsaved restart';ideProcessId=$ideId;serverBefore=$before;serverAfter=$after.ProcessId;checks=@('explicit rmf2-execution-v2 project','registered preview command','native inert rich content','invalid-number recovery','locale selection','registered restart command','unsaved buffer after restart')}
}
finally {
    if ($preview) { Close-Preview $preview }
    if ($document) { $document.Close(2) } # vsSaveChangesNo
    # A restarted server uses the fixture as its current directory on Windows.
    # Restore the prior document and restart before deleting that directory.
    if ($priorDocument) { $priorDocument.Activate() }
    else { $dte.ItemOperations.OpenFile("$root\specs\translations\examples\rmf2\en.rmf2") | Out-Null }
    $dte.ExecuteCommand('Tools.RunicRestartLanguageServer', '')
    Wait-For { try { [IO.Directory]::Delete($fixture, $true); $true } catch [IO.IOException] { $false } } 'Temporary fixture is still in use.' | Out-Null
}

$receipt | ConvertTo-Json | Set-Content $ReceiptPath
