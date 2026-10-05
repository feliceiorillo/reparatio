Stiamo sviluppando Reparatio, un progetto personale SaaS multi tenant per negozi che riparano smartphone. Agisci come collaboratore di sviluppo: lavora direttamente sul mio PC, procedendo con DDD e TDD.

CARTELLA E REPOSITORY
- Il progetto deve stare nella cartella Documenti\codex\reparatio del mio PC. Risolvi il percorso reale di Documenti, anche se reindirizzato su OneDrive.
- Ho installato manualmente il SDK .NET: verifica versione e percorso.
- Nella precedente sessione il progetto è stato creato in un workspace remoto, non sul PC.
- È disponibile l’archivio reparatio.zip, che contiene sorgenti e repository .git con due commit sul branch main.
- Se il progetto è già estratto, continua da quello senza sovrascrivere modifiche.
- Se manca, chiedimi l’archivio o il suo percorso. Non presumere di poter accedere al workspace della precedente sessione.
- Mantieni Git e aggiorna documentazione e stato del lavoro.
- Quando mi indichi i file, usa percorsi locali completi, non soltanto link.

OBIETTIVO
Gestire il dispositivo dalla consegna al ritiro:
accettazione → diagnosi → preventivo → decisione cliente → eventuale acconto → riparazione → collaudo → saldo e ritiro.

TECNOLOGIE E METODO
- C#/.NET; il progetto iniziale usa net10.0.
- Clean Architecture, DDD, CQRS e microservizi.
- TDD: test che fallisce, implementazione minima, refactoring.
- Microsoft SQL Server ed Entity Framework Core.
- Docker e Kubernetes.
- JWT con ruoli e identity provider custom.
- Redis e RabbitMQ.
- Angular nell’ultima versione stabile disponibile, da verificare prima dello scaffolding.
- Componenti Angular distinti e riutilizzabili; servizi dedicati per API e logica applicativa.
- Definisci responsabilità concrete per le tecnologie, senza aggiungere complessità priva di utilità.

TENANT E SEDI
- Il titolare si registra autonomamente e crea l’azienda.
- Ogni tenant può avere più sedi già nell’MVP.
- Ogni sede riceve, ripara e riconsegna i propri dispositivi.
- Nessun trasferimento tra sedi nell’MVP.
- Isolamento dei dati tra tenant.
- Personale autorizzato su una o più sedi.
- Clienti e storico condivisi tra sedi dello stesso tenant: modello proposto da mantenere coerente con i permessi.
- Ruoli proposti: titolare/amministratore tenant, responsabile di sede, addetto all’accettazione, tecnico.
- L’amministratore della piattaforma è distinto dall’amministratore del tenant.

CLIENTE E AUTENTICAZIONE
- Registrazione autonoma con email/password oppure Google.
- Google è l’unico provider esterno nell’MVP; per l’autenticazione federata usa OpenID Connect sopra OAuth2.
- Il cliente consulta esclusivamente le proprie pratiche.
- Non crea o modifica le pratiche.
- Può accettare o rifiutare preventivi, incluse le revisioni, e pagare online.
- Account cliente unico tra negozi e collegamento verificato alle pratiche sono proposte, non decisioni tecniche definitive.
- Il negozio deve poter accettare dispositivi anche da clienti non ancora registrati.
- Non collegare pratiche a un account basandoti soltanto su un’email non verificata.
- Il perimetro completo dell’identity provider custom va progettato: non implementare protocolli o crittografia artigianali.

PRATICA
- Dati previsti: cliente, marca/modello, IMEI o seriale se disponibile, problema dichiarato, accessori consegnati, foto iniziali.
- Diagnosi, attività svolte, componenti utilizzati, collaudo e consegna.
- Costo di diagnosi escluso dall’MVP.
- Preventivo iniziale rifiutato: dispositivo da restituire senza riparazione, senza costo di diagnosi.
- Revisione rifiutata: nessun lavoro aggiuntivo autorizzato; gestione esplicita di lavoro già svolto e acconti.
- Collaudo fallito: ritorno in lavorazione.
- Mantieni distinti stato della riparazione, stato del preventivo e stato del pagamento.

