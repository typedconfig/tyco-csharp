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
JsonObject document = context.AsObject();

var timezone = document["timezone"]?.GetValue<string>();
Console.WriteLine($"timezone={timezone}");

if (document.TryGetPropertyValue("Application", out var appsNode) && appsNode is JsonArray apps)
{
    var primaryApp = apps[0]?.AsObject();
    var service = primaryApp?["service"]?.GetValue<string>();
    var command = primaryApp?["command"]?.GetValue<string>();
    Console.WriteLine($"primary service -> {service} ({command})");
}

if (document.TryGetPropertyValue("Host", out var hostsNode) && hostsNode is JsonArray hosts)
{
    var backupHost = hosts[1]?.AsObject();
    var hostname = backupHost?["hostname"]?.GetValue<string>();
    var cores = backupHost?["cores"]?.GetValue<int>();
    Console.WriteLine($"host {hostname} cores={cores}");
}
```

### Example Tyco File

```
tyco/example.tyco
```

```tyco
str timezone: UTC  # this is a global config setting

Application:       # schema defined first, followed by instance creation
  str service:
  str profile:
  str command: start_app {service}.{profile} -p {port.number}
  Host host:
  Port port: Port(http_web)  # reference to Port instance defined below
  - service: webserver, profile: primary, host: Host(prod-01-us)
  - service: webserver, profile: backup,  host: Host(prod-02-us)
  - service: database,  profile: mysql,   host: Host(prod-02-us), port: Port(http_mysql)

Host:
 *str hostname:  # star character (*) used as reference primary key
  int cores:
  bool hyperthreaded: true
  str os: Debian
  - prod-01-us, cores: 64, hyperthreaded: false
  - prod-02-us, cores: 32, os: Fedora

Port:
 *str name:
  int number:
  - http_web,   80  # can skip field keys when obvious
  - http_mysql, 3306
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
