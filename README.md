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

All bindings ship the same canonical sample configuration under `tyco/example.tyco`
([view on GitHub](https://github.com/typedconfig/tyco-csharp/blob/main/tyco/example.tyco)).
Load it to explore globals, structs, and references exactly like in the Python README:

```csharp
using System;
using System.Text.Json.Nodes;
using Tyco.CSharp;

var context = TycoParser.Load("tyco/example.tyco");
JsonObject document = context.ToJson();

var environment = document["environment"]?.GetValue<string>();
var debug = document["debug"]?.GetValue<bool>();
var timeout = document["timeout"]?.GetValue<int>();
Console.WriteLine($"env={environment} debug={debug} timeout={timeout}");

if (document.TryGetPropertyValue("Database", out var databasesNode) && databasesNode is JsonArray databases)
{
    var primaryDb = databases[0]?.AsObject();
    var dbHost = primaryDb?["host"]?.GetValue<string>();
    var dbPort = primaryDb?["port"]?.GetValue<int>();
    Console.WriteLine($"primary database -> {dbHost}:{dbPort}");
}
```

### Example Tyco File

```
tyco/example.tyco
```

```tyco
# Global configuration with type annotations
str environment: production
bool debug: false
int timeout: 30

# Database configuration struct
Database:
 *str name:           # Primary key field (*)
  str host:
  int port:
  str connection_string:
  # Instances
  - primary, localhost,    5432, "postgresql://localhost:5432/myapp"
  - replica, replica-host, 5432, "postgresql://replica-host:5432/myapp"

# Server configuration struct  
Server:
 *str name:           # Primary key for referencing
  int port:
  str host:
  ?str description:   # Nullable field (?) - can be null
  # Server instances
  - web1,    8080, web1.example.com,    description: "Primary web server"
  - api1,    3000, api1.example.com,    description: null
  - worker1, 9000, worker1.example.com, description: "Worker number 1"

# Feature flags array
str[] features: [auth, analytics, caching]
```

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
