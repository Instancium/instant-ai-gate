# Runtime Package Migration - Version 1.0.9

## Summary
Fixed runtime package delivery mechanism by adding MSBuild `.props` files that automatically copy `.7z` archives to consumer publish output.

## Changes in 1.0.9

### All Runtime Packages
1. **Added MSBuild integration** via `build/*.props` files
   - `build/InstantAIGate.Runtimes.{rid}.{backend}.props`
   - Automatically copies `.7z` archives to consumer `runtimes/{rid}/{backend}/` during publish
   - Uses `CopyToOutputDirectory="PreserveNewest"` to ensure files are present

2. **Package structure**
   ```
   InstantAIGate.Runtimes.{rid}.{backend}.1.0.9.nupkg
   ├── .runtimes/{rid}/{backend}/
   │   └── {backend}.7z
   ├── build/
   │   └── InstantAIGate.Runtimes.{rid}.{backend}.props
   ├── README.md
   └── *.nuspec
   ```

3. **Updated `.csproj` files**
   - Added `<ContentTargetFolders>contentFiles</ContentTargetFolders>` where needed
   - Changed `PackagePath` from `runtimes/...` to `.runtimes/...`
   - Added `build/*.props` file to package

### Files Modified
- `src/Runtimes/InstantAIGate.Runtimes.win-x64.Cpu/InstantAIGate.Runtimes.win-x64.Cpu.csproj`
- `src/Runtimes/InstantAIGate.Runtimes.win-x64.Cuda/InstantAIGate.Runtimes.win-x64.Cuda.csproj`
- `src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cpu/InstantAIGate.Runtimes.linux-x64.Cpu.csproj`
- `src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cuda/InstantAIGate.Runtimes.linux-x64.Cuda.csproj`

### Files Created
- `src/Runtimes/InstantAIGate.Runtimes.win-x64.Cpu/build/InstantAIGate.Runtimes.win-x64.Cpu.props`
- `src/Runtimes/InstantAIGate.Runtimes.win-x64.Cuda/build/InstantAIGate.Runtimes.win-x64.Cuda.props`
- `src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cpu/build/InstantAIGate.Runtimes.linux-x64.Cpu.props`
- `src/Runtimes/InstantAIGate.Runtimes.linux-x64.Cuda/build/InstantAIGate.Runtimes.linux-x64.Cuda.props`

## Verification Steps

### Local Testing (✅ Completed)
1. Build packages: `.\scripts\Publish-RuntimePackages.ps1 -Source local -OutputDirectory nuget-test-v2`
2. Add local source: `dotnet nuget add source "$PWD\nuget-test-v2" -n TestRuntimeSource`
3. Update consumer to 1.0.9 in `src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj`
4. Publish API: `dotnet publish src/InstantAIGate.API/InstantAIGate.API.csproj -c Release -o test-publish-output`
5. Verify files exist:
   - `test-publish-output/runtimes/win-x64/cpu/cpu.7z` ✅
   - `test-publish-output/runtimes/win-x64/cuda/cuda.7z` ✅

### Docker Testing (Pending publish to nuget.org)
1. Version 1.0.7 does not have `.props` files → runtime assets not copied
2. Once 1.0.9 is published to nuget.org:
   - Update `RuntimePackageVersion` to 1.0.9
   - Run `.\test-docker-build.ps1`
   - Should find runtime `.7z` files in `/app/publish/api/runtimes/linux-x64/*/`

## Publication Plan

### Prerequisites
- [x] Runtime binaries present in `.runtimes/` folder
- [x] All 4 runtime package projects updated to version 1.0.9
- [x] Local testing confirms `.7z` files copied to publish output
- [ ] Update `RuntimePackageVersion` in `src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj` to 1.0.9 **AFTER** packages are published

### Publish Command
```powershell
.\scripts\Publish-RuntimePackages.ps1 -Source nuget -ApiKey 'YOUR_NUGET_API_KEY'
```

This will:
1. Compress runtime binaries (if not already compressed)
2. Build all 4 packages
3. Push to nuget.org:
   - `InstantAIGate.Runtimes.win-x64.Cpu.1.0.9.nupkg` (7.92 MB)
   - `InstantAIGate.Runtimes.win-x64.Cuda.1.0.9.nupkg` (87.19 MB)
   - `InstantAIGate.Runtimes.linux-x64.Cpu.1.0.9.nupkg` (7.01 MB)
   - `InstantAIGate.Runtimes.linux-x64.Cuda.1.0.9.nupkg` (184.6 MB)

### Post-Publication
1. Wait 5-10 minutes for NuGet.org index update
2. Update consumer reference: `<RuntimePackageVersion>1.0.9</RuntimePackageVersion>`
3. Re-run Docker test: `.\test-docker-build.ps1`
4. Commit changes with message:
   ```
   feat(runtimes): add MSBuild props for automatic asset delivery

   - Added build/*.props files to copy .7z archives to consumer output
   - Updated all runtime package projects to version 1.0.9
   - Fixed Docker builds missing runtime assets
   - Verified local publish includes cpu.7z and cuda.7z
   ```

## Rollback Plan
If 1.0.9 has issues:
1. Revert `RuntimePackageVersion` back to 1.0.7
2. Investigate and fix
3. Publish as 1.0.10

## Breaking Changes
None - this is backward compatible:
- Old consumers using 1.0.7 continue working (but don't get automatic copy)
- New consumers using 1.0.9+ get automatic runtime delivery

## Known Limitations
- Version 1.0.7 and earlier do **not** automatically copy runtime files
- Manual extraction from NuGet cache required for older versions
- Docker builds require 1.0.9+ for automatic runtime delivery
