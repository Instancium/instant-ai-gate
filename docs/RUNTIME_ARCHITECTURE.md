# InstantAIGate Runtime Architecture

## Overview
InstantAIGate utilizes a NuGet package-based runtime delivery system combined with dynamic, on-the-fly hardware detection and extraction. This ensures a zero-configuration experience for end-users while seamlessly supporting both CPU and CUDA backends.

---

## Architecture Details

### NuGet Distribution (Current)
Native binaries are distributed via OS-aggregated NuGet packages:
* `InstantAIGate.Runtimes.Windows`
* `InstantAIGate.Runtimes.Linux`

### Build Process
1. `dotnet restore` pulls the runtime packages based on the target OS platform.
2. The packages contain embedded `.7z` resources (e.g., `windows-x64.7z` or `linux-x64.7z`) containing the native C/C++ libraries.

### Runtime Initialization Lifecycle
1. **Environment Detection (`DriverEnvironmentDetector`)**: Checks OS and hardware compatibility. Dynamically detects CUDA availability by probing for OS-level drivers (`nvcuda.dll` or `libcuda.so.1`) without prematurely loading inference libraries.
2. **Extraction (`DriverExtractor`)**: Uses cross-process synchronization via a global Mutex to safely extract the embedded `.7z` archive. The files are routed to `runtimes/{os}/{cpu|cuda}` within the base directory or the OS temp folder.
3. **Native Resolution (`DriverNativeResolver`)**: Intercepts `DllImport` calls for libraries containing `"llama"` or `"ggml"` and routes them to the physically extracted absolute paths.

---

## Execution Modes

### 1. Consumer Mode (Production)
In the `InstantAIGate.Infrastructure.csproj` file, packages are referenced using conditions: `InstantAIGate.Runtimes.Windows` is included for the Windows platform, and `InstantAIGate.Runtimes.Linux` is included for the Linux platform.
* No manual infrastructure setup is required by the end-user.
* `DriverExtractor` unpacks binaries automatically on the first application startup, utilizing a `.instantaigate-version` marker file to cache the extraction.

### 2. Zero-Config Local Debug (Development)
Supported natively via `DriverEnvironmentDetector.GetLocalRuntimesPath()`.
* If a `.runtimes` folder is detected traversing upwards from the application base directory, or if the `INSTANTAI_RUNTIMES_PATH` environment variable is set, the extraction pipeline is completely bypassed.
* Used by maintainers for rapid local testing without needing to repackage or restore NuGet packages.

---

## Deployment Scenarios

### Docker Deployment (Recommended)
* The `Dockerfile` requires no manual runtime download logic (legacy `curl` commands are deprecated).
* `.dockerignore` excludes the local `.runtimes/` folder to minimize build context size.
* Native binaries are automatically bundled via the NuGet restore process and extracted inside the container upon execution.

---

## Troubleshooting

### Issue: `System.DllNotFoundException` (Unable to load DLL 'mtmd' or 'llama')
**Solution:**
* Ensure the target host operates on an **x64 architecture**.
* Verify that `DriverExtractor` successfully unpacked the binaries into `runtimes/{os}/cuda` or `runtimes/{os}/cpu`.
* Ensure that the OS has the required **Visual C++ Redistributable** (Windows) or **standard C libraries** (Linux) installed.

### Issue: Application hangs on startup during DLL load
**Solution:**
* The `DriverNativeResolver` will block threads up to **60 seconds** if the application attempts to load native APIs before the background extraction finishes. 
* Check logs for `[DriverNativeResolver] API is trying to load...` early to diagnose initialization race conditions.