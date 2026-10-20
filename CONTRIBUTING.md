# Contributing to BastionNet

Thanks for your interest in contributing! This document explains how to get started.

## Project Layout

| Directory       | Description                                  | Stack          |
| --------------- | -------------------------------------------- | -------------- |
| `Server/`       | Central blind relay server (broadcast)       | C# / .NET 8    |
| `Client/`       | Chat client with inline AI suggestions       | C# / .NET 8    |
| `AI-Assistant/` | Local AI writing assistant                    | Python 3.10+   |
| `Shared/`       | Common crypto & protocol code (AES-256-GCM)  | C# / .NET 8    |

## Prerequisites

- [.NET SDK 8+](https://dotnet.microsoft.com/download)
- [Python 3.10+](https://www.python.org/downloads/)

## Development Workflow

1. Fork the repository and create a branch from `main`:
   ```bash
   git checkout -b feature/my-feature
   ```
2. Make your changes, following the conventions of the directory you work in:
   - C#: follow standard .NET coding conventions, keep `Shared/` free of UI or I/O dependencies.
   - Python: PEP 8, type hints encouraged in `AI-Assistant/`.
3. Test your changes manually (run the server, a client, and the assistant).
4. Commit with a clear message and open a Pull Request against `main`.

## Guidelines

- **Cryptography first**: any change touching `Shared/` or the key exchange must preserve the property that the server never sees plaintext or key material. Explain the crypto reasoning in your PR.
- **No cloud calls**: the AI assistant must remain fully local. PRs adding external API calls will be rejected.
- **Keep it small**: prefer focused PRs over large multi-topic ones.
- **Documentation**: update `README.md` if you change the architecture, tech stack, or usage.

## Reporting Bugs

Open a GitHub issue with:

- OS and .NET / Python versions
- Steps to reproduce
- Expected vs. actual behavior
- Logs or screenshots, with **no plaintext messages or keys pasted in issues**

For security vulnerabilities, see [SECURITY.md](SECURITY.md) instead — do not report them publicly.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
