internal static class TestExecutionPolicy
{
 private static readonly HashSet<Func<Task>> interactive=[NativeWindowRescueTests.ShowStates,NativeCursorGdiSafetyTests.CursorInfoAfterRaster,CapturePerformanceTests.NativeGpu,CapturePerformanceTests.NativeFallback,PresentationTests.Run,PresentationTests.DirectApply];
 internal static bool RequiresInteractiveDisplay(Func<Task> test)=>interactive.Contains(test);
 internal static bool RequiresHardwareAdapter(Func<Task> test)=>test==(Func<Task>)NativeGpuDiagnosticTests.TimestampSmoke;
 internal static Task Classification()
 {
  foreach(Func<Task> test in new Func<Task>[]{NativeWindowRescueTests.ShowStates,NativeCursorGdiSafetyTests.CursorInfoAfterRaster,CapturePerformanceTests.NativeGpu,CapturePerformanceTests.NativeFallback,PresentationTests.Run,PresentationTests.DirectApply})if(!RequiresInteractiveDisplay(test))throw new Exception("An awake-only capture/window/input test entered the headless suite: "+test.Method.Name);
  foreach(Func<Task> test in new Func<Task>[]{NativeFrameCopyTests.Equivalence,NativeCursorTests.Composition,NativeCursorTests.RasterShapes,NativeArrowVisibilityTests.BothScreens,NativeCursorGdiSafetyTests.BitmapInfoBounds})if(RequiresInteractiveDisplay(test)||RequiresHardwareAdapter(test))throw new Exception("A useful software/offscreen native test was excluded from headless verification: "+test.Method.Name);
  if(!RequiresHardwareAdapter(NativeGpuDiagnosticTests.TimestampSmoke))throw new Exception("Hardware timestamp diagnostics must not enter CI that only guarantees software graphics.");
  return Task.CompletedTask;
 }
}
