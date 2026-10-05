# Reparatio

SaaS multi tenant per negozi di riparazione smartphone.
Progetto locale: C:\Users\felice\Documents\Codex\reparatio.

## Stato

Dominio .NET 10: assegnazione, riassegnazione con storico e collaudo.
Application/CQRS: apertura con controllo accesso, idempotenza e retry concorrenti.
35 test xUnit. SQL Server, API, Angular e infrastruttura non ancora implementati.
I contratti Application non sono adattatori di produzione.

## Verifica

Con SDK .NET 10:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet restore Reparatio.slnx --disable-parallel -m:1
dotnet test Reparatio.slnx --no-restore -m:1
```

Il 5 ottobre 2026 restore e test sono stati eseguiti sul PC: 35 test superati.
Nel contesto ristretto di Codex anche le cache CLI/NuGet sono state reindirizzate
in una cartella scrivibile; vedere docs/STATUS.md per percorso e limiti.

## Documentazione

- [Stato verificato e prossimi passi](docs/STATUS.md)
- [Modello di dominio](docs/DOMAIN.md)
- [Requisiti concordati](docs/REQUIREMENTS.md)
- [Brief completo del progetto](docs/PROJECT_BRIEF.md)

Repository su main con storico originale conservato, senza remote configurato.