ASSEGNAZIONE DEI TECNICI — REGOLE CONFERMATE
- Assegnazione automatica all’accettazione.
- Considerare soltanto tecnici disponibili dello stesso tenant e della stessa sede.
- Scegliere chi ha meno pratiche nel carico.
- Le pratiche sospese/in attesa del cliente, acconto o ricambio contano nel carico.
- Dopo collaudo positivo la pratica non conta più, anche se attende saldo o ritiro.
- Se torna in lavorazione ricomincia a contare.
- A parità di carico scegliere chi non riceve assegnazioni da più tempo.
- Nell’implementazione iniziale: mai assegnati prima degli altri; ulteriore parità risolta per Guid stabile.
- Senza tecnici disponibili: pratica in attesa di assegnazione.
- Assegnazione automatica quando torna disponibile un tecnico.
- Ordine di arrivo, senza urgenze.
- Le pratiche temporaneamente bloccate non dovrebbero impedire la lavorazione delle successive: proposta da rendere esplicita.
- Il responsabile può riassegnare a un tecnico della stessa sede per assenze o impedimenti.
- Tracciare tecnico precedente, nuovo tecnico, autore, data e motivo.

LISTINO E PREVENTIVI
- Listino unico del tenant con voci di ricambi e manodopera.
- Prezzi stabiliti dall’amministratore del tenant.
- Il tecnico seleziona le voci necessarie e genera/invia il preventivo tramite pulsante nell’app.
- Nessuna approvazione preventiva del responsabile.
- Il tecnico non modifica prezzi e non applica sconti.
- Sconti esclusi dall’MVP.
- Solo listino: scorte, movimenti e acquisti di magazzino arrivano dopo.
- Possibile indicare attesa ricambio senza disponibilità automatica.
- Preventivi versionati, con descrizioni e prezzi conservati per ogni versione.
- Modifiche al listino non alterano preventivi già emessi.
- Nuovi guasti possono richiedere una revisione, con nuova accettazione prima del lavoro aggiuntivo.
- Nessuna scadenza del preventivo nell’MVP.

PAGAMENTI
- Percentuale di acconto configurata dall’amministratore tenant, da 0% a 100%, default 0%.
- 0%: dopo l’accettazione si può lavorare senza pagamento anticipato.
- Percentuale positiva: acconto online obbligatorio dopo accettazione; lavoro bloccato fino alla conferma del pagamento.
- 100%: pagamento completo anticipato.
- Saldo online oppure contanti al ritiro; il personale registra l’incasso cash.
- Approvazione, pagamento e riparazione sono stati distinti.
- Regola proposta: percentuale congelata sul preventivo accettato; modifiche successive del tenant non alterano accordi esistenti.
- Per revisioni, proposta:
  integrazione = max(0, nuovo totale × percentuale concordata − importo già pagato).
- Esempio: 200 €, 30%, pagati 60 €; revisione a 250 € → integrazione 15 €.
- Pagamenti già effettuati restano tracciati.
- Rifiuto di revisione non genera automaticamente rimborso.
- Provider, routing degli incassi ai tenant, rimborsi e dettagli fiscali restano da definire prima dell’integrazione reale.

NOTIFICHE
- Email a ogni cambio di stato visibile al cliente.
- Link diretto alla pratica nel portale.
- Se necessario, login e poi ritorno alla pratica.
- Verificare sempre che il cliente sia autorizzato alla pratica.

DEMO GUIDATA
- Ambiente demo separato con profili selezionabili:
  titolare, accettazione, tecnico, cliente.
