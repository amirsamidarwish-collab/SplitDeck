namespace SplitDeck.Windows;
internal sealed record CaptureFlowSnapshot(long Attempts,long Timeouts,long PointerOnly,long CadenceSkips,long Images,double AverageAgeMs,double MaxAgeMs,double AverageImageIntervalMs,double MaxImageIntervalMs,double AverageSourceIntervalMs,double MaxSourceIntervalMs);
internal sealed class CaptureFlow(long frequency)
{
 private readonly double toMilliseconds=frequency>0?1000d/frequency:throw new ArgumentOutOfRangeException(nameof(frequency));
 private long attempts,timeouts,pointerOnly,skips,images,ages,ageSamples,maxAge,intervals,intervalSamples,maxInterval,previousImage,previousPresent,sourceIntervals,sourceSamples,maxSourceInterval;
 internal void Attempt()=>Interlocked.Increment(ref attempts);
 internal void Timeout()=>Interlocked.Increment(ref timeouts);
 internal void Pointer()=>Interlocked.Increment(ref pointerOnly);
 internal void Skip()=>Interlocked.Increment(ref skips);
 internal void Image(long now,long present){if(present>0){if(previousPresent>0&&present>previousPresent){long delta=present-previousPresent;Interlocked.Add(ref sourceIntervals,delta);Interlocked.Increment(ref sourceSamples);if(delta>maxSourceInterval)Interlocked.Exchange(ref maxSourceInterval,delta);}previousPresent=present;long age=Math.Max(0,now-present);Interlocked.Add(ref ages,age);Interlocked.Increment(ref ageSamples);if(age>maxAge)Interlocked.Exchange(ref maxAge,age);}if(previousImage>0){long interval=Math.Max(0,now-previousImage);Interlocked.Add(ref intervals,interval);Interlocked.Increment(ref intervalSamples);if(interval>maxInterval)Interlocked.Exchange(ref maxInterval,interval);}previousImage=now;Interlocked.Increment(ref images);}
 internal CaptureFlowSnapshot Snapshot=>new(Interlocked.Read(ref attempts),Interlocked.Read(ref timeouts),Interlocked.Read(ref pointerOnly),Interlocked.Read(ref skips),Interlocked.Read(ref images),Interlocked.Read(ref ages)*toMilliseconds/Math.Max(1,Interlocked.Read(ref ageSamples)),Interlocked.Read(ref maxAge)*toMilliseconds,Interlocked.Read(ref intervals)*toMilliseconds/Math.Max(1,Interlocked.Read(ref intervalSamples)),Interlocked.Read(ref maxInterval)*toMilliseconds,Interlocked.Read(ref sourceIntervals)*toMilliseconds/Math.Max(1,Interlocked.Read(ref sourceSamples)),Interlocked.Read(ref maxSourceInterval)*toMilliseconds);
 internal void ResetInterval(){previousImage=0;previousPresent=0;}
}
