# Interactive Windows UI test. Use only a disposable launcher build, never a live installation.
param([Parameter(Mandatory=$true)][string]$Exe, [string]$DotNetRoot = '')
$ErrorActionPreference='Stop'
$root=Join-Path $env:TEMP ('nova-display-fixture-'+[guid]::NewGuid().ToString('N'))
$status=Join-Path $env:TEMP ('nova-display-status-'+[guid]::NewGuid().ToString('N')+'.txt')
$log=Join-Path $env:TEMP ('nova-display-log-'+[guid]::NewGuid().ToString('N')+'.txt')
$p=$null
function Log($m){Add-Content -Path $log -Value $m}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DisplayNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr h2, int x,int y,int cx,int cy,uint flags);
}
'@
function Bounds($h){$r=New-Object DisplayNative+RECT;[void][DisplayNative]::GetWindowRect($h,[ref]$r);return $r}
function FindId($el,[string]$id){$c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id);return $el.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)}
function InvokeId($el,$id){$e=FindId $el $id;if(!$e){throw "missing $id"};$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
try {
 New-Item -ItemType Directory -Path $root -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $root 'settings.json'),'{"AutoCheckUpdates":false}')
 $env:NOVA_ROOT=$root;if($DotNetRoot){$env:DOTNET_ROOT=$DotNetRoot}
 if(!(Test-Path -LiteralPath $Exe)){throw 'test executable missing'}
 $p=Start-Process $exe -ArgumentList '--page','Store' -PassThru
 for($i=0;$i -lt 60;$i++){Start-Sleep -Seconds 2;$p.Refresh();if($p.HasExited){throw 'launcher exited'};if($p.MainWindowHandle -ne [IntPtr]::Zero){break}}
 if($p.MainWindowHandle -eq [IntPtr]::Zero){throw 'window not ready'}
 Start-Sleep -Seconds 5
 $h=$p.MainWindowHandle;$win=[System.Windows.Automation.AutomationElement]::FromHandle($h)
 for($i=0;$i -lt 30;$i++){if(FindId $win 'StoreQuery'){break};Start-Sleep -Seconds 2}
 if(!(FindId $win 'StoreQuery')){throw 'Store page missing'};Log 'STORE_PAGE_READY'
 foreach($size in @(@(1024,700),@(800,500))){
  [void][DisplayNative]::SetWindowPos($h,[IntPtr]::Zero,20,20,$size[0],$size[1],0x0014);Start-Sleep -Seconds 4
  $b=Bounds $h;Log "RESIZED_$($size[0])x$($size[1])_ACTUAL_$($b.Right-$b.Left)x$($b.Bottom-$b.Top)"
  $p.Refresh();if(!$p.Responding){throw 'window unresponsive'}
  if(!(FindId $win 'StoreQuery')){throw 'search missing after resize'}
  if(!(FindId $win 'FullscreenButton')){throw 'fullscreen button missing'}
  $workspace=FindId $win 'WorkspaceScroll';if(!$workspace){throw 'workspace scroller missing'}
  $pattern=$workspace.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  Log "SCROLL_$($size[0]) H=$($pattern.Current.HorizontallyScrollable) V=$($pattern.Current.VerticallyScrollable)"
  if($size[0] -eq 800){
    if(!$pattern.Current.HorizontallyScrollable -or !$pattern.Current.VerticallyScrollable){throw 'small workspace should scroll both ways'}
    $pattern.SetScrollPercent(100,100);Start-Sleep -Seconds 2
    if($pattern.Current.HorizontalScrollPercent -lt 90 -or $pattern.Current.VerticalScrollPercent -lt 90){throw 'workspace did not scroll'}
    $pattern.SetScrollPercent(0,0)
  }
 }
 $b0=Bounds $h
 InvokeId $win 'FullscreenButton';Start-Sleep -Seconds 4
 $b=Bounds $h;$screen=[System.Windows.Forms.Screen]::FromHandle($h).Bounds
 Log "FULLSCREEN_WINDOW=$($b.Left),$($b.Top),$($b.Right),$($b.Bottom) MONITOR=$($screen.Left),$($screen.Top),$($screen.Right),$($screen.Bottom)"
 if([Math]::Abs($b.Left-$screen.Left) -gt 2 -or [Math]::Abs($b.Top-$screen.Top) -gt 2 -or [Math]::Abs($b.Right-$screen.Right) -gt 2 -or [Math]::Abs($b.Bottom-$screen.Bottom) -gt 2){throw 'fullscreen bounds mismatch'}
 InvokeId $win 'FullscreenButton';Start-Sleep -Seconds 4
 $b=Bounds $h;Log "RESTORE_WINDOW=$($b.Left),$($b.Top),$($b.Right),$($b.Bottom)"
 if([Math]::Abs(($b.Right-$b.Left)-($b0.Right-$b0.Left)) -gt 4 -or [Math]::Abs(($b.Bottom-$b.Top)-($b0.Bottom-$b0.Top)) -gt 4){throw 'restore size mismatch'}
 InvokeId $win 'MaximizeButton';Start-Sleep -Seconds 4
 $b=Bounds $h;$area=[System.Windows.Forms.Screen]::FromHandle($h).WorkingArea;Log "MAX_WINDOW=$($b.Left),$($b.Top),$($b.Right),$($b.Bottom) WORK=$($area.Left),$($area.Top),$($area.Right),$($area.Bottom)"
 if([Math]::Abs(($b.Right-$b.Left)-$area.Width) -gt 20 -or [Math]::Abs(($b.Bottom-$b.Top)-$area.Height) -gt 20){throw 'maximize work area mismatch'}
 InvokeId $win 'FullscreenButton';Start-Sleep -Seconds 3
 $full=Bounds $h;if([Math]::Abs(($full.Bottom-$full.Top)-$screen.Height) -gt 2){throw 'fullscreen from maximized failed'}
 InvokeId $win 'FullscreenButton';Start-Sleep -Seconds 3
 $back=Bounds $h;if([Math]::Abs(($back.Bottom-$back.Top)-$area.Height) -gt 20){throw 'maximized state not restored after fullscreen'}
 Log 'MAXIMIZED_FULLSCREEN_RESTORE_OK'
 InvokeId $win 'MaximizeButton';Start-Sleep -Seconds 3
 $p.Refresh();if(!$p.Responding){throw 'not responding after restore'}
 Set-Content $status 'SUCCESS';Log 'PASS'
}catch{Log ('FAIL: '+$_.Exception.ToString());Set-Content $status ('FAIL: '+$_.Exception.Message)}
finally{if($p -and !$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}; if(Test-Path $log){Get-Content $log};if(Test-Path $root){Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue}}
if((Get-Content $status -Raw).Trim() -ne 'SUCCESS'){exit 1}
