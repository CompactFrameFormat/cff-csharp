# AGENTS.md

This file provides guidance to AI coding agents working in this repository.

## Project Overview

C# reference implementation of Compact Frame Format (CFF), a binary message framing protocol for microcontrollers and reliable serial links. Published as a NuGet package (`CompactFrameFormat`). The library targets netstandard2.0 for broad compatibility.

## Build & Test Commands

```bash
dotnet build                    # Build the solution
dotnet test                     # Run all tests with coverage
dotnet format                   # Fix formatting
dotnet format --verify-no-changes  # Check formatting (used in CI and pre-commit hook)
```

Run a single test:
```bash
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

## Architecture

The solution has three projects:

- **CompactFrameFormat/** — Core library (netstandard2.0). Single file `Cff.cs` containing:
  - `Cff` static class — frame creation (`CreateFrame`), parsing (`TryParseFrame`), and stream parsing (`FindFrames`)
  - `CFrame` readonly struct — parsed frame with `FrameCounter` and `Payload`
  - `FrameParseResult` enum — parse outcome (Success, InsufficientData, InvalidPreamble, InvalidHeaderCrc, InvalidPayloadCrc)
  - CRC-16/CCITT-FALSE implementation with static lookup table

- **CompactFrameFormat.Tests/** — NUnit tests (net8.0). Unit tests in `UnitTests.cs`, integration tests in `IntegrationTests.cs` using binary test data files in `TestData/`.

- **UsageExample/** — Console demo app (net9.0).

## Frame Wire Format

`[0xFA 0xCE] [FrameCounter:u16le] [PayloadSize:u16le] [HeaderCRC:u16] [Payload...] [PayloadCRC:u16]`

Header is 8 bytes, payload CRC is 2 bytes, max payload is 65,535 bytes.

## CI/CD

GitHub Actions workflows enforce:
- Tests across .NET 6.0, 8.0, 9.0
- **95% minimum code coverage** (via coverlet + ReportGenerator)
- Format check via `dotnet format --verify-no-changes`
- NuGet publishing on release tags (version extracted from git tag)

## Performance Patterns

The library uses `ReadOnlySpan<byte>` / `ReadOnlyMemory<byte>` for zero-copy operations and `AggressiveInlining` on hot paths. Maintain these patterns when modifying the core library.
