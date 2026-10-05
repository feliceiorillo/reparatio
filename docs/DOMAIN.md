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
