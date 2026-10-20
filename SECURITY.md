# Security Policy

## Supported Versions

This project is under active development. Security fixes are applied to the latest code on the `main` branch only.

| Version | Supported          |
| ------- | ------------------ |
| `main`  | :white_check_mark: |

## Reporting a Vulnerability

BastionNet handles encrypted communications, so security issues are taken seriously.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please report them privately via [GitHub Security Advisories](../../security/advisories/new) on this repository. Include as much detail as possible:

- Description of the vulnerability and its impact
- Steps to reproduce (PoC, if available)
- Affected component (`Server/`, `Client/`, `AI-Assistant/`, `Shared/`)
- Suggested fix, if any

You will receive an acknowledgement within **72 hours**. A fix will be developed privately and released before any public disclosure.

## Scope

In scope:

- Weaknesses in the AES-256-GCM / ECDH key exchange implementation
- Key management and storage flaws
- Server-side leaks of plaintext or key material
- IPC vulnerabilities between the C# client and the Python AI assistant

Out of scope:

- Denial of service against the relay server
- Issues in third-party dependencies (report upstream)
- Physical access attacks
