# Oris.HttpKit

**A lightweight and robust HTTP client helper library for .NET**  
Provides easy GET/POST/PUT/PATCH/DELETE requests with **retry logic, caching, timeout handling, and integrated logging**.

[![NuGet](https://img.shields.io/nuget/v/Oris.HttpKit.svg)](https://www.nuget.org/packages/Oris.HttpKit/)

---

## Features

- Simple wrapper around `HttpClient`
- Automatic **retry with exponential backoff**
- **Circuit breaker** support to avoid repeated failures
- **In-memory caching** for GET requests
- Integrated **Serilog logging** (console + file)
- JSON serialization/deserialization built-in
- Supports .NET 6, 7, 8, 9

---

## Installation

```bash
dotnet add package OrisHttpKit --version 1.0.0
```
