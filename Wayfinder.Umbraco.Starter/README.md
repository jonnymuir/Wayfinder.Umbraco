# Wayfinder for Umbraco: starter kit

Try [Wayfinder for Umbraco](https://github.com/jonnymuir/Wayfinder.Umbraco) on a fresh Umbraco
site without writing any code. Add this package to a new site and start it to get:

- **A home page** that lists every service on the site and shows how to connect an AI assistant.
- **A sample service**, *Request a callback*, with a public side anyone can use without signing in
  and a staff side worked from a queue.
- **A demo caseworker** you sign in as with one button, no password, to pick up and decide requests
  on the **work queue** page.
- **A design partner endpoint** (MCP), so an AI assistant such as Claude Code can design a service
  with you in conversation. You sign in with your backoffice account when the assistant asks.
- **A page for every service**, at `/services/{key}`. A service the assistant saves can be tried
  straight away, with no content to create.

**This is for trying Wayfinder on your own machine, never for a real site.** The caseworker
sign-in has no password, so the starter refuses to run outside the Development environment.

## Try it in five minutes

You need the .NET 10 SDK and the Umbraco templates (`dotnet new install Umbraco.Templates`).

```bash
dotnet new umbraco -n TryWayfinder -r LTS --development-database-type SQLite \
  --friendly-name "Admin" --email admin@example.test --password "ChangeMe123!"
cd TryWayfinder
dotnet add package Wayfinder.Umbraco.Starter
dotnet run
```

Umbraco installs itself on first run, with the backoffice account you named (`-r LTS` picks
Umbraco 17, the version this package supports). Open the address `dotnet run` prints and you are
on the starter home page.

1. Open **Request a callback** and send a request, as a member of the public.
2. Open **Work queue**, sign in as the caseworker, pick up the request and arrange the callback.
3. Go back to the service: the page now shows the outcome.
4. Connect an AI assistant with the command on the home page, describe a service, and try it at
   `/services/{key}` once the assistant has saved it.

## How access works

The starter decides who can do what from the blueprints themselves, so a service designed in
conversation works the moment it is saved, whatever its queues are called:

- The queue that a blueprint's first stage sits in is the **public** queue. Anyone can start there,
  and each person sees only their own requests.
- **Every other queue** is staff work, open to the demo caseworker.

The design partner endpoint tells a connecting assistant about this, so it lays services out the
same way.

## Moving on to a real site

When you build a real site, remove this package and wire Wayfinder.Umbraco yourself: your own
identity model, your own pages with the Wayfinder blocks on them, and the MCP endpoint behind your
own sign-in. The [Wayfinder.Umbraco README](https://github.com/jonnymuir/Wayfinder.Umbraco#quickstart)
shows how. The blueprints you designed travel with you, through uSync or the backoffice.

## Settings

| Setting | Default | What it does |
|---|---|---|
| `Wayfinder:Starter:AllowOutsideDevelopment` | `false` | Lets the starter run outside Development, for a shared demo server that holds nothing real. Anyone who can reach the site can then sign in as the caseworker. |
| `Wayfinder:Mcp:*` | | The design partner sign-in, as for [Wayfinder.Umbraco](https://github.com/jonnymuir/Wayfinder.Umbraco#turning-on-mcp-authoring-in-your-own-site). |
