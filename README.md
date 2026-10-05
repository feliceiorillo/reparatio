# Reparatio

SaaS multi tenant per negozi di riparazione smartphone.
Progetto locale: C:\Users\felice\Documents\Codex\reparatio.

## Stato

Dominio .NET 10: assegnazione, riassegnazione con storico e collaudo.
Application/CQRS: apertura e disponibilità con FIFO, accesso, idempotenza e retry.
Persistenza Code First EF Core 10.0.12 / SQL Server: migrazione InitialRepairs applicata
al database Reparatio. Apertura e disponibilità salvate in transazioni atomiche.
60 test xUnit superati: 35 Domain, 16 Application, 9 SQL Server reali.
API, autenticazione reale, Angular e workflow completo ancora da implementare.

## Verifica

Con SDK .NET 10 e schema già migrato:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet restore Reparatio.slnx --disable-parallel -m:1
dotnet test Reparatio.slnx --no-restore -m:1
```

I test SQL leggono automaticamente il secret locale cifrato. REPARATIO_SQL_CONNECTION è un override opzionale; senza entrambe le fonti i test SQL risultano ignorati. Per migrare e verificare con database reale,
vedere [persistenza e comandi riproducibili](docs/PERSISTENCE.md).
Le credenziali non sono salvate nei file del progetto.

## Documentazione

- [Stato verificato e prossimi passi](docs/STATUS.md)
- [Modello di dominio](docs/DOMAIN.md)
- [Persistenza Code First](docs/PERSISTENCE.md)
- [Requisiti concordati](docs/REQUIREMENTS.md)
- [Brief completo del progetto](docs/PROJECT_BRIEF.md)

Repository su main con storico originale conservato, senza remote configurato.
