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
L'adattatore nei test verifica questo contratto; SQL Server ed EF Core restano da fare.

Responsabilità previste: SQL Server/EF Core per dati e transazioni; Identity & Access
per autenticazione, JWT e autorizzazione; RabbitMQ per eventi tra contesti con outbox
transazionale; Notifications per email; Redis soltanto per cache/dati temporanei,
mai fonte autorevole per carico o incassi. Docker per ambienti ripetibili, Kubernetes
quando esisteranno servizi da distribuire. Nessuna integrazione aggiunta ora.
