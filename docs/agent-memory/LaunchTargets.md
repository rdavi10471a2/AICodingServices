# CodingServices Launch Targets

Use this file for cheap launch and status orientation instead of rediscovering entry points each session.

## Main App

- Main app project: `C:\VSCodeProjects\CodingServices\src\CodexUI\CodexUI.csproj`
- Main runnable binary after build: `C:\VSCodeProjects\CodingServices\src\CodexUI\bin\Debug\net10.0\CodexUI.exe`
- Local URL rule: inspect the live `CodexUI` process for listening localhost ports instead of assuming a fixed port.

## Workflow Infrastructure

- Watched solution: confirm through the live app or `get_monitor_status`; it is an external watched target, not this repo's solution
- MCP hub owner: CodexUI
- Bridge server name for clients: `aicodingservices`
- CodingServices MCP backend owns watched-source review, staging, and decision recording.
