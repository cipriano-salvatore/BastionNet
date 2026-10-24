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

        # Suggerimenti lessicali estesi in italiano/english
        suggestions = {
            # Saluti e despedidas
            "ciao": "Ciao! Come posso aiutarti oggi?",
            "salve": "Salve! Come posso esserti utile?",
            "good morning": "Buongiorno! Come ti posso aiutare?",
            "good evening": "Buonasera! Come sto?",
            "arrivederci": "Arrivederci! A presto.",
            "goodbye": "Arrivederci! A presto.",

            # Aiuto e supporto
            "help": "Ecco cosa puoi fare: scrivi un messaggio e verrà trasmesso.",
            "info": "Puoi scrivere un messaggio e verrá trasmesso ai altri client.",
            "support": "Sono qui per aiutarti con suggerimenti in tempo reale.",

            # Risposte affermative/negative
            "yes": "Ottima scelta! Procedo con l'invio.",
            "no": "Va bene, nessun problema. Puoi cambiare idea in qualsiasi momento.",
            "sure": "Certamente! Cosa desideri fare?",

            # Ringraziamenti
            "grazie": "Prego! Sono qui per aiutarti.",
            "thank you": "Prego! Sono qui per aiutarti.",
            "thanks": "Prego! Sono qui per aiutarti.",

            # Errori e correzioni
            "errore": "Forse intendevi 'errore' o vuoi segnalare un problema?",
            "sbagliato": "Potresti riformulare il messaggio?",
            "scusa": "Nessun problema! Continua pure.",

            # Autocomplete tematico
            "messaggio": "Il tuo messaggio verrà cifrato e trasmesso al server.",
            "chiave": "La chiave sessione viene derivata dalle chiavi pubbliche dei peer.",
            "criptato": "Il messaggio è cifrato end-to-end con AES-256-CBC + HMAC.",
        }

        # Corrispondenza substring (più permissiva)
        for key, suggestion in suggestions.items():
            if key in text_lower:
                return suggestion

        # Generazione "autocomplete" basata sulla cronologia
        if len(self.history) > 0:
            last = self.history[-1]
            # Se l'input inizia con gli stessi primi caratteri della storia
            if text_lower.startswith(last[:3].lower()):
                return last

            # Controllo backward: se l'ultima storia inizia con ciò che sta digitando
            if last.lower().startswith(text_lower[:3]):
                return last

        return None

    def process_input(self, user_input: str) -> Optional[str]:
        """Processa l'input dell'utente e restituisce un suggerimento."""
        self.history.append(user_input)
        suggestion = self.get_suggestion(user_input)
        return suggestion


def main():
    print("=== BastionNet AI Assistant ===")
    print("Assistant pronto. Digita 'ai' per richiedere suggerimenti.\n")

    engine = AISuggestionEngine()

    try:
        while True:
            # In una configurazione reale, leggerei da un socket locale o pipe
            # qui usiamo stdin per semplicità dimostrativa
            user_input = input("Testo digitato (o 'quit' per uscire): ").strip()

            if user_input.lower() == "quit":
                break

            # Gestione formato "ai suggestion" inviato dal client C#
            if user_input.lower().startswith("ai "):
                # Estrai il testo dopo "ai " per basare il suggerimento su di esso
                suggested_text = user_input[3:].strip()
                if suggested_text:
                    # Processa il testo "ai X" come input per il motore suggerimenti
                    suggestion = engine.process_input(suggested_text)
                else:
                    suggestion = "[AI] Formato: ai <testo>"  # Formato errore
            else:
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