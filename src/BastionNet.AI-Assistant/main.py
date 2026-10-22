#!/usr/bin/env python3
"""BastionNet AI Assistant - Python side helper.

This assistant runs alongside the C# client and provides real-time
suggestions as the user types. In a full implementation it would connect
via IPC (local socket or stdin/stdout bridge) to the C# client.

For now: basic placeholder with a simple suggestion engine.
"""

import sys
import time
import json
from typing import Optional


class AISuggestionEngine:
    """Motore suggerimenti AI basico."""

    def __init__(self):
        self.history = []
        self.running = False

    def get_suggestion(self, text: str) -> Optional[str]:
        """Restituisce una suggerimento basato su ciò che l'utente sta digitando."""
        if not text.strip():
            return None

        text_lower = text.lower().strip()

        # Suggerimenti lessicali semplici in italiano/english
        suggestions = {
            "ciao": "Ciao! Come posso aiutarti oggi?",
            "help": "Ecco cosa puoi fare: scrivi un messaggio e verrà trasmesso.",
            "grazie": "Prego! Sono qui per aiutarti.",
            "errore": "Forse intendevi 'errore' o vuoi segnalare un problema?",
        }

        for key, suggestion in suggestions.items():
            if key in text_lower:
                return suggestion

        # Generazione "autocomplete" basata sulla cronologia
        if len(self.history) > 0:
            last = self.history[-1]
            if text_lower.startswith(last[:3].lower()):
                return last

        return None

    def process_input(self, user_input: str) -> Optional[str]:
        """Processa l'input dell'utente e restituisce un suggerimento."""
        self.history.append(user_input)
        suggestion = self.get_suggestion(user_input)
        return suggestion


def main():
    print("=== BastionNet AI Assistant ===")
    print("Assistant avviato. In attesa di input dal client C...")
    print("Premi Ctrl+C per terminare.\n")

    engine = AISuggestionEngine()

    try:
        while True:
            # In una configurazione reale, leggerei da un socket locale o pipe
            # qui usiamo stdin per semplicità dimostrativa
            user_input = input("Testo digitato (o 'quit' per uscire): ").strip()

            if user_input.lower() == "quit":
                break

            suggestion = engine.process_input(user_input)

            if suggestion:
                print(f"[AI SUGGERIMENTO] {suggestion}")
            else:
                print("[AI] Nessun suggerimento disponibile.")

            time.sleep(0.1)  # Piccola pausa per non bloccare eccessivamente

    except KeyboardInterrupt:
        print("\nInterrotto dall'utente.")
    finally:
        engine.running = False
        print("Assistant terminato.")


if __name__ == "__main__":
    main()