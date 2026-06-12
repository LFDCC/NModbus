# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Restore, build, test (mirrors CI in .github/workflows/pr.yml)
dotnet restore .
dotnet build --configuration Release --no-restore .
dotnet test --no-restore --verbosity normal .

# Run a single test by name
dotnet test --no-restore --filter "FullyQualifiedName~NModbus.UnitTests.IO.ModbusRtuTransportFixture"

# Pack NuGet packages (CI uses /p:Version=x.y.z)
dotnet pack --configuration Release /p:Version=1.0.0 --output . ./
```

CI runs on `windows-latest` and requires .NET 6.0.x and 7.0.x SDKs. The unit test project targets `net4.6` (Framework), while integration tests target both `net4.6` and `net6.0`.

## Project Structure

| Project | Purpose | Targets |
|---|---|---|
| `NModbus/` | Core library — protocol, transports, devices, data stores | net46, netstandard1.3, netstandard2.0, net6.0 |
| `NModbus.Serial/` | Serial port adapter via `System.IO.Ports` | net46, netstandard2.0, net6.0 |
| `NModbus.SerialPortStream/` | Legacy SerialPortStream adapter | net46, netstandard1.5 |
| `NModbus.UnitTests/` | Unit tests (xUnit + Moq) | net4.6 |
| `NModbus.IntegrationTests/` | Integration tests (xUnit + Shouldly) | net4.6, net6.0 |
| `Samples/` | Sample console application | net46 |

## Architecture

### Factory Pattern (`ModbusFactory` / `IModbusFactory`)

`ModbusFactory` is the central entry point. It creates masters, slaves, slave networks, and transports. All creation flows through this factory — it holds the function service registry and logger. Custom function code handlers can be injected via the constructor.

### Transport Layer (`NModbus/IO/`)

Transports handle framing and wire-level communication. The hierarchy is:

- `ModbusTransport` (abstract base) → `ModbusIpTransport` (TCP/UDP), `ModbusSerialTransport` → `ModbusRtuTransport`, `ModbusAsciiTransport`
- `EnhancedModbusRtuTransport` for non-standard RTU variants
- `IStreamResource` abstracts the byte stream; concrete adapters: `TcpClientAdapter`, `UdpClientAdapter`, `SocketAdapter`

### Device Layer (`NModbus/Device/`)

- **Masters**: `ModbusMaster` (base) → `ModbusIpMaster`, `ModbusSerialMaster`. Also `ConcurrentModbusMaster` for thread-safe access.
- **Slaves**: `ModbusSlave` with `ModbusSlaveNetwork` subclasses (`ModbusTcpSlaveNetwork`, `ModbusUdpSlaveNetwork`, `ModbusSerialSlaveNetwork`).
- Slave networks accept connections and dispatch requests to registered `IModbusSlave` instances.

### Function Code Services (`NModbus/Device/MessageHandlers/`)

Each Modbus function code is implemented as an `IModbusFunctionService`. The factory registers built-in services (read coils, read/write registers, diagnostics, file records, device identification, etc.). Custom services can be added or replace built-in ones via `ModbusFactory` constructor.

### Message Types (`NModbus/Message/`)

Request/response PDU classes for each function code. Messages implement `IModbusMessage` and handle serialization/deserialization of the Modbus PDU.

### Data Model (`NModbus/Data/`)

`SlaveDataStore` / `DefaultSlaveDataStore` holds coil/discrete input/register data. `DiscreteCollection` and `RegisterCollection` are the backing collections. `IPointSource<T>` provides individual point access.

### Extensions (`NModbus/Extensions/`)

- `ModbusMasterEnhanced` — convenience methods on `IModbusMaster`
- `Enron/` — Enron Modbus extensions (32-bit registers)
- `Functions/` — register read/write helpers with endian handling
- `CrcExtensions` — CRC validation helpers

### Serial Adapters (separate packages)

`NModbus.Serial` wraps `System.IO.Ports.SerialPort` as `IStreamResource` via `SerialPortAdapter`. The factory extension in `NModbus.Serial/ModbusFactoryExtensions.cs` adds `CreateMaster(SerialPort)` / `CreateSlaveNetwork(SerialPort)` methods.

## Conventions

- Unit test classes use `*Fixture.cs` naming (e.g., `ModbusRtuTransportFixture.cs`)
- Unit tests use xUnit + Moq; integration tests use xUnit + Shouldly
- `TreatWarningsAsErrors` is enabled across all projects
- The root namespace is `NModbus` (not `NModbus.Core` or similar)
- NuGet packages are published via the `push.yml` workflow, triggered by semver tags (`v*.*.*`)

## Key Differences from NModbus4 (predecessor)

- Slave devices are added to a network via `IModbusSlaveInstance`
- Heavier use of interfaces throughout
- Custom function code handlers can be registered on slave devices
