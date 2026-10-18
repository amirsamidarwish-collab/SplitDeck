using SplitDeck.Windows;
using SplitDeck.Core;
using System.Xml.Linq;
if(args.Length==2&&args[0]=="--window-rescue-fixture")return await NativeWindowRescueTests.RunFixtureAsync(args[1]);
var tests=new List<(string,Func<Task>)>{("Safety regressions",SafetyTests.Run),
 ("Fresh install rollback retains ownership when new-device removal fails",FreshInstallTests.FailedRemovalKeepsOwnership),
 ("Fresh install rollback clears metadata only after confirmed removal",FreshInstallTests.RemovedDeviceClearsAttempt),
 ("CI headless classification retains offscreen graphics tests and excludes awake desktop tests",TestExecutionPolicy.Classification),
 ("Presentation policy requires capability and explicit request for immediate mode",PresentationPolicyTests.Selection),
 ("Controller recovery separates failed activation from confirmed restored desktop",ControllerRecoveryTests.ActivationErrorAfterRestore),
 ("Session topology keeps triple sources connected to physical presenter",SessionTopologyTests.ConnectedTriple),
 ("Session topology translates physical group without overlap or relative drift",SessionTopologyTests.PhysicalGroup),
 ("Native frame copy patches match full acquired image byte for byte",NativeFrameCopyTests.Equivalence),
 ("Frame copy patches current dirty and moved destination pixels",FrameCopyTests.PartialPlan),
 ("Frame copy preserves full image on startup recovery or invalid metadata",FrameCopyTests.Fallbacks),
 ("Frame copy bounds rectangle calls and coverage",FrameCopyTests.Bounds),
 ("Frame copy rejects failed truncated and misaligned metadata buffers",FrameCopyTests.MetadataBuffers),
 ("Diagnostic output refresh rejects rates outside active virtual and panel bounds",OutputRefreshTests.Bounds),
 ("Diagnostic output refresh stages selected panel only and preserves recovery Hz",OutputRefreshTests.IntentAndRestoration),
 ("Native GPU diagnostic query timer completes offscreen samples",NativeGpuDiagnosticTests.TimestampSmoke),
 ("GPU diagnostic queries distinguish pending success from ready data",GpuDiagnosticTests.PendingQueries),
 ("GPU diagnostic timestamps reject disjoint invalid intervals",GpuDiagnosticTests.TimingValidation),
 ("GPU diagnostic sampling stays bounded to one in sixteen",GpuDiagnosticTests.SamplingBound),
 ("GPU diagnostic dirty area clips metadata without overflow",GpuDiagnosticTests.DirtyArea),
 ("Three-screen driver ownership preserves all managed IDs and refuses extras",DriverCountTests.Ownership),
 ("Three-screen driver modes and attachment retain individual exact resolutions",DriverCountTests.ModesAndAttachment),
 ("Three-screen driver count upgrades preserve persisted ownership and reduction",DriverCountTests.Upgrade),
 ("Three-screen driver interrupted upgrades retain failed-removal ownership",DriverCountTests.UpgradeRollback),
 ("Three-screen driver activation enables requested count and disables retained extras",DriverCountTests.ActivationSelection),
 ("Three-screen driver primary routing preserves ordering and legacy requests",DriverCountTests.PrimaryAndLegacyRequest),
 ("Three-screen driver diagnostic profile overrides preserve saved settings",ProbeProfileTests.Selection),
 ("Window rescue maps existing apps into arbitrary work areas without workspace drift",WindowRescueTests.Geometry),
 ("Window rescue excludes shell helper hidden and tool windows",WindowRescueTests.Selection),
 ("Native existing-window rescue retains normal maximized minimized state and focus",NativeWindowRescueTests.ShowStates),
 ("Frame invalidation suppresses static work and retains bounded keepalive",FrameInvalidationTests.StaticFrames),
 ("Frame invalidation responds to desktop work and explicit inspection",FrameInvalidationTests.ContentAndInspection),
 ("Frame invalidation tracks visible cursor changes and query failures",FrameInvalidationTests.CursorChanges),
 ("Cursor boundary repairs released clips and escaped positions",CursorBoundaryGuardTests.ReleasedClip),
 ("Cursor boundary preserves narrower game clips",CursorBoundaryGuardTests.NarrowGameClip),
 ("Cursor boundary intersects partially overlapping clips",CursorBoundaryGuardTests.PartialClip),
 ("Cursor boundary replaces disjoint physical clips",CursorBoundaryGuardTests.DisjointClip),
 ("Cursor boundary projects the portrait height gap",CursorBoundaryGuardTests.PortraitGap),
 ("Cursor boundary avoids ordinary pointer writes",CursorBoundaryGuardTests.OrdinaryPointer),
 ("Cursor boundary skips failed input desktop queries",CursorBoundaryGuardTests.FailedQuery),
 ("Cursor boundary fails closed on native write errors",CursorBoundaryGuardTests.FailedWrite),
 ("Native GDI cursor bitmap buffer boundaries",NativeCursorGdiSafetyTests.BitmapInfoBounds),
 ("Native cursor query remains valid after raster",NativeCursorGdiSafetyTests.CursorInfoAfterRaster),
 ("Native real arrow remains visible across mixed-rate source screens",NativeArrowVisibilityTests.BothScreens),
 ("Interaction validation requires every completed target and phase",InteractionValidationTests.CompleteEvidence),
 ("Interaction validation rejects missing case-specific work",InteractionValidationTests.MissingWork),
 ("Cursor projection retains native hotspot and screen bounds",CursorPacingTests.CursorProjection),
 ("Native cursor composition preserves alpha and monochrome operations",NativeCursorTests.Composition),
 ("Native cursor shapes retain native dimensions hotspots and transparency",NativeCursorTests.RasterShapes),
 ("Cursor resources are reused for unchanged handles",CursorPacingTests.CursorReuse),
 ("Presentation deadline retains cadence without catch-up bursts",CursorPacingTests.PresentationDeadline),
 ("GPU duplication recovers bounded access loss without masking device failures",CaptureRecoveryTests.AccessLostPolicy),
 ("Driver disable attempts all instances and reports required restart",CaptureRecoveryTests.DisableRestart),
 ("Driver disable is idempotent for native disabled states",DriverDisableTests.AlreadyDisabled),
 ("Driver disable accepts disconnect only after verifying disabled state",DriverDisableTests.DisconnectedRace),
 ("Driver transition retries verified disconnected devices through class installer",DriverTransitionTests.DisconnectedFallback),
 ("Driver transition preserves trust failure missing device and restart boundaries",DriverTransitionTests.FailureBoundaries),
 ("Driver transition verifies requested state rather than utility success",DriverTransitionTests.StateVerification),
 ("Driver transition keeps already-disabled upgrade cleanup idempotent",DriverTransitionTests.DisabledIdempotence),
 ("Output attachment stages only detached owned adapters beside an existing display",OutputAttachmentTests.DetachedOwnedOutputs),
 ("Output attachment never guesses missing foreign or duplicate identities",OutputAttachmentTests.IdentityBoundaries),
 ("Partial recovery detaches only owned outputs and preserves external topology",CaptureRecoveryTests.PartialRestore),
 ("Editor readiness cached reads never inspect the driver",DriverReadinessTests.CachedReads),
 ("Editor readiness refresh shares one background probe",DriverReadinessTests.SingleFlight),
 ("Editor readiness invalidation rejects stale results",DriverReadinessTests.Invalidation),
 ("Editor readiness failures remain unavailable",DriverReadinessTests.Errors),
 ("Managed driver pair parses legacy ownership and rejects foreign devices",DriverPairTests.Ownership),
 ("Managed driver pair rolls back only new devices and retains failed-removal ownership",DriverPairTests.Upgrade),
 ("Setup retry requires both owned devices to finish disabling",async()=>{int calls=0;var state=await DriverInstallation.WaitForHealthAsync(()=>new DriverHealthState(true,++calls==1?0:22,"test"),TimeSpan.FromSeconds(1),requireDisabled:true);Check(calls==2&&state.ProblemCode==22);}),
 ("Bound driver package is resolved directly for supplied device without WMI inventory",()=>{string? id=Environment.GetEnvironmentVariable("SPLITDECK_TEST_DEVICE_ID");if(id is not null){var binding=DriverInstallation.ReadBoundDriver(id);Check(binding.Provider=="MikeTheTech"&&binding.Version=="11.30.4.434");DriverVerification.VerifyPackage(DriverVerification.DriverStoreFolder(binding.Inf));Check(DriverInstallation.InspectHealth(id).Ready);}return Task.CompletedTask;}),
 ("Canceled startup retains a wait for actual helper exit",CapturePerformanceTests.RecoverySettlement),
 ("Performance telemetry remains outside recovery journal discovery",CapturePerformanceTests.TelemetryJournalIsolation),
 ("CPU capture fallback bounds aggregate bandwidth and skips pointer-only updates",CapturePerformanceTests.Budget),
 ("Mixed-rate GPU cadence retains requested rate across presentation ticks",CapturePerformanceTests.Cadence),
 ("Resource trial normalizes CPU usage and aborts excessive hardware overhead",CapturePerformanceTests.ResourceGuard),
 ("Native GPU capture reproduces a known desktop marker pixel",CapturePerformanceTests.NativeGpu),
 ("Native nonblocking fallback produces an image within the shared readback budget",CapturePerformanceTests.NativeFallback),
 ("Native compositor retains its draw target after successive flip presents",PresentationTests.Run),
 ("Direct Apply stays active beyond the former timeout without confirmation and retains emergency restore",PresentationTests.DirectApply),
 ("Pinned signed driver package verifies through Windows trust without WMI signature inventory",()=>{
  DriverVerification.VerifyPackage(Path.Combine(AppContext.BaseDirectory,"driver"));return Task.CompletedTask;}),
 ("Tampered or unsigned files fail direct package verification",()=>{
  string folder=Path.Combine(Path.GetTempPath(),"SplitDeck-verification-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try{
   foreach(string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"driver")))File.Copy(path,Path.Combine(folder,Path.GetFileName(path)));
   File.AppendAllText(Path.Combine(folder,"MttVDD.dll"),"tampered");
   try{DriverVerification.VerifyPackage(folder);throw new Exception("Expected integrity failure");}catch(InvalidOperationException){}
   string unsigned=Path.Combine(folder,"unsigned.dll");File.WriteAllText(unsigned,"unsigned content");Check(!DriverVerification.IsTrusted(unsigned));
  }finally{Directory.Delete(folder,true);}return Task.CompletedTask;}),
 ("Device health keeps signature enforcement failures and pending restarts distinct",()=>{
  Check(new DriverHealthState(true,0,"ready").Ready);Check(new DriverHealthState(true,22,"disabled").Ready);
  Check(!new DriverHealthState(false,0,"unverified package").Ready);
  Check(!new DriverHealthState(true,52,"Windows cannot verify the signature").Ready);
  Check(!new DriverHealthState(true,null,"device missing").Ready);
  var pending=new DriverHealthState(true,14,"restart");Check(!pending.Ready&&pending.RestartRequired);return Task.CompletedTask;}),
 ("Health probe waits for enumeration but never accepts signature failure",async()=>{
  int attempts=0;var ready=await DriverInstallation.WaitForHealthAsync(()=>++attempts==1?new(false,null,"not enumerated"):new(true,22,"disabled"),TimeSpan.FromSeconds(2));Check(attempts==2&&ready.Ready);
  var blocked=await DriverInstallation.WaitForHealthAsync(()=>new(true,52,"signature blocked"),TimeSpan.Zero);Check(!blocked.Ready&&blocked.ProblemCode==52);
  var missing=DriverInstallation.InspectHealth("ROOT\\SPLITDECK_TEST_MISSING\\0000");Check(!missing.Ready);
 }),
 ("Published INF resolves the pinned installed package through SetupAPI when supplied",()=>{
  string? inf=Environment.GetEnvironmentVariable("SPLITDECK_TEST_PUBLISHED_INF");
  if(inf is not null)DriverVerification.VerifyPackage(DriverVerification.DriverStoreFolder(inf));
  try{DriverVerification.DriverStoreFolder("..\\unsafe.inf");throw new Exception("Expected INF validation");}catch(InvalidOperationException){}
  return Task.CompletedTask;}),
 ("Editor elevation requests visible UAC and preserves the edited layout",()=>{
  var profile=Presets.Gaming(144,true) with{FirstShare=75};
  var request=new EditorLaunchRequest(profile,"\\\\.\\DISPLAY1","setup");
  var start=EditorElevation.CreateRestartInfo("C:\\A folder\\SplitDeck.exe",null,request);
  Check(start.UseShellExecute&&start.Verb=="runas"&&start.WindowStyle==System.Diagnostics.ProcessWindowStyle.Normal);
  Check(start.FileName=="C:\\A folder\\SplitDeck.exe"&&start.ArgumentList.Count==2&&start.ArgumentList[0]=="--elevated-editor");
  Check(EditorElevation.Decode(start.ArgumentList[1])==request);
  var sdkStart=EditorElevation.CreateRestartInfo("C:\\SDK folder\\dotnet.exe","C:\\App folder\\SplitDeck.dll",request);
  Check(sdkStart.ArgumentList[0]=="C:\\App folder\\SplitDeck.dll"&&sdkStart.ArgumentList[1]=="--elevated-editor");return Task.CompletedTask;}),
 ("Elevation restart rejects invalid action and oversized payload",()=>{
  try{EditorElevation.CreateRestartInfo("app.exe",null,new(Presets.Balanced(60),null,"untrusted"));throw new Exception("Expected validation");}catch(ArgumentException){}
  try{EditorElevation.Decode(new string('A',32769));throw new Exception("Expected payload limit");}catch(ArgumentException){}
  try{EditorElevation.Decode("not base64!");throw new Exception("Expected decode error");}catch(ArgumentException){}return Task.CompletedTask;}),
 ("Recovery reverses completed steps once",async()=>{var log=new List<int>();var r=new RecoveryStack();r.Push(()=>{log.Add(1);return Task.CompletedTask;});r.Push(()=>{log.Add(2);return Task.CompletedTask;});await r.RevertAsync();await r.RevertAsync();Check(log.SequenceEqual(new[]{2,1}));}),
 ("A failed cleanup does not skip remaining recovery",async()=>{var log=new List<int>();var r=new RecoveryStack();r.Push(()=>{log.Add(1);return Task.CompletedTask;});r.Push(()=>throw new IOException("failure"));try{await r.RevertAsync();throw new Exception("Expected aggregate");}catch(AggregateException){}Check(log.SequenceEqual(new[]{1}));}),
 ("Driver XML exposes one monitor per instance with both exact modes and rates",()=>{var l=LayoutEngine.Calculate(Presets.Gaming(143,true),2560,1080,143);var x=DriverConfiguration.Build(l);Check(x.Root!.Element("monitors")!.Element("count")!.Value=="1");var modes=x.Descendants("resolution").ToArray();Check(modes.Length==2);Check(modes[0].Element("width")!.Value=="1792");Check(modes[0].Element("refresh_rate")!.Value=="143");Check(modes[1].Element("height")!.Value=="1072");Check(x.Descendants("HardwareCursor").Single().Value=="true");return Task.CompletedTask;}),
 ("Malformed mode requests rejected",()=>{try{DriverConfiguration.Build([]);throw new Exception("Expected validation");}catch(ArgumentException){}return Task.CompletedTask;})
};
if(args.Length==2&&args[0]=="--filter")tests=tests.Where(x=>x.Item1.Contains(args[1],StringComparison.OrdinalIgnoreCase)).ToList();
if(args.Length==1&&args[0]=="--headless"){
 var unavailable=tests.Where(x=>TestExecutionPolicy.RequiresInteractiveDisplay(x.Item2)||TestExecutionPolicy.RequiresHardwareAdapter(x.Item2)).ToArray();
 foreach(var test in unavailable){Console.WriteLine("EXCLUDED ("+(TestExecutionPolicy.RequiresHardwareAdapter(test.Item2)?"requires a hardware Direct3D adapter":"requires an awake interactive desktop")+"): "+test.Item1);tests.Remove(test);}
}
if(args.Length==1&&args[0]=="--without-display"){
 var interactive=tests.Where(x=>x.Item1.StartsWith("Native ",StringComparison.Ordinal)||x.Item1.StartsWith("Direct Apply ",StringComparison.Ordinal)).ToArray();
 foreach(var test in interactive){Console.WriteLine("EXCLUDED (requires an awake interactive display): "+test.Item1);tests.Remove(test);}
}
int failed=0;foreach(var(name,test)in tests){try{await test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}Console.WriteLine($"{tests.Count-failed}/{tests.Count} passed");return failed>0?1:0;
static void Check(bool condition){if(!condition)throw new Exception("Assertion failed");}


