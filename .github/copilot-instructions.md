# Copilot Instructions

## Commit Messages
- Use Conventional Commits format
- Format: `type(scope): description`
- Types: feat, fix, docs, style, refactor, test, chore
- Always use English for commit messages
- Keep subject line under 72 characters

## Code Style
- **Language**: All code, variable names, method names, and comments MUST be in English
- Use async/await patterns
- Prefer LINQ where appropriate
- Follow Microsoft C# coding conventions
- Use PascalCase for public members, camelCase for private fields
- Prefer expression-bodied members when appropriate
- Use `var` only when type is obvious

## Comments and Documentation
- Write comments in professional, technical style
- **AVOID conversational phrases** like:
  - ❌ "Here is the new code"
  - ❌ "This is your new function"
  - ❌ "Now we add this feature"
  - ❌ "Let's implement this"
  - ❌ "I've added this method"
- **USE clear, descriptive technical comments**:
  - ✅ "Validates user credentials against database"
  - ✅ "Calculates total price including tax"
  - ✅ "Handles connection timeout errors"
  - ✅ "Returns null if entity not found"
- XML documentation for public APIs required
- Inline comments only when logic is non-obvious
- Explain WHY, not WHAT (code should be self-explanatory)

## Naming Conventions
- Use descriptive, intention-revealing names
- Methods: Start with verbs (Get, Set, Process, Calculate, Validate)
- Booleans: Use is/has/can prefixes (isValid, hasPermission, canExecute)
- Avoid abbreviations except well-known ones (Id, Url, Html)

## Best Practices
- Single Responsibility Principle for classes and methods
- Dependency Injection over direct instantiation
- Prefer composition over inheritance
- Use nullable reference types appropriately
- Handle exceptions explicitly, avoid empty catch blocks
- Use `ILogger` for logging, not Console.WriteLine
- Async all the way - don't mix sync and async patterns

## ASP.NET Core / Razor Pages Specific
- Use minimal APIs for simple endpoints
- Leverage built-in dependency injection
- Use IOptions pattern for configuration
- Follow RESTful conventions for API endpoints
- Use data annotations for validation
- Implement proper error handling middleware

## Testing
- Follow Arrange-Act-Assert pattern
- Use descriptive test method names: `MethodName_Scenario_ExpectedBehavior`
- One assertion per test when possible
- Use meaningful test data, avoid magic numbers