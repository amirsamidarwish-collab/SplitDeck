namespace SplitDeck.Windows;

internal static class DriverPairUpgrade
{
 internal static async Task RunAsync(IReadOnlyList<string> existing,int requestedCount,Func<string> create,Action<string[]> persist,Func<Task> bind,Func<string,Task> disable,Func<string,Task> remove)
 {
  if(existing.Count is <1 or >3||requestedCount is <2 or >3||existing.Any(string.IsNullOrWhiteSpace)||existing.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=existing.Count)throw new ArgumentException("One to three distinct owned instances and a two- or three-screen target are required.");
  var original=existing.ToArray();var added=new List<string>();
  try{
   while(original.Length+added.Count<requestedCount){
    string next=create();if(string.IsNullOrWhiteSpace(next)||original.Concat(added).Contains(next,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Windows returned a duplicate or invalid new driver instance.");
    added.Add(next);persist([..original,..added]);await bind();
   }
   var errors=new List<Exception>();foreach(string id in original.Concat(added))try{await disable(id);}catch(Exception e){errors.Add(e);}if(errors.Count>0)throw new AggregateException("Driver setup could not disable every managed instance.",errors);
  }catch(Exception failure){
   var errors=new List<Exception>();var retained=new HashSet<string>(added,StringComparer.OrdinalIgnoreCase);
   foreach(string id in added.AsEnumerable().Reverse())try{await remove(id);retained.Remove(id);}catch(Exception e){errors.Add(e);}
   string[] remaining=[..original,..added.Where(retained.Contains)];
   try{persist(remaining);}catch(Exception e){errors.Add(e);}
   foreach(string id in remaining)try{await disable(id);}catch(Exception e){errors.Add(e);}
   if(errors.Count>0)throw new AggregateException("Driver setup failed; retained owned device IDs require cleanup before retry.",new[]{failure}.Concat(errors));
   throw;
  }
 }
 internal static Task RunAsync(string first,Func<string> create,Action<string[]> persist,Func<Task> bind,Func<string,Task> disable,Func<string,Task> remove)=>RunAsync([first],2,create,persist,bind,disable,remove);
}
