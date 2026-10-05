# SQL Server — Code First

EF Core SqlServer/Design e dotnet-ef 10.0.12, fissati nei progetti e nel manifesto.
La configurazione condivisa SqlConnectionSettings legge prima REPARATIO_SQL_CONNECTION,
altrimenti il secret locale cifrato. Tests e design-time usano lo stesso lettore.

## Database verificato
Server: WIN-796T11TJJRG\SQLEXPRESS. Database: Reparatio, login dedicato reparatio.
Il nome dell'istanza non è stato risolto durante la verifica: connessione riuscita
con Server=tcp:127.0.0.1,62081 e Database=Reparatio. La porta è dinamica e può cambiare
al riavvio: non viene fissata nel codice. TLS e TrustServerCertificate sono mantenuti
come richiesto. Persist Security Info=False; timeout finiti 15/30 secondi per evitare
attese infinite; MARS disabilitato. Su richiesta dell'utente, le credenziali sono ora salvate nel secret locale cifrato, fuori dal repository.

## Migrazioni
Prima migrazione: InitialRepairs (20261005055013), applicata al database già creato.
Tabelle: Sites, Technicians, Repairs, Receipts, Reassignments e __EFMigrationsHistory.
Sono presenti chiavi composte tenant/sede, foreign key senza cancellazione a cascata,
vincoli di carico/stato/assegnazione e indici univoci per ordine di arrivo e ricevute.

Con il secret locale già configurato, dalla cartella del progetto:

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
la versione del tool; non serve un'installazione globale. I test SQL richiedono schema già migrato. Se mancano sia secret sia variabile d'ambiente, vengono esplicitamente ignorati.

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

## Secret locale Windows
Percorso predefinito: C:\Users\felice\Documents\Codex\.secrets\reparatio\sql.dpapi.
Il file è cifrato con Windows DPAPI CurrentUser: richiede lo stesso utente Windows.
Non è incluso nel repository. Nessuna password è incorporata in sorgenti o script.
REPARATIO_SQL_SECRET_PATH permette un percorso alternativo; REPARATIO_SQL_CONNECTION
ha precedenza per CI/ambienti diversi da Windows. Un secret non decifrabile fa fallire
la configurazione con messaggio senza credenziali: non viene ignorato silenziosamente.

Per ricreare/aggiornare il secret, usare scripts/set-sql-secret.ps1; la password viene
richiesta con input nascosto. Server, Database, UserName e Path sono parametri opzionali.
Il percorso standard .NET User Secrets del profilo non era scrivibile nel sandbox;
è stato usato questo percorso autorizzato con cifratura Windows, senza protocolli custom.
Test verificati con REPARATIO_SQL_CONNECTION assente: 60 superati, inclusi 9 SQL,
zero ignorati. Anche la factory design-time legge il secret automaticamente.
