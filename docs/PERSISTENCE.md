# SQL Server — Code First

EF Core SqlServer/Design e dotnet-ef 10.0.12, fissati nei progetti e nel manifesto.
Nessuna stringa con credenziali è salvata nel repository. Il contesto usa
REPARATIO_SQL_CONNECTION; il design-time ha un fallback locale Windows senza password
per generare migrazioni, che non sostituisce la connessione effettiva per l'aggiornamento.

## Database verificato
Server: WIN-796T11TJJRG\SQLEXPRESS. Database: Reparatio, login dedicato reparatio.
Il nome dell'istanza non è stato risolto durante la verifica: connessione riuscita
con Server=tcp:127.0.0.1,62081 e Database=Reparatio. La porta è dinamica e può cambiare
al riavvio: non viene fissata nel codice. TLS e TrustServerCertificate sono mantenuti
come richiesto. Persist Security Info=False; timeout finiti 15/30 secondi per evitare
attese infinite; MARS disabilitato. Le credenziali sono rimaste solo nei processi.

## Migrazioni
Prima migrazione: InitialRepairs (20261005055013), applicata al database già creato.
Tabelle: Sites, Technicians, Repairs, Receipts, Reassignments e __EFMigrationsHistory.
Sono presenti chiavi composte tenant/sede, foreign key senza cancellazione a cascata,
vincoli di carico/stato/assegnazione e indici univoci per ordine di arrivo e ricevute.

Impostare REPARATIO_SQL_CONNECTION nel processo corrente con un metodo sicuro,
poi dalla cartella del progetto:

```powershell
.\scripts\database.ps1 -Action Update
.\scripts\database.ps1 -Action Test
```

Per una nuova modifica al modello:

```powershell
.\scripts\database.ps1 -Action AddMigration -Name NomeModifica
.\scripts\database.ps1 -Action Script
```

Rivedere sempre la nuova migrazione prima di applicarla. AddMigration genera i file;
l'azione Update ricompila includendo la migrazione prima di aggiornarne il database.
Lo script usa cache locali escluse da Git. Il manifesto dotnet-tools.json conserva
la versione del tool; non serve un'installazione globale. I test SQL richiedono schema
già migrato: senza variabile di connessione vengono esplicitamente ignorati.

## Transazioni e isolamento
SqlRepairStore implementa entrambi i contratti Application. Ogni commit acquisisce
la stessa versione della sede tramite UPDATE condizionato, mantenendo il lock fino
al commit. Modifiche a pratica, disponibilità, carico, timestamp e ricevuta avvengono
insieme. Una versione superata restituisce false senza effetti persistiti. Deadlock
SQL e conflitti di unicità vengono gestiti con retry dei comandi. Gli snapshot sono
letti in transazione Serializable, iniziando dalla sede; tutte le query filtrano
tenant e sede. Non sono autorizzazioni: i contratti di accesso restano obbligatori.

I test usano dati sintetici con tenant casuali e ripuliscono solo quei tenant.
Non usano EnsureDeleted, non rimuovono il database e non alterano dati preesistenti.
Concorrenza forzata con barriera per richieste duplicate e apertura/disponibilità.

## Perimetro attuale
Persistiti apertura e prima assegnazione/disponibilità. Sites è uno stato operativo
della sede per Repairs, non il modello definitivo di Tenant Management; le righe
tecnico sono proiezioni per assegnazione, non account Identity. Nessun seed demo o
utente reale inserito. La tabella dello storico è predisposta ma riassegnazione e
collaudo non hanno ancora comandi di persistenza. Ogni futuro scrittore del carico
deve utilizzare lo stesso protocollo di versione. API, worker/outbox e autenticazione
restano da integrare; nessun endpoint espone il DbContext al client.
