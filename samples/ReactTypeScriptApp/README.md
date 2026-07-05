# React TypeScript App Sample

This sample uses a TypeScript launcher to declare a small app topology:

- a React/Vite frontend as `application.javascript-app:react-frontend`
- a Node backend API as `application.javascript-app:react-api`
- a Configuration Store with settings projected into the backend environment
- a Traefik load-balancer resource with frontend and `/api` backend routes

Run the launcher against the local development host:

```bash
./cloudshell.sh run-no-auth
```

From a second terminal, inspect resources and start the frontend with its
dependencies:

```bash
./cloudshell.sh resources
./cloudshell.sh start-app
```

Install the frontend dependencies before starting the React resource for the
first time:

```bash
cd App
npm install
```

After the app starts, open:

```text
http://localhost:5175
```

The frontend calls the backend at `http://localhost:5185/api/status`. The
backend reads `Sample--Message` and `Sample--Mode` from the Configuration
Store through CloudShell-projected environment variables, then returns them to
the React app.

The launcher itself is `AppHost/src/app-host.ts`. It runs through Node's
TypeScript transform path for the sample commands, so template/apply/run do
not require building generated JavaScript first. Use `npm run typecheck` in
`AppHost` when changing the launcher source.
