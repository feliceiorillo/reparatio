# Stato del lavoro

## Preparato
- Repository main e soluzione .NET 10.
- Progetto di dominio indipendente dall'infrastruttura.
- 8 test Fact e 9 casi Theory: 17 casi previsti.
- Policy di assegnazione e apertura dell'aggregato Repair.
- Requisiti e limiti documentati.

## Verifica bloccata
SDK .NET assente. Tentativo di accesso a dot.net non riuscito.
Nessun risultato di compilazione o test disponibile.

## Prossimi passi
1. Eseguire restore e test con SDK .NET 10; risolvere eventuali errori.
2. TDD per riassegnazione, collaudo e rientro in lavorazione.
3. Application/CQRS e test della consistenza dell'assegnazione concorrente.
4. SQL Server e integrazione; poi API e interfaccia.
