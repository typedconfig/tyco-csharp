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

## Usage

Install the library into another solution (from the repo root):

```bash
dotnet add <YourProject>.csproj reference tyco-csharp/Tyco.CSharp/Tyco.CSharp.csproj
```

Sample code:

```csharp
using Tyco.CSharp;

var context = TycoParser.Load("../tyco-test-suite/inputs/simple1.tyco");
var json = context.ToJson(); // returns a JsonObject mirroring the canonical output
Console.WriteLine(json["project"]); // prints "demo"
```

You can also parse from a string with `TycoParser.LoadString(content)`.

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
