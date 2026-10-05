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
Tabelle: Sites, Technicians, Repairs, Receipts, Reassignments, RepairTransitions e __EFMigrationsHistory.
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
Persistiti apertura, disponibilità, riassegnazione, invio/esito del collaudo e ritorno
in lavorazione. Sites è uno stato operativo per Repairs, non il modello definitivo
Tenant Management; Technicians è una proiezione per assegnazione, non un account.
Avvio lavoro collegato a Quotes/Payments, API, worker/outbox e autenticazione reale
restano da integrare. Nessun endpoint espone DbContext o RepairState al client.

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
Test verificati con REPARATIO_SQL_CONNECTION assente: 83 superati, inclusi 19 SQL,
zero ignorati. Anche la factory design-time legge il secret automaticamente.

## Transizioni persistite del ciclo di riparazione
RepairLifecycleAudit (20261005062052) aggiunge RepairTransitions ed è applicata.
I nuovi comandi usano lo stesso gate della sede e lo stesso protocollo dei due
adattatori precedenti. Snapshot, pratica e storico riassegnazioni vengono letti
nella stessa transazione Serializable. Il dominio viene ricaricato, il comando
applicato in Application e rivalidato sotto il lock del commit.

L'autore arriva da IRepairLifecycleAccess dopo il controllo della specifica azione
sulla pratica. Nessun actor viene letto da un campo del comando. L'implementazione
reale dei permessi resta da collegare a Identity/API. Timestamp dall'orologio server.

Riassegnazione aggiorna entrambe le proiezioni di carico e LastAssignedAt del nuovo
tecnico; esito positivo decrementa il carico, esito negativo lo mantiene. ReturnToWork
incrementa il carico ma non LastAssignedAt. Un decremento con carico zero segnala
incoerenza e annulla l'intera transazione, inclusa la versione della sede.
Storico riassegnazione e storico transizioni sono append-only tramite questi comandi.
Le ricevute serializzano i quattro tipi noti di comando con discriminatore esplicito;
un replay restituisce il risultato originario, anche se la pratica è poi cambiata.

19 test SQL complessivi, inclusi dieci nuovi casi di lifecycle. Le fixture di lifecycle
preparano stati sintetici per isolare il comportamento: non è un comando pubblico
per iniziare lavoro senza preventivo/acconto. La disponibilità dell'autorizzazione
per rilavorazione entro il preventivo è requisito dei futuri permessi; ReturnToWork
non deve essere usato per aggirare accettazione di nuovi lavori o revisioni.
