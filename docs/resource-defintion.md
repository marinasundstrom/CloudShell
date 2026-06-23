```json
// Resource
{
    "name": "acme-api",
    "type": "application.executable",
    "executable.path": "whatsup.exe",
    "executable.arguments": "doc",

    // capabilities
    // attributes
}
```

```json
// Resource type
{
    "name": "application.executable",
    "class": "application",
    "capabilities": [
        "application.executable.runner",
        "storage.volumeConsumer"
    ],
    "executable.workingDirectory": ".",
    "custom.data": {
        // Some complex value
    }

    // capabilities
    // attributes
}
```

```json
// Resource class
{
    "name": "application"

    // capabilities
    // attributes
}
```