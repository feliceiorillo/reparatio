# Reparatio

SaaS multi tenant per negozi di riparazione smartphone.

## Stato

Prima implementazione del dominio Repairs: apertura e selezione automatica del tecnico.
.NET 10 LTS; test xUnit. Frontend Angular, SQL Server, servizi e infrastruttura non ancora implementati.

## Verifica

Con SDK .NET 10 installato:

```sh
dotnet restore Reparatio.slnx
dotnet test Reparatio.slnx
```

In questo ambiente manca dotnet e il download del SDK non è raggiungibile.
I test sono stati scritti prima dell'implementazione ma non sono stati eseguiti:
non è stato verificato né il RED né il GREEN. Le versioni NuGet dichiarate
devono ancora essere ripristinate e validate.

## Navigazione

- [Stato del lavoro](docs/STATUS.md)
- [Modello di dominio](docs/DOMAIN.md)
- [Requisiti concordati](docs/REQUIREMENTS.md)
- [Test](tests/Reparatio.Repairs.Domain.Tests/AssignmentTests.cs)

Il repository è locale: non è ancora collegato a un remote.
