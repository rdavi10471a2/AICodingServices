# CodingServices Launch Targets

Use this file for cheap launch and status orientation instead of rediscovering entry points each session.

## Main App

- Main app project: `C:\VSCodeProjects\CodingServices\src\CodexUI\CodexUI.csproj`
- Main runnable binary after build: `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll`
- Normal launch command: `dotnet C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll --urls http://localhost:5000`
- Local URL rule: CodexUI defaults to `http://localhost:5000/`. Only expect a different URL when an explicit `--urls` override or `ASPNETCORE_URLS` value is supplied.

## Normal Start

Use normal start when CodingServices is watching a non-CodingServices solution.

1. Keep or start CodexUI at `http://localhost:5000/`.
2. Start a new Codex thread in `C:\VSCodeProjects\CodingServices`.
3. Run `initialize_coding_services`.
4. Confirm `get_monitor_status` and `get_workflow_status`, including the intended watched solution.

## Self-Edit Or Tooling Rebuild

Use self-edit mode when CodingServices is watching/editing itself or after rebuilding/restarting CodexUI, MCP server, MCP bridge, workflow, hub, or tool-registration code.

### Stop Site

```powershell
$conns = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
foreach ($conn in $conns) {
    Stop-Process -Id $conn.OwningProcess -Force
}
```

### Rebuild Site With Local Cache

```powershell
dotnet build C:\VSCodeProjects\CodingServices\src\CodexUI\CodexUI.csproj --no-restore /nodeReuse:false
```

For full-solution validation while the live site or MCP bridge may lock normal output files, prefer isolated output under `runtime/`:

```powershell
dotnet build C:\VSCodeProjects\CodingServices\AICodingServices.slnx --no-restore /nodeReuse:false /p:OutDir=C:\VSCodeProjects\CodingServices\runtime\build-validation\manual\
```

### Restart Latest Site Binary

```powershell
Start-Process -FilePath dotnet -ArgumentList @(
    'C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.dll',
    '--urls',
    'http://localhost:5000'
) -WorkingDirectory 'C:\VSCodeProjects\CodingServices' -WindowStyle Hidden
```

### Fresh Thread Validation

After a tooling rebuild/restart, old Codex threads can keep stale mounted MCP transports. Start a fresh thread in `C:\VSCodeProjects\CodingServices`, then run:

1. `get_monitor_status`
2. `get_workflow_status`
3. `initialize_coding_services`

Expected healthy result: site reachable at `http://localhost:5000/`, active transport `native mounted MCP`, direct bridge fallback not needed, `staleFileCount: 0`, and `diagnosticCount: 0`.

## Workflow Infrastructure

- Watched solution: confirm through the live app or `get_monitor_status`; it may be this repo during self-edit dogfooding or an external watched target during normal work.
- MCP hub owner: CodexUI
- Bridge server name for clients: `aicodingservices`
- CodingServices MCP backend owns watched-source review, staging, and decision recording.
