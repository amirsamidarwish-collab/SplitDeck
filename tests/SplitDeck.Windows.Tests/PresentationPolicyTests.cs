using SplitDeck.Windows;
using Vortice.DXGI;
internal static class PresentationPolicyTests
{
 internal static Task Selection(){
  foreach(var pair in new[]{(false,false),(false,true),(true,false)}){var p=PresentationPolicy.Select(pair.Item1,pair.Item2);if(p.SyncInterval!=1||p.Flags!=PresentFlags.None||p.CreationFlags!=SwapChainFlags.None)throw new Exception("Default/unsupported must preserve vsync without tearing flags.");}
  var immediate=PresentationPolicy.Select(true,true);if(immediate.SyncInterval!=0||immediate.Flags!=PresentFlags.AllowTearing||immediate.CreationFlags!=SwapChainFlags.AllowTearing)throw new Exception("Diagnostic immediate present must require support and pair creation/present flags.");return Task.CompletedTask;
 }
}
