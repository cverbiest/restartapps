Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Configuration
$ConfigFile = Join-Path $PSScriptRoot "restartapps.json"

if (-not (Test-Path $ConfigFile)) {
    [System.Windows.Forms.MessageBox]::Show("Configuration file not found: $ConfigFile", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    exit
}

try {
    $AppList = Get-Content -Path $ConfigFile | ConvertFrom-Json
    foreach ($app in $AppList) {
        $app.Commandline = [System.Environment]::ExpandEnvironmentVariables($app.Commandline)
    }
}
catch {
    [System.Windows.Forms.MessageBox]::Show("Error reading configuration file: $_", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    exit
}

# Helper to extract process name from command line
function Get-ProcessNameFromCommand {
    param([string]$Commandline)

    # Simple extraction: take the first part of the string, remove path and extension
    # This handles "notepad.exe", "c:\windows\notepad.exe", "c:\my app\app.exe -arg" (assuming quotes if spaces)

    # Very basic parser: split by space, take first token unless quoted
    $exe = ""
    if ($Commandline.StartsWith('"')) {
        $parts = $Commandline.Split('"')
        if ($parts.Count -gt 1) {
            $exe = $parts[1]
        }
    }
    else {
        $exe = $Commandline.Split(' ')[0]
    }

    return [System.IO.Path]::GetFileNameWithoutExtension($exe)
}

# UI Setup
$Form = New-Object System.Windows.Forms.Form
$Form.Text = "Restart Apps"
$Form.Size = New-Object System.Drawing.Size(400, 500)
$Form.StartPosition = "CenterScreen"
$Form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
$Form.MaximizeBox = $false

# App List
$LabelConfig = New-Object System.Windows.Forms.Label
$LabelConfig.Text = "Select applications to START (All listed will be stopped):"
$LabelConfig.Location = New-Object System.Drawing.Point(10, 10)
$LabelConfig.Size = New-Object System.Drawing.Size(360, 20)
$Form.Controls.Add($LabelConfig)

$CheckListBox = New-Object System.Windows.Forms.CheckedListBox
$CheckListBox.Location = New-Object System.Drawing.Point(10, 35)
$CheckListBox.Size = New-Object System.Drawing.Size(365, 250)
$CheckListBox.CheckOnClick = $true
$Form.Controls.Add($CheckListBox)

foreach ($app in $AppList) {
    $isChecked = if ($null -ne $app.Restart) { $app.Restart } else { $false }
    [void]$CheckListBox.Items.Add($app.Name, $isChecked)
}

# Log Output
$TxtLog = New-Object System.Windows.Forms.TextBox
$TxtLog.Multiline = $true
$TxtLog.ScrollBars = "Vertical"
$TxtLog.Location = New-Object System.Drawing.Point(10, 330)
$TxtLog.Size = New-Object System.Drawing.Size(365, 120)
$TxtLog.ReadOnly = $true
$Form.Controls.Add($TxtLog)

# Button
$BtnRestart = New-Object System.Windows.Forms.Button
$BtnRestart.Text = "Kill All && Start Selected"
$BtnRestart.Location = New-Object System.Drawing.Point(10, 295)
$BtnRestart.Size = New-Object System.Drawing.Size(365, 30)
$Form.Controls.Add($BtnRestart)

# Logger Helper
function Log-Message {
    param([string]$Message)
    $TxtLog.AppendText("$(Get-Date -Format 'HH:mm:ss'): $Message`r`n")
    $Form.Refresh() # Force redraw
}

# Action Logic
$BtnRestart.Add_Click({
        $BtnRestart.Enabled = $false
        $Form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor

        try {
            # 1. Kill ALL apps in the list (regardless of selection)
            Log-Message "STOPPING applications..."
            foreach ($app in $AppList) {
                $procName = Get-ProcessNameFromCommand -Commandline $app.Commandline
                if ($procName) {
                    Log-Message "Stopping $($app.Name) ($procName)..."
                    Stop-Process -Name $procName -ErrorAction SilentlyContinue -Force
                }
            }

            Start-Sleep -Seconds 1 # Give brief pause for cleanup

            # 2. Start SELECTED apps
            Log-Message "STARTING selected applications..."
            foreach ($item in $CheckListBox.CheckedItems) {
                # Find the app object
                $appConfig = $AppList | Where-Object { $_.Name -eq $item }

                if ($appConfig) {
                    Log-Message "Starting $($appConfig.Name)..."
                    try {
                        # Handling arguments if present

                        # Rudimentary command parsing
                        # If the commandline has arguments, Start-Process needs them separate usually,
                        # OR we can just pass the whole thing to CMD /C or run it directly if it's simple

                        # Better approach: Use ArgumentList if spaces exist
                        # For simplicity in this version, we will try to execute it directly or rely on shell execute

                        $pinfo = New-Object System.Diagnostics.ProcessStartInfo
                        $pinfo.FileName = "powershell"
                        $pinfo.Arguments = "-Command `"$($appConfig.Commandline)`""
                        $pinfo.WindowStyle = "Normal"

                        # To simply run the command line as defined:
                        Start-Process -FilePath "cmd.exe" -ArgumentList "/c start `"`" $($appConfig.Commandline)" -WindowStyle Hidden

                        # Alternatively, if we want to track it, we'd need to parse it better.
                        # Using cmd /c start "" "command" allows generic execution of whatever is in the JSON string.
                    }
                    catch {
                        Log-Message "Failed to start $($appConfig.Name): $_"
                    }
                }
            }

            Log-Message "Operation Complete."
        }
        catch {
            Log-Message "Error: $_"
        }
        finally {
            $Form.Cursor = [System.Windows.Forms.Cursors]::Default
            $BtnRestart.Enabled = $true
        }
    })

# Show
$Form.Add_Shown({
        $Form.Activate()
    })
[void]$Form.ShowDialog()
