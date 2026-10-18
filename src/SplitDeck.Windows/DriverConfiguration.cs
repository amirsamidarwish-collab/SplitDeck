using System.Xml.Linq;
using SplitDeck.Core;
namespace SplitDeck.Windows;
public static class DriverConfiguration
{
 public static XDocument Build(IReadOnlyList<VirtualScreenLayout> layouts)
 {
  if(layouts.Count is not(2 or 3)||layouts.Any(x=>x.RenderWidth<1||x.RenderHeight<1||x.RenderWidth>8192||x.RenderHeight>8192||x.RefreshHz<1||x.RefreshHz>1000))throw new ArgumentException("Two or three valid display modes are required.");
  return new XDocument(new XElement("vdd_settings",new XElement("monitors",new XElement("count",1)),new XElement("gpu",new XElement("friendlyname","default")),new XElement("global",layouts.Select(x=>x.RefreshHz).Distinct().Select(x=>new XElement("g_refresh_rate",x))),new XElement("resolutions",layouts.Select(x=>(x.RenderWidth,x.RenderHeight,x.RefreshHz)).Distinct().Select(x=>new XElement("resolution",new XElement("width",x.RenderWidth),new XElement("height",x.RenderHeight),new XElement("refresh_rate",x.RefreshHz)))),new XElement("options",new XElement("CustomEdid",false),new XElement("HardwareCursor",true),new XElement("SDR10bit",false),new XElement("HDRPlus",false),new XElement("logging",false),new XElement("debuglogging",false))));
 }
}
