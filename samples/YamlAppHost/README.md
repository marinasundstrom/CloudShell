# CloudShell YAML App Host Sample

This is the first file-based CloudShell local-development path. The
`cloudshell.yaml` file declares an ASP.NET Core application project without a
language-specific launcher project.

Install the CLI preview from the CloudShell MyGet feed:

```bash
dotnet tool install --global CloudShell.Cli \
  --add-source https://www.myget.org/F/cloudshell/api/v3/index.json \
  --version <preview-version>
```

Then run this directory:

```bash
cloudshell run
```

The CLI discovers `cloudshell.yaml`, starts the matching development host from
the tool package, applies the resource template, and stays attached to that
host. Open the printed CloudShell URL, start **YAML Sample API** from Resource
Manager, and then open <http://localhost:5265>. Press Ctrl+C in the original
terminal to stop the development host and its child processes.

There is deliberately no daemon or attach behavior in this flow. Use
`cloudshell run <path-to-template>` when the file is not named
`cloudshell.yaml` or is not in the current directory.
