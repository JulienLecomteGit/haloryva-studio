param([string]$Language,[string]$AppPath,[string]$OutputPath)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted'){throw 'GitHub only'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$flags=[Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$app=[Reflection.Assembly]::LoadFrom($AppPath)
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$profile=Join-Path $env:RUNNER_TEMP ('store-profile-'+$Language)
$app.GetType('CursorFlow.TrailTrial').GetMethod('Initialize',$flags).Invoke($null,@(,[string[]]@('--trail-session',$profile)))|Out-Null
$config=[Activator]::CreateInstance($app.GetType('CursorFlow.Config'),$true)
$config.GetType().GetField('UiLanguage',$flags).SetValue($config,$Language)
foreach($key in @('Ring','Trail','Clicks','KeyDisplay','ClickSounds','LaunchAtLogin')){$config.GetType().GetField($key,$flags).SetValue($config,$false)}
$app.GetType('CursorFlow.ConfigStore').GetMethod('Save',$flags).Invoke($null,@($config))|Out-Null
$controller=[Activator]::CreateInstance($app.GetType('CursorFlow.CursorController'),$true)
try{
 $controller.GetType().GetMethod('ShowSettings',$flags).Invoke($controller,$null)|Out-Null
 $form=$controller.GetType().GetField('settings',$flags).GetValue($controller)
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 if($screen.Width -ne 1920 -or $screen.Height -ne 1080){throw "Unexpected actual screen $screen"}
 $form.WindowState=[Windows.Forms.FormWindowState]::Normal
 $form.Bounds=New-Object Drawing.Rectangle($screen.X,$screen.Y,1920,1080)
 $form.TopMost=$true
 $form.Show();$form.Activate();$form.BringToFront()
 [Windows.Forms.Cursor]::Position=New-Object Drawing.Point(1919,1079)
 for($i=0;$i -lt 40;$i++){[Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 50}
 if($form.Bounds.Width -ne 1920 -or $form.Bounds.Height -ne 1080){throw 'Studio dimensions changed'}
 $lang=$app.GetType('CursorFlow.L').GetMethod('get_Language',$flags).Invoke($null,$null)
 if($lang -ne $Language){throw 'Wrong language'}
 $bitmap=New-Object Drawing.Bitmap(1920,1080)
 try{$g=[Drawing.Graphics]::FromImage($bitmap);try{$g.CopyFromScreen($screen.Location,[Drawing.Point]::Empty,$bitmap.Size)}finally{$g.Dispose()};$bitmap.Save($OutputPath,[Drawing.Imaging.ImageFormat]::Png)}finally{$bitmap.Dispose()}
 [ordered]@{Language=$lang;Capture=[IO.Path]::GetFileName($OutputPath);Width=1920;Height=1080;StudioBounds=$form.Bounds.ToString();Execution='Existing compiled UI loaded in PowerShell STA, not Program.Main';ApplicationRecompiled=$false;DriverCompiled=$false;SelfTestsExecuted=$false;Historical195Executed=$false}|ConvertTo-Json|Set-Content ($OutputPath+'.json')
}finally{$controller.GetType().GetMethod('Exit',$flags).Invoke($controller,$null)|Out-Null}
