using Vortice.DXGI;
namespace SplitDeck.Windows;
internal readonly record struct PresentationPolicy(uint SyncInterval,PresentFlags Flags,SwapChainFlags CreationFlags)
{
 internal static PresentationPolicy Select(bool requested,bool supported)=>requested&&supported?new(0,PresentFlags.AllowTearing,SwapChainFlags.AllowTearing):new(1,PresentFlags.None,SwapChainFlags.None);
}
