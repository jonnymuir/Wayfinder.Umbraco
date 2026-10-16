# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through
[GitHub's private vulnerability reporting](https://github.com/jonnymuir/Wayfinder.Umbraco/security/advisories/new),
not in a public issue or pull request. Include the package version, your Umbraco version, what an
attacker can do, and the steps to reproduce it. A vulnerability in the engine itself belongs in
[Wayfinder's own reporting](https://github.com/jonnymuir/Wayfinder/security/advisories/new); if
you aren't sure which, report it here.

This is a solo-maintained project, so reports are answered as soon as possible rather than
against a fixed timescale. Fixes ship in a new release, credited in the advisory unless you'd
rather not be named.

## Supported versions

Only the latest release receives security fixes.

## Scope

In scope: the `Wayfinder.Umbraco` package: its authoring API, Block Grid blocks, stage and poll
endpoints, uSync handlers, and the MCP OAuth wiring. Of particular interest: one citizen or
tenant reaching another's request or files, bypassing the `BlueprintsAdmin` policy, and anything
in the OAuth discovery or token flow that deviates from the RFCs.

Out of scope: `Wayfinder.Umbraco.ReferenceApp` and `Wayfinder.Umbraco.AppHost`. They are local
demo hosts with published demo credentials and never ship. See the
[README's security responsibilities](README.md#security-responsibilities) for what a host owns.
