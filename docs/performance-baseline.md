# Tonarink performance baseline

Date: 2026-09-07

App version under test: 1.0.2

Reactor: 0.1.0-preview.14

.NET SDK: 10.0.400

This is a development-machine baseline, not a cross-device product requirement. Repeat the
same scenarios on lower-end and ARM64 hardware before assigning release thresholds.

## Test system

| Item | Value |
|---|---|
| OS | Windows 11 Pro 10.0.26200 |
| CPU | AMD Ryzen 5 7500F, 6 cores / 12 logical processors |
| Memory | 31.6 GiB |
| `dotnet-trace` | 10.0.731102 |
| PerfView | 3.2.6 |

All app measurements used x64 Release builds. The AOT measurements used the portable Native
AOT output. The normal JIT measurements used the self-contained Release output.

## Method

- Cold start: start the process and wait until the three primary navigation items are exposed
  through UI Automation. Seven starts per build; discard the first and summarize the remaining
  six.
- Idle: leave the receive page and its Lottie animation visible for 10 seconds after a two-second
  settling period.
- Normal navigation: cycle Receive, Send, and Settings with a 500 ms settling interval.
- Rapid navigation: cycle the same pages with a 16 ms interval, both as a traced 90-selection
  burst and as three consecutive 60-selection bursts in one process.
- Reactor timing: EventPipe provider `Microsoft-UI-Reactor:0x203:4`, covering reconcile, render,
  and navigation events.
- Native UI correlation: PerfView ETW providers `*Microsoft-UI-Reactor:0x203:4` and
  `Microsoft-Windows-XAML`, evaluated in 400 ms windows following each top-level reconcile.
- Static verification: `mur check --final` against the x64 Release configuration.

Native AOT disables EventSource support by default in the runtime targets. The traced AOT build
was therefore a diagnostics-only publish with `EventSourceSupport=true`. The production AOT
binary was used for startup, idle, and uninstrumented automation measurements. Do not enable
that switch in release builds solely for this workflow.

## Results

### Startup and idle

| Metric | AOT | JIT | Observation |
|---|---:|---:|---|
| Ready-to-use startup, mean | 471 ms | 1,035 ms | AOT is about 2.2x faster |
| Ready-to-use startup, p95 | 485 ms | 1,058 ms | Stable after first launch |
| Working set at startup, mean | 160 MiB | 203 MiB | AOT saves about 42 MiB |
| Idle CPU, whole-machine share | 0.82% | 1.09% | Includes the continuously playing receive-page Lottie |
| Idle working set, end | 163 MiB | 209 MiB | No material 10-second idle growth |
| Idle private bytes, end | 133 MiB | 134 MiB | Nearly identical committed private memory |

The normal app baseline does not show an AOT-wide slowdown. AOT starts faster and uses less
resident memory.

### Reactor work during normal navigation

| Span | AOT mean / p95 / max | JIT mean / p95 / max |
|---|---:|---:|
| Reconcile | 3.46 / 8.30 / 14.48 ms | 6.40 / 17.30 / 78.12 ms |
| Child reconcile | 1.86 / 7.05 / 14.41 ms | 3.47 / 7.88 / 77.97 ms |
| Component render | 0.04 / 0.16 / 0.60 ms | 0.08 / 0.20 / 0.30 ms |
| Effect flush | <0.01 ms | about 0.01 ms |

Both builds created at most 93 UI elements and modified at most 15 existing elements in a pass.
The slowest AOT reconcile remained below one 60 Hz frame budget. The JIT trace contained one
large child-reconcile outlier, but it did not reproduce as an AOT-specific issue.

The correlated XAML trace also did not show slower AOT layout. AOT `MeasureElement` durations
were 0.23 ms mean / 0.84 ms p95 / 5.07 ms max; JIT was 0.34 ms mean / 0.90 ms p95 with a
92.91 ms one-off maximum. `ArrangeElement` was 0.17 ms mean / 0.85 ms p95 for AOT and 0.20 ms
mean / 1.55 ms p95 for JIT.

