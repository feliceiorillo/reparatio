# Stato del lavoro — 5 ottobre 2026

## Repository sul PC
- Percorso reale Documenti: C:\Users\felice\Documents.
- Progetto estratto da reparatio.zip in C:\Users\felice\Documents\Codex\reparatio.
- .git e i due commit originali su main conservati; archivio originale non modificato.
- Nessun progetto preesistente o AGENTS.md trovato nei percorsi controllati.
- SDK selezionato 10.0.401, eseguibile C:\Program Files\dotnet\dotnet.exe.

## Implementato
- Apertura e selezione automatica iniziali del tecnico.
- Riassegnazione nella stessa sede e tenant a tecnico disponibile, con storico immutabile
  di precedente/nuovo tecnico, autore, data e motivo. Respinti stesso tecnico,
  autore/motivo mancanti e pratiche senza lavoro pendente.
- Avvio lavoro con snapshot di preventivo accettato e importi acconto/incassi confermati.
- Invio al collaudo, esito positivo/negativo e ritorno in lavorazione.
- Application: comando di apertura, controllo accesso, ricevuta idempotente,
  controllo payload e retry limitato a cinque tentativi su versione concorrente.
- Contratto atomico per pratica, carico, ultima assegnazione e ricevuta.

## Verificato
- Restore NuGet riuscito e 17 test originali superati prima delle modifiche.
- Dominio: nuovi test prima del codice; RED di compilazione per API assente, poi GREEN.
- Application: otto test eseguiti e falliti con handler non implementato (RED), poi GREEN.
- Totale corrente: 27 casi Domain + 8 Application = 35 test superati, nessuno ignorato.
- Test concorrente con barriera: tre richieste leggono la stessa versione prima dei commit;
  due aperture distinte bilanciano il carico; ripetizione della stessa richiesta non duplica.
- La verifica usa un adattatore di storage soltanto nei test: nessuna garanzia SQL verificata.
- Il ciclo RED/GREEN della precedente sessione resta non verificato storicamente.

## Ambiente
`dotnet --info` legge SDK e runtime ma segnala accesso negato al Service Control Manager.
Restore e test funzionano usando MSBuildEnableWorkloadResolver=false e -m:1.
Accesso NuGet autorizzato; cache CLI/NuGet reindirizzate nella cartella di lavoro
C:\Users\felice\Documents\Codex\2026-10-05\hai\work.

## Limiti e prossimi passi
- IRepairAccess è un contratto: autenticazione e autorizzazione reali non implementate.
- RepairWorkAuthorization è un input interno fidato, non una prova di accettazione:
  deve essere costruito da dati Quotes/Payments verificati; non esporlo come input API.
- ReturnToWork è rilavorazione dopo collaudo; nuovo lavoro fuori preventivo richiederà
  revisione accettata e ulteriore controllo acconto, non ancora modellati.
- Storico riassegnazione in memoria, senza persistenza. L'autorizzazione del responsabile
  e l'aggiornamento atomico dei carichi nella riassegnazione restano in Application.
- SQL Server/EF Core: implementare tutti gli scrittori secondo il protocollo di versione
  tenant/sede, vincoli univoci e transazioni; verificare concorrenza su database reale.
- FIFO, assegnazione al ritorno di disponibilità, diagnosi dettagliata, preventivi,
  rifiuti, pagamenti, ritiro, API, frontend e demo ancora da implementare.
- Nessuna decisione su provider pagamenti o identità globale cliente è stata presa.
- Brief completo conservato in docs/PROJECT_BRIEF.md, con proposte distinte dai requisiti.
