# OutWit.Database.Native

NativeAOT shared library exposing the WitDatabase C ABI (`include/witdb.h`) for **pywitdb**.

## Build

```bash
dotnet publish Sources/Core/OutWit.Database.Native/OutWit.Database.Native.csproj -c Release -r win-x64
```

Artifact: `bin/Release/net9.0/win-x64/publish/witdb.dll`

## Consumer

- [AI-Guiders/pywitdb](https://github.com/AI-Guiders/pywitdb) — `ctypes` via `WITDB_NATIVE_PATH` or packaged `native/<rid>/`.
