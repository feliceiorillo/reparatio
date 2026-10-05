# Requisiti concordati

- Tenant registrati autonomamente; più sedi, senza trasferimenti di dispositivi nell'MVP.
- Clienti: email/password e Google; consultazione, accettazione/rifiuto preventivi, pagamento.
- Preventivi versionati da listino tenant: ricambi e manodopera stabiliti dall'amministratore.
  Invio diretto del tecnico; niente sconti o variazioni manuali dei prezzi.
- Acconto tenant 0–100%, default 0%; quando dovuto, pagamento online prima del lavoro.
  Saldo online o contanti al ritiro. Revisione con nuova approvazione.
- Costo di diagnosi e scorte di magazzino esclusi.
- Assegnazione al tecnico disponibile della sede con meno pratiche nel carico.
  Sospese incluse, collaudo positivo escluso; parità per ultima assegnazione più remota.
- Riassegnazione manuale del responsabile per assenze/impedimenti, con storico.
- Nessun tecnico disponibile: attesa. Ordine di arrivo, senza urgenze.
- Email ai cambi di stato visibili al cliente, link alla pratica dopo autenticazione.
- Demo guidata con profili selezionabili, dataset fittizio e pagamenti simulati.
- Angular ultima stabile da verificare, componenti riutilizzabili; Microsoft SQL Server.
- Clean Architecture, DDD, TDD, CQRS, microservizi, Docker, Kubernetes,
  JWT con ruoli, identity provider custom, Redis e RabbitMQ.

## Proposte da validare nell'implementazione
- Account cliente trasversale ai negozi, collegamento verificato alle pratiche.
- Mai assegnati prima degli altri; Guid come ultimo criterio di parità.
- Percentuale congelata sul preventivo, integrazione al netto dei pagamenti.
- Confini dei bounded context e modalità di incasso per tenant.

Provider di pagamento, routing degli incassi ai tenant, rimborsi e autorizzazioni
di dettaglio restano da definire prima dell'integrazione dei pagamenti.