Conclusion: the reported AOT navigation symptom is not caused by Tonarink component rendering,
effects, or ordinary WinUI measure/arrange work.

### Rapid navigation

At a 16 ms selection interval, AOT accepted navigation input more quickly:

| Metric, 90 selections | AOT | JIT |
|---|---:|---:|
| Selection dispatch p50 | 4.60 ms | 10.16 ms |
| Selection dispatch p95 | 53.36 ms | 106.51 ms |
| Reactor reconcile mean | 5.58 ms | 9.71 ms |
| Reactor reconcile p95 | 20.25 ms | 25.66 ms |

That advantage explains the counterintuitive visual result. The current host uses
`NavigationTransition.Slide()` with its default vertical WinUI specification. Reactor keeps the
incoming page at opacity zero until the 250 ms handoff point of the 600 ms transition. A second
navigation can begin while the previous incoming page is still transparent. AOT processes the
requests densely enough to expose this state; slower JIT dispatch unintentionally throttles the
requests and can look smoother.

Rapid navigation also exposed sustained memory growth in both runtimes:

| Consecutive 60-selection bursts | AOT working set | JIT working set |
|---|---:|---:|
| Before burst 1 | 165 MiB | 207 MiB |
| After burst 1 and 1.5 s settle | 256 MiB | 325 MiB |
| After burst 2 and 1.5 s settle | 308 MiB | 371 MiB |
| After burst 3 and 1.5 s settle | 369 MiB | 417 MiB |

This is not AOT-only behavior. WinUI 3 / Windows App SDK have known long-running resource
retention behavior around visual and composition resources, so working-set growth alone is not
evidence of a Tonarink or Reactor application leak. A conclusive attribution requires native
heap and retained-object analysis.

## Findings and priorities

1. **No general AOT regression was found.** Startup, idle memory, Reactor render, and XAML layout
   are all equal or better in AOT.
2. **The white content during very rapid switching is real and transition-related.** It is the
   250 ms opacity handoff being re-entered, not slow page construction.
3. **Rapid overlapping navigation increases process working set in both builds.** Treat this as
   a platform/framework observation unless native retained-object analysis identifies an
   application-owned root.
4. **Normal paced navigation is healthy.** No component render or effect hotspot currently
   justifies application-level memoization work.
5. `mur check --final` passes for x64 Release.

## Recommended next actions

1. Keep the standard Reactor navigation behavior. An experimental 300 ms navigation gate reduced
   synthetic stress work, but made real interaction feel less direct and was therefore reverted.
2. If the rapid-switch white interval matters in practice, test a simultaneous custom slide
   (for example a short explicit duration) as an alternative
   to the default delayed-opacity vertical slide. It can remove the white interval, but it does
   not diagnose working-set retention.
3. Add ARM64 and a lower-end Windows device to the release performance matrix.

## Reproduction tools

- `tools/Measure-StartupPerformance.ps1` measures ready-to-use startup and initial memory.
- `tools/Measure-NavigationPerformance.ps1` drives normal or rapid navigation and records
  latency, CPU, immediate memory, and post-settle memory.
- `tools/Analyze-ReactorTrace.cs` summarizes Reactor EventPipe spans and reconcile counters.
- `tools/Analyze-XamlNavigationTrace.ps1` summarizes relevant XAML events in per-navigation
  windows from a PerfView XML export.

Example normal-navigation collection:

```powershell
dotnet-trace collect --process-id <pid> `
  --providers Microsoft-UI-Reactor:0x203:4 `
  --output navigation.nettrace

.\tools\Measure-NavigationPerformance.ps1 `
  -ProcessId <pid> -WarmupRounds 3 -MeasuredRounds 20 `
  -SettleMilliseconds 500 -OutputPath navigation.json
```

Example rapid-navigation stress run:

```powershell
.\tools\Measure-NavigationPerformance.ps1 `
  -ProcessId <pid> -WarmupRounds 1 -MeasuredRounds 20 `
  -SettleMilliseconds 16 -PostRunSettleMilliseconds 1500 `
  -OutputPath rapid-navigation.json
```
