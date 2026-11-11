using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Automation;
public static class SettingsUi {
 public static AutomationElement Root(){
  var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,Condition.TrueCondition);
  foreach(AutomationElement w in windows){try{
   string p=Process.GetProcessById(w.Current.ProcessId).ProcessName;
   if((p.Equals("SystemSettings",StringComparison.OrdinalIgnoreCase)||p.Equals("ApplicationFrameHost",StringComparison.OrdinalIgnoreCase)) &&
      (w.Current.Name=="Settings"||w.Current.Name=="Paramètres"||p.Equals("SystemSettings",StringComparison.OrdinalIgnoreCase)) && !w.Current.IsOffscreen)return w;
  }catch{}}
  return null;
 }
 public static string Describe(AutomationElement e){
  object p; string toggle="";
  if(e.TryGetCurrentPattern(TogglePattern.Pattern,out p))toggle=" toggle="+((TogglePattern)p).Current.ToggleState;
  return "name="+e.Current.Name+" id="+e.Current.AutomationId+" type="+e.Current.ControlType.ProgrammaticName+" enabled="+e.Current.IsEnabled+" offscreen="+e.Current.IsOffscreen+toggle;
 }
 public static string[] Dump(AutomationElement root){
  var lines=new List<string>();lines.Add(Describe(root));
  var all=root.FindAll(TreeScope.Descendants,Condition.TrueCondition);
  for(int i=0;i<Math.Min(500,all.Count);i++){try{lines.Add(Describe(all[i]));}catch{}}
  return lines.ToArray();
 }
 public static AutomationElement Target(AutomationElement root,string marker){
  var all=root.FindAll(TreeScope.Descendants,Condition.TrueCondition);
  var found=new List<AutomationElement>();
  foreach(AutomationElement e in all){
   if(e.Current.Name.IndexOf(marker,StringComparison.OrdinalIgnoreCase)<0)continue;
   var parent=e;
   for(int depth=0;depth<5 && parent!=null && !Automation.Compare(parent,root);depth++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){
    var candidates=parent.FindAll(TreeScope.Subtree,new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty,true));
    if(candidates.Count==1){bool duplicate=false;foreach(var prior in found)if(Automation.Compare(prior,candidates[0]))duplicate=true;if(!duplicate)found.Add(candidates[0]);break;}
    if(candidates.Count>1)break;
   }
  }
  if(found.Count!=1)throw new Exception("Expected one Haloryva startup toggle; found "+found.Count);
  return found[0];
 }
 public static string State(AutomationElement e){return ((TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState.ToString();}
 public static void Disable(AutomationElement e){
  if(!e.Current.IsEnabled||State(e)!="On")throw new Exception("Only an enabled On target can be disabled");
  object scroll;if(e.TryGetCurrentPattern(ScrollItemPattern.Pattern,out scroll))((ScrollItemPattern)scroll).ScrollIntoView();
  ((TogglePattern)e.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
 }
 public static void Close(AutomationElement root){
  object p;if(root.TryGetCurrentPattern(WindowPattern.Pattern,out p))((WindowPattern)p).Close();
 }
 public static void Focus(IntPtr window){AutomationElement.FromHandle(window).SetFocus();}
}
