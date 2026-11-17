# Contributing

Contributions are welcome. Keep changes focused, document security behavior, and add tests for workflow validation and authorization changes.

## Development

- Use the .NET 11 SDK (with the .NET 10 runtime) and the `MOSAIC.slnx` solution.
- Keep nullable reference types enabled and treat compiler warnings as errors.
- Use file-scoped namespaces, descriptive names, and XML documentation for public APIs.
- Do not add generated-code execution, implicit permissions, or handlers that perform hidden network or file-system access.
- Never commit credentials, private datasets, or sensitive workflow payloads.

Before opening a pull request, run `dotnet test MOSAIC.slnx --configuration Release` on a machine with the .NET 11 SDK and .NET 10 runtime, so tests run on both target frameworks. GitHub Actions provides the hosted validation path for contributors who do not install the SDK locally.
