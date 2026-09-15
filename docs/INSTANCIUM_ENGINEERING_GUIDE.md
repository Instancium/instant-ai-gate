# INSTANCIUM_ENGINEERING_PROTOCOL

## 🛠 Core Directives
- **Language & Tone:** Code, naming, commits, and `/// <summary>` descriptions must be strictly in English. No conversational filler or introductory phrases in comments (e.g., NO "Here is the code", "Sure, I can help"). Use a dry, technical style exclusively (e.g., "Validates user credentials").
- **Code Output (CRITICAL):** Absolutely NO abbreviations, ellipses, or placeholder comments (e.g., "// omitted for brevity", "// existing code", "// rest of the code remains unchanged", "// your code here"). NO code skipping! The minimum code output must be a complete, compilable function, class, or procedure.
- **Documentation:** XML comments (`///`) are mandatory for all public APIs. Inline comments must be used *only* to explain non-obvious logic (explain *Why*, not *What*).
- **C# Constraint:** Account for the C# compiler rule that `yield return` cannot be used inside a `try` block that contains a `catch` clause. Restructure logic accordingly (e.g., using iterator blocks or separating the try-catch from the yield).

## 💻 C# & Architecture
- **Code Style:** Strict adherence to Microsoft C# Coding Conventions. `PascalCase` for public members, `camelCase` for private fields/locals. Use `var` only when the type is immediately obvious. Use expression-bodied members where appropriate.
- **Modern C#:** `async/await` everywhere (do not mix sync and async code paths). Active use of LINQ and Nullable Reference Types (`#nullable enable`).
- **Best Practices:** Single Responsibility Principle (SRP), Dependency Injection (instead of direct instantiation), Composition over Inheritance.
- **Error Handling:** Explicit exception handling (NO empty `catch` blocks). Use `ILogger` for logging instead of `Console.WriteLine`.

## 🌐 ASP.NET Core
- Use Minimal APIs for simple endpoints.
- Strict adherence to RESTful conventions.
- Use `IOptions<T>` or `IOptionsMonitor<T>` for configurations.
- Validation via Data Annotations or FluentValidation.
- Implement global middleware for centralized exception handling.

## 🧪 Naming & Testing
- **Naming:** Verbs for methods (e.g., `Get`, `Calculate`), state prefixes for booleans (e.g., `is`, `has`, `can`). NO unnecessary abbreviations.
- **Unit Tests:** Strict AAA pattern (Arrange-Act-Assert).
- **Test Naming Format:** `MethodName_Scenario_ExpectedBehavior`.
- **Test Quality:** One assert per test where possible. Use meaningful, descriptive test data (NO "magic numbers" or generic names like `test1`).

## 📝 Commit Messages
- **Format:** Conventional Commits (`type(scope): description`).
- **Types:** `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`.
- **Limits:** Subject line strictly under 72 characters. Description must be imperative mood (e.g., "Add", not "Added").