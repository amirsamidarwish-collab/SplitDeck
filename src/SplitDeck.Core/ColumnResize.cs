namespace SplitDeck.Core;
public static class ColumnResize
{
 public static LayoutProfile Move(LayoutProfile profile,int divider,double percentageDelta)
 {
  LayoutEngine.ValidateProfile(profile);
  if(profile.Third is null||divider is <0 or >1||!double.IsFinite(percentageDelta))throw new ArgumentException("A three-screen layout, divider0 or1 and finite movement are required.");
  if(profile.Screens.Any(s=>s.VisibleWidth>0||s.VisibleHeight>0))throw new ArgumentException("Set exact visible dimensions to zero to drag automatic columns.");
  int change=(int)Math.Round(Math.Clamp(percentageDelta,-100,100),MidpointRounding.AwayFromZero);
  int[] shares=[profile.FirstShare,profile.SecondShare,100-profile.FirstShare-profile.SecondShare];
  change=Math.Clamp(change,1-shares[divider],shares[divider+1]-1);
  shares[divider]+=change;shares[divider+1]-=change;
  return profile with{FirstShare=shares[0],SecondShare=shares[1],Priority=false};
 }
}
