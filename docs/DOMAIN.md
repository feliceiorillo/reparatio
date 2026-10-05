# Modello iniziale

Bounded context proposti: Identity & Access, Tenant Management, Repairs,
Catalog & Quotes, Payments, Notifications. Non sono ancora confini definitivi
di deployment.

Repair è la radice dell'aggregato iniziale. Tenant e sede sono immutabili.
TechnicianAssignmentPolicy seleziona uno snapshot eleggibile per tenant e sede,
ordinato per carico crescente, ultima assegnazione più remota (mai assegnati prima),
poi Guid per determinismo.

Le pratiche sospese contano nel carico; quelle con collaudo positivo non contano.
Gli stati proposti sono parziali: rifiuti, restituzioni e transizioni saranno
modellati con i prossimi comportamenti, senza setter pubblici.

La policy non aggiorna disponibilità o ultima assegnazione e non garantisce
atomicità. La futura applicazione deve aggiornare carico e timestamp nella stessa
operazione consistente dell'apertura, gestendo concorrenza, retry e idempotenza.
L'ordine FIFO delle pratiche in attesa richiede una coda persistente e non è
implementato dalla policy di selezione dei tecnici.

Disponibilità è un dato esplicito, distinto dal carico. Ruoli e tenant verranno
derivati dall'identità autenticata, non da valori liberamente inviati dal client.

## Evoluzione locale del 5 ottobre 2026
Repair espone comportamenti, senza setter pubblici: Reassign, StartWork,
SubmitForTesting, RecordTesting, ReturnToWork. I fallimenti di validazione non
modificano lo stato. Lo storico conserva record immutabili e non espone la lista mutabile.
Il collaudo positivo porta a ReadyForCollection, quello fallito a InProgress;
ReturnToWork reinserisce la pratica nel carico mantenendo il tecnico.

RepairWorkAuthorization contiene identificativo del preventivo accettato, acconto
richiesto e pagamenti confermati: è uno snapshot interno verificato dall'applicazione,
non sostituisce i bounded context Quotes/Payments e non deve arrivare dal client.
Il calcolo percentuale e la gestione revisioni non sono ancora implementati.

OpenRepairHandler separa comando e coordinamento dal dominio. IRepairAccess deve
usare l'identità autenticata. IRepairOpeningStore deve leggere snapshot consistenti
ed eseguire un commit atomico condizionato alla versione tenant/sede, con ricevuta
idempotente e unicità dell'identificativo pratica. Le altre operazioni che alterano
carico/disponibilità devono usare lo stesso protocollo. Cinque conflitti consecutivi
terminano con errore; il chiamante può riprovare con lo stesso RequestId.
La ricevuta deve restare disponibile per tutta la finestra di idempotenza concordata.
L'adattatore nei test verifica questo contratto; SqlRepairStore implementa ora questi contratti su SQL Server/EF Core.

Responsabilità previste: SQL Server/EF Core per dati e transazioni; Identity & Access
per autenticazione, JWT e autorizzazione; RabbitMQ per eventi tra contesti con outbox
transazionale; Notifications per email; Redis soltanto per cache/dati temporanei,
mai fonte autorevole per carico o incassi. Docker per ambienti ripetibili, Kubernetes
quando esisteranno servizi da distribuire. Nessuna integrazione aggiunta ora.

## Coda FIFO e disponibilità
ArrivalSequence viene allocato monotonico e univoco nella sede dalla transazione
che apre la pratica; non viene scelto dal client. Snapshot e conteggio delle pratiche
in attesa devono essere letti nella stessa versione dell'assegnazione.
Le nuove aperture non superano una coda esistente. L'adattatore dovrà inserire la
pratica nella coda e attivare il coordinatore per evitarne lo stallo.
WaitingAssignmentPolicy produce un piano senza mutare aggregati persistiti; tiene
conto del carico aggiornato dopo ogni scelta. Pratiche in attesa di ricambio, cliente
o acconto già assegnate non bloccano questa coda e restano incluse nel carico.

Il comando SetTechnicianAvailability cambia la disponibilità e applica il piano
nella stessa transazione. Un conflitto non deve lasciare effetti parziali. Tutti gli
scrittori condividono la versione tenant/sede usata da OpenRepair. Persistenza e concorrenza incrociata sono verificate con SQL Server reale.
Le ricevute sono distinte per tenant, tipo di comando e RequestId; il payload deve
restare identico. Disponibilità falsa non azzera il carico delle pratiche assegnate.
I contratti di autorizzazione sono punti di integrazione per l'identità autenticata,
non implementazioni complete dei permessi.

Il coordinatore futuro va chiamato dopo disponibilità, aperture che trovano coda,
variazioni di carico e recupero; eventi persistiti/outbox renderanno affidabile il
risveglio. Non esiste ancora un processo eseguibile in background.

## Persistenza Code First implementata
La sezione precedente descriveva il passaggio ancora da fare: SqlRepairStore ora
implementa apertura e disponibilità su SQL Server. InitialRepairs è applicata al
database Reparatio; nove test di integrazione verificano anche concorrenza incrociata,
isolamento e vincoli del database. Vedere PERSISTENCE.md per schema e comandi.
Riassegnazione e collaudo hanno ora comandi persistenti; tutti gli scrittori acquisiscono il gate della sede.

## Persistenza del ciclo di lavorazione completata
RepairState è uno snapshot interno per ricostituire l'aggregato dalle righe persistite;
non è un DTO da accettare dal client. Restore copia lo storico e verifica invarianti
su identificativi, stato e presenza del tecnico. Il dominio continua a controllare
riassegnazione e transizioni attraverso i suoi comportamenti.

RepairLifecycleHandler coordina i quattro comandi con autorizzazioni specifiche per
azione e pratica, actor autenticato, orologio server e retry idempotenti. SQL rivalida
il cambiamento sotto il gate condiviso della sede; storico e carichi sono atomici.
La rilavorazione rimette la pratica nel carico senza essere una nuova assegnazione.
ReturnToWork resta una rilavorazione sotto autorizzazione esistente: nuovo lavoro
fuori preventivo richiederà il flusso revisione/accettazione, non questo comando.
