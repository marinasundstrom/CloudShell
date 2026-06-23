```json
// Resource
{
    "$artifact": "resource",

    "name": "acme-api",
    "type": "application.executable",
    "executable.path": "whatsup.exe",
    "executable.arguments": "doc",
    "volumes": [
        []
    ]

    // capabilities
    // attributes
}
```

```json
// Resource type
{
    "$artifact": "resource.type",

    "name": "application.executable",
    "class": "application",
    "capabilities": [
        "storage.volumeConsumer"
    ],
    "executable.workingDirectory": ".",
    "custom.data": {
        // Some complex value
    },
    "logSources": [
        {}
    ],
    "operations": [
        {}
    ]

    // capabilities
    // attributes
}
```

```json
// Resource class
{
    "$artifact": "resource.class",

    "name": "application"

    // capabilities
    // attributes
}
```