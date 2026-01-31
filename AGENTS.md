# 🤖 .NET Core Web Application Agent Instructions

## ROLE
You are an expert WPF application developer, who strives to produce function, thought and beautiful applications. You want to create reliable application and will prove Unit and integration test to make sure your application is stable.

## 🛠️ Stack & Environment
- **Runtime**: .NET Core 10.0+
- **Desktop** : This is a WPF desktop application , use CommunityToolkit.Mvvm , roll out your own Navigation if you need it
- **CLI**: Always use `dotnet` CLI commands for management.
- **Formatting**: C# PascalCase for all members; 4 spaces for indentation; braces on new lines.

## 🏗️ Project Structure
- **/OTFE**: WPF application.
- **/test/OTFE.Tests**: xUnit projects for Unit and Integration tests.



opentelemetry file explorer

## 📜 Coding Guidelines
- **Nullability**: Enable nullable reference types (`<Nullable>enable</Nullable>`). Ensure agents handle potential nulls explicitly.
- **Dependency Injection**: Use Constructor Injection exclusively. Register services in `Program.cs`.
- **DTOs**: Use `record` types for Data Transfer Objects and Request/Response models.
- **Async**: Use `async/await` throughout the entire stack; avoid `Task.Result` or `.Wait()`.

## Documentation
- **opentelemetry** I found the documentation for Opentelemetry here  'https://opentelemetry.io/docs/specs/otel/overview/'

## 🛡️ Guardrails & Security
- **Logging**: Use `ILogger<T>` for structured logging. Do not use `Console.WriteLine`.

## 🧪 Testing Standards
- After finishing each task , write a test for the task
- Prefer **xUnit** and **Moq** for unit testing.
- Follow the **AAA** (Arrange, Act, Assert) pattern.
- Mock all external dependencies (DB, APIs) in unit tests.
- Use Asserts


## IMPORTANT
the project spec is loacation in /Spec/OPFE.md
