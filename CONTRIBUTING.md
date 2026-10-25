# Contributing to Toucan

Small, focused contributions are welcome. Follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Issues

Search existing issues first. For bugs, include your Toucan version, operating system, reproduction steps and expected versus actual behavior. Use a small sample project and remove credentials and private data.

Discuss larger changes in an issue before implementing them. Report vulnerabilities privately using the [security policy](SECURITY.md).

## Development

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet build Toucan.CrossPlatform.slnx -c Release
dotnet test Toucan.CrossPlatform.slnx -c Release --no-build
```

Follow nearby code. Keep UI code in `Toucan.Avalonia`, shared logic in Core and format/provider integrations in the relevant module. Add regression tests for behavior changes and update documentation when needed. Packaging instructions are in [packaging/README.md](packaging/README.md).

## Pull Requests

Branch from the active development branch named in the README and target that same branch. Keep each PR focused; describe the change, link related issues and report what you tested. Include screenshots for visible UI changes.

Do not commit build outputs, credentials or private project data. CI must pass before merging.
