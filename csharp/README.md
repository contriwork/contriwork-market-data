# Contriwork.PackageName (.NET)

.NET adapter for the ContriWork **PackageName** port. One API surface, three languages (Python / .NET / npm) — this package is the .NET implementation.

Cross-language specification, contract, and release history live in the
[GitHub repository](https://github.com/contriwork/contriwork-PACKAGE_NAME):

- [Root README](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/README.md) — ecosystem overview
- [`CONTRACT.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CONTRACT.md) — language-agnostic port spec
- [`CHANGELOG.md`](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/CHANGELOG.md)

Sister packages: [`contriwork-PACKAGE_NAME`](https://pypi.org/project/contriwork-PACKAGE_NAME/) (PyPI), [`@contriwork/PACKAGE_NAME`](https://www.npmjs.com/package/@contriwork/PACKAGE_NAME) (npm).

## Install

```bash
dotnet add package Contriwork.PackageName
```

Targets **.NET 10 LTS**.

## Quick start

```csharp
using Contriwork.PackageName;

// TODO: one-line example once the port has real methods.
```

## Local development

```bash
dotnet restore
dotnet build
dotnet test
dotnet format --verify-no-changes
```

## License

MIT — see [LICENSE](https://github.com/contriwork/contriwork-PACKAGE_NAME/blob/main/LICENSE).
