# Windows UI ????

- Build: Release
- OS: Windows 11
- Dataset: 500 total / 100 active group
- Cache states: cold and warm
- Record: CPU, GPU, resolution, scale, transparency mode
- Markers: selection-ack, content-stable
- Report: sample count, P50, P95, P99, maximum, realized container count

## Baseline command

```powershell
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

## Required visual checks

1. Switch grid/list/card groups ten times each.
2. Record whether any icon appears outside its configured geometry on first frame.
3. Record trace samples without calling UpdateLayout or recursive item traversal.