- Percorso guidato attraverso una riparazione completa.
- Scenari aggiuntivi: rifiuto, revisione, pagamento fallito, collaudo fallito.
- Pulsante “Ripristina demo”.
- Pagamenti simulati/test ed email non inviate a persone reali.
- Scelta rapida dei profili soltanto in demo, non nell’app ordinaria.
- Dataset proposto:
  2 tenant (“DemoPhone”, “SmartLab”), 3 sedi, 10 utenti interni,
  20 clienti, 25 dispositivi, 30 pratiche.
- Dati inventati, email example.com, identificativi sintetici.
- Ricambi e prezzi fittizi; nessun magazzino richiesto.
- Includere scenari acconto 0% e 30%, revisione 200→250 €,
  saldo online/cash e isolamento tenant/sedi.
- Google reale richiede account di test validi; niente identità provider inventate.

DDD INIZIALE
Bounded context proposti, non ancora confini definitivi di deployment:
1. Identity & Access
2. Tenant Management
3. Repairs
4. Catalog & Quotes
5. Payments
6. Notifications

STATO DEL CODICE NELL’ARCHIVIO
- Reparatio.slnx
- Directory.Build.props: net10.0, nullable, implicit usings, warnings as errors.
- src/Reparatio.Repairs.Domain:
  - Reparatio.Repairs.Domain.csproj
  - Repair.cs
  - TechnicianAssignmentPolicy.cs
- tests/Reparatio.Repairs.Domain.Tests:
  - Reparatio.Repairs.Domain.Tests.csproj
  - AssignmentTests.cs
- README.md
- docs/DOMAIN.md
- docs/REQUIREMENTS.md
- docs/STATUS.md
- .gitignore

Implementato:
- TechnicianCandidate: snapshot con identità, tenant, sede, disponibilità,
  numero pratiche nel carico e ultima assegnazione.
- TechnicianAssignmentPolicy.Select: filtro e ordinamento.
- Repair.Open: apertura con tecnico oppure attesa di assegnazione.
- RepairWorkload.Counts: classificazione degli stati per il carico.
- Stati iniziali parziali, non workflow completo.
- Nessuna Application, API, persistenza o UI ancora implementata.

TEST E LIMITI
- 17 casi previsti: 8 Fact e 9 casi Theory.
- Coprono minor carico, rotazione, mai assegnati, parità stabile,
  isolamento tenant/sede, indisponibilità, apertura, stati del carico e carico negativo.
- Test scritti prima dell’implementazione e conservati in un commit separato.
- Non sono mai stati compilati o eseguiti perché il vecchio ambiente non aveva SDK.
- Non dichiarare verificato il ciclo RED/GREEN precedente.
- NuGet configurati:
  Microsoft.NET.Test.Sdk 17.12.0
  xunit 2.9.2
  xunit.runner.visualstudio 2.8.2
  Anche il restore deve ancora essere verificato.
- La policy pura non gestisce concorrenza o persistenza.
- In Application bisognerà rendere consistente apertura, aggiornamento carico
  e ultima assegnazione; prevedere retry e idempotenza.
- FIFO delle pratiche in attesa non ancora implementato.

PRIME AZIONI
1. Individua Documenti\codex\reparatio e leggi eventuali AGENTS.md.
2. Verifica repository e modifiche esistenti.
3. Verifica dotnet --info.
4. Esegui restore e dotnet test Reparatio.slnx.
5. Correggi eventuali errori e riporta i risultati effettivi.
6. Continua con TDD per riassegnazione, collaudo e ritorno in lavorazione.
7. Poi affronta Application/CQRS e consistenza concorrente prima di integrare SQL Server.

COMUNICAZIONE
- Procedi autonomamente sulle attività già autorizzate.
- Fammi una domanda alla volta quando manca una decisione importante.
- Evita domande premature sui dettagli minori.
- Non confondere proposte con requisiti confermati.
- Riporta cosa è implementato, cosa è verificato e cosa resta da fare.
- Non dichiarare creati file sul PC se hai lavorato soltanto in un ambiente remoto.