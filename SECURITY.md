# Security Policy

MOSAIC is an early reference implementation. Do not use it as the sole security boundary for untrusted tool handlers or production agent execution.

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's security advisory feature for this repository. Include the affected version, a concise reproduction, and impact. Do not publish exploit details before maintainers have had a chance to assess and address the report.

## Scope and known limitations

The workflow plan is untrusted input and is validated, bounded, and authorized against host-owned tool definitions. Tool handlers execute in the host process and inherit its privileges. The library does not currently isolate handlers, enforce CPU or memory quotas, or provide a wall-clock timeout. Hosts must sandbox untrusted handlers separately and grant only the capabilities required for the task.
