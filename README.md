# Tyco C# Binding

This project provides a .NET implementation of the Tyco configuration language parser.  
It mirrors the canonical Python implementation and is validated against the shared
[`tyco-test-suite`](../tyco-test-suite).

## Requirements

- .NET SDK 8.0+
- The `tyco-test-suite` directory located alongside `tyco-csharp` (already present in this repo)

## Layout

```
tyco-csharp/
├── Tyco.CSharp/         # Parser library with TycoParser, context, values, utilities
├── Tyco.CSharp.Tests/   # Golden tests wired to ../tyco-test-suite
└── tyco-csharp.sln      # Solution that ties the projects together
```


## Quick Start

This package includes a ready-to-use example Tyco file at:

	example.tyco

([View on GitHub](https://github.com/typedconfig/tyco-csharp/blob/main/example.tyco))

You can load and parse this file using the C# Tyco API. Example usage:

```csharp
using Tyco.CSharp;

var context = TycoParser.Load("example.tyco");
var globals = context.Globals;
var environment = globals["environment"];
var debug = globals["debug"];
var timeout = globals["timeout"];
Console.WriteLine($"env={environment} debug={debug} timeout={timeout}");
// ... access objects, etc ...
```

See the [example.tyco](https://github.com/typedconfig/tyco-csharp/blob/main/example.tyco) file for the full configuration example.

## Development

Run the shared golden suite:

```bash
cd tyco-csharp
dotnet test
```

The test project iterates over each `.tyco` file in `../tyco-test-suite/inputs` and compares the
resulting JSON with the corresponding file under `../tyco-test-suite/expected`. A failure indicates
a behaviour drift relative to the canonical parser.

Please run `dotnet format` or ensure `dotnet build`/`dotnet test` succeeds before submitting changes.
