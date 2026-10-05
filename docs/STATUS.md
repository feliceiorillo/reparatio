# Stato del lavoro — 5 ottobre 2026

## Ambiente e repository
- Progetto sul PC: C:\Users\felice\Documents\Codex\reparatio.
- Estratto dall'archivio originale preservando .git e i due commit iniziali su main.
- SDK 10.0.401: C:\Program Files\dotnet\dotnet.exe.
- MSBuildEnableWorkloadResolver=false e -m:1 necessari nell'ambiente ristretto.
- EF Core SqlServer/Design e dotnet-ef 10.0.12; tool locale versionato.
- SQL Server 17.0.1000.7, WIN-796T11TJJRG\SQLEXPRESS.
- Database Reparatio già creato dall'utente; nuovo login dedicato con db_owner.
- Connessione riuscita via TCP 127.0.0.1:62081; nome istanza non risolto nel contesto.
- Su richiesta dell'utente, credenziali salvate nel secret locale DPAPI, fuori dal repository e dai commit.

## Implementato
- Policy tecnica per tenant/sede, minor carico, rotazione e Guid stabile.
- Apertura, prima assegnazione, riassegnazione con storico, avvio lavoro, collaudo
  positivo/negativo e ritorno in lavorazione nel dominio.
- FIFO con ArrivalSequence positivo e univoco, ricalcolo carico tra assegnazioni.
- Application: apertura e disponibilità con contratti di autorizzazione,
  idempotenza, payload invariato, cancellazione e retry limitati.
- Nuove pratiche non superano quelle già in attesa.
- Infrastructure EF Code First: Sites, Technicians, Repairs, Receipts, Reassignments.
- Chiavi/FK composte tenant/sede, carico non negativo, stati/assegnazioni coerenti,
  sequenze univoche e ricevute per tenant/tipo comando/RequestId.
- SqlRepairStore implementa apertura e disponibilità. Snapshot Serializable;
  commit protetti dalla versione condivisa della sede e transazione ReadCommitted.
- Pratica, disponibilità, carico, timestamp, FIFO e ricevuta persistiti atomicamente.
- Migration InitialRepairs 20261005055013 applicata al database Reparatio.
- scripts/database.ps1 per migrazioni, script SQL e test; nessuna password incorporata.

## Verificato
- Restore NuGet e compilazione con warnings as errors.
- Intera soluzione: 83 test superati, zero falliti, zero ignorati.
- 41 Domain + 23 Application + 19 integrazione su SQL Server reale.
- SQL: apertura e ricevute persistenti, replay, payload modificato, identificativo
  pratica duplicato, versione superata senza scritture parziali, FIFO, disponibilità.
- Concorrenza SQL forzata con barriera: doppia richiesta identica e apertura contro
  cambio disponibilità condividono la versione e non duplicano carichi/pratiche.
- Query isolate per tenant/sede; FK respingono tecnici di altro tenant o altra sede;
  database respinge carico negativo senza alterare quello persistito.
- ef migrations has-pending-model-changes: nessuna differenza dal modello migrato.
- Fixture SQL sintetiche, rimozione soltanto dei propri tenant casuali; nessun drop.

## Evidenza TDD
- I 17 test originali passavano prima delle modifiche locali; il RED/GREEN storico
  della vecchia sessione resta non verificato.
- Lifecycle: RED di compilazione per API assente, poi GREEN.
- Application apertura: otto test falliti con handler non implementato, poi GREEN.
- FIFO: otto Domain e otto Application falliti prima del comportamento, poi GREEN.
- SQL: dopo applicazione dello schema, sette test falliti per adattatore non
  implementato (RED); dopo implementazione tutti superati (GREEN).
- Due ulteriori verifiche SQL su isolamento e carico aggiunte come regressioni.
- Il primo tentativo di test SQL senza tabelle non è contato come RED comportamentale.

## Limiti e prossimi passi
- Autenticazione/autorizzazione reali non implementate: contratti obbligatori in API.
- Riassegnazione, collaudo e ritorno al lavoro persistiti; restano API e implementazione reale dei contratti di autorizzazione.
- Ripresa automatica della coda dopo tutte le cause di risveglio: coordinatore/worker
  e outbox ancora da implementare. Il comportamento è eseguito dal comando disponibilità.
