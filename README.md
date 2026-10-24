# BastionNet 🖥

**BastionNet** is a peer-to-broadcast encrypted chat system: a central server relays messages between clients **without ever being able to read them**, while a custom AI assistant — written from scratch in Python — helps you compose your messages as you type.

## Overview

- **Clients and server are written in C#** (.NET).
- **All message content is end-to-end encrypted with AES.** The server only sees ciphertext; its job is to broadcast each message to every connected client except the sender.
- **A custom Python AI assistant** runs next to the client and suggests completions, rephrasings, and corrections in real time while you type — fully self-written, no third-party APIs.

## Features

- 🔐 End-to-end encrypted messaging (AES-256-GCM)
- 📡 Central relay server with broadcast delivery (everyone but the sender)
- 🧠 Custom AI writing assistant (pure Python, runs locally)
- 🖥️ Cross-platform clients built on .NET
- 🚫 The server **cannot** read message content — it is a blind relay

## Architecture

```
┌──────────────┐   encrypted    ┌──────────────────┐   encrypted    ┌──────────────┐
│  C# Client A │◄──────────────►│   C# Server      │◄──────────────►│  C# Client B │
│              │   (AES-256)    │  (blind relay,   │   (AES-256)    │              │
│  ┌────────┐  │                │   broadcast to   │                │  ┌────────┐  │
│  │ Python │  │                │   all clients    │                │  │ Python │  │
│  │   AI   │  │                │   except sender) │                │  │   AI   │  │
│  └────────┘  │                └──────────────────┘                │  └────────┘  │
└──────────────┘                                                    └──────────────┘
```

### How end-to-end encryption works

1. Clients exchange public keys with the server acting only as a directory/relay.
2. A shared AES-256-GCM key is negotiated per session (or per group) between clients.
3. Each message is encrypted by the sender's client and decrypted only by recipients.
4. The server routes ciphertext packets; it holds no keys and cannot decrypt anything.

### How the AI assistant works

- A local Python process listens to what you type in the client's input box (via a local IPC channel).
- It computes real-time suggestions — autocomplete, grammar fixes, rephrasing — using its own model/engine.
- Suggestions are rendered inline; pressing `Tab` (or clicking) accepts one.
- **Nothing is sent to the cloud** — the AI and the encryption both keep your words private.

## Tech Stack

| Component    | Technology                     |
| ------------ | ------------------------------ |
| Server       | C# / .NET                      |
| Client       | C# / .NET                      |
| Encryption   | AES-256-GCM, ECDH key exchange |
| AI Assistant | Python (pure, self-written)    |
| IPC          | Local socket / stdio bridge    |

## Project Structure

```
BastionNet/
├── Server/          # C# central relay server
├── Client/          # C# chat client
├── AI-Assistant/    # Python AI writing assistant
└── Shared/          # Common crypto & protocol code
```

## Getting Started

### Prerequisites

- [.NET SDK 8+](https://dotnet.microsoft.com/download)
- [Python 3.10+](https://www.python.org/downloads/)

### Run the server

```bash
cd Server
dotnet run
```

### Run a client

```bash
cd Client
dotnet run
```

### Run the AI assistant

```bash
cd AI-Assistant
python main.py
```

## Roadmap

- [ ] Key exchange and AES-256-GCM message encryption
- [ ] Server relay with broadcast delivery
- [ ] Client UI with encrypted send/receive
- [ ] Python AI assistant with inline autocomplete
- [ ] Multiple chat rooms
- [ ] Offline message queue

## License

This project is licensed under the [MIT License](LICENSE).
