## Development

### Prerequisites
- Visual Studio 2022 or VS Code
- .NET 8.0 SDK

### TODO
- [ ] Fix right click context menu opening slowly on the first time
    - Perhaps preload the xaml and initialize the bindings without showing
- [ ] Scan Changes needs work
    - [ ] Maybe rename this to Scan Local Changes
- [ ] Add a read-only view (default on open)
    - [ ] Once write access is acquired, the view changes to what it currently is
    - [ ] Only show a simple "Sync" button that will do what the CLI does