- RepairWorkAuthorization è uno snapshot interno fidato di Quotes/Payments,
  non un input client né la prova di accettazione di un preventivo.
- ReturnToWork rappresenta rilavorazione dopo collaudo; nuovi lavori richiedono
  revisione accettata e controllo acconto, ancora da modellare.
- Sites/Technicians sono stato operativo e proiezioni di Repairs, non il modello
  definitivo Tenant Management o Identity.
- Diagnosi dettagliata, listino/preventivi, rifiuti, pagamenti, ritiro, API, Angular,
  notifiche e demo ancora da implementare.
- Nessuna decisione definitiva su account cliente globale o provider pagamenti.

Per i comandi e lo schema vedere docs/PERSISTENCE.md.

## Secret locale e test automatici
- Secret cifrato per l'utente Windows in Documents\Codex\.secrets\reparatio\sql.dpapi.
- SqlConnectionSettings condiviso da test e factory EF design-time; variabile di
  connessione opzionale con precedenza, percorso alternativo tramite variabile dedicata.
- script database.ps1 non richiede più di impostare manualmente la connessione.
- script set-sql-secret.ps1 aggiorna il secret con password richiesta a input nascosto.
- Verificata intera soluzione con connessione assente dall'ambiente: 60 test superati,
  inclusi i nove SQL, nessuno ignorato.

## Persistenza riassegnazione e collaudo completata
- Comandi ReassignRepair, SubmitRepairForTesting, RecordRepairTesting,
  ReturnRepairToWork; RepairLifecycleHandler con idempotenza e cinque retry.
- Accesso verificato prima di leggere anche nei replay; autore ricavato dal contratto
  di identità autenticata. Il comando non contiene un ActorId liberamente inviabile.
- Repair.Restore/Snapshot ricostruisce lo stato persistito e copia lo storico,
  senza setter pubblici o finte assegnazioni. Identificativi/stati incoerenti respinti.
- SqlRepairStore acquisisce lo stesso gate di apertura/disponibilità e rivalida il
  cambiamento col dominio prima di salvare. Pratica, carichi, audit e ricevuta atomici.
- Riassegnazione: carico precedente -1, nuovo +1; timestamp di assegnazione nuovo;
  storico con precedente/nuovo tecnico, autore, data e motivo. Storico già esistente
  ricaricato e mantenuto senza duplicazione. Precedente indisponibile consentito.
- Invio al collaudo: carico invariato. Esito positivo: carico -1. Esito negativo:
  ritorno InProgress con carico invariato. Rilavorazione dopo esito positivo: +1
  senza modificare LastAssignedAt, perché non è una nuova assegnazione.
- Tabella RepairTransitions conserva invio, esito e ritorno al lavoro, autore/data,
  stato precedente/nuovo, esito nullable e note/motivo. Note fino a 2000 caratteri;
  motivo riassegnazione fino a 1000. Motivo obbligatorio per riaprire la lavorazione.
- Migration 20261005062052_RepairLifecycleAudit applicata; nessuna differenza pendente
  tra modello Code First e migrazioni.
- TDD: sei nuovi casi Domain, sette Application e dieci SQL osservati in RED,
  poi GREEN. Totale 83 superati, nessuno ignorato, connessione letta dal secret locale.
- SQL verificato: replay anche dopo successive transizioni, storico ripetuto,
  carichi/timestamp, target/stati/scope invalidi, versione superata, rollback se
  carico persistito incoerente; doppia riassegnazione simultanea e collaudo contro
  riassegnazione su snapshot identici. Il collaudo positivo può rendere la successiva
  riassegnazione non più valida; nessuna operazione perde o duplica il carico.
- Fixture sintetiche: nessun dato reale toccato, rimozione solo dei tenant generati.
- Avvio lavoro con controllo Quotes/Payments resta un comportamento di dominio:
  il relativo comando persistente sarà collegato al preventivo accettato/acconto.
  I test preparano stati sintetici InProgress/AwaitingTesting come fixture esplicite.
- Il ritorno al lavoro riguarda rilavorazione entro l'autorizzazione esistente,
  non autorizza nuovi guasti/lavori fuori preventivo. Revisioni restano da implementare.
