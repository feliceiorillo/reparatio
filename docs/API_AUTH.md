# API e provider locale

Provider ASP.NET Core Identity + OpenIddict 7.7.1. Database Reparatio, contesto
IdentityStore separato e schema `identity`. Migrazioni applicate con Code First;
nessun EnsureCreated, account predefinito o password nel codice.

## Avvio locale

Da C:\Users\felice\Documents\Codex\reparatio, con le variabili cache .NET/NuGet
già usate nel progetto:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Reparatio.Repairs.Api --no-restore
```

HTTPS https://localhost:7240. Serve un certificato HTTPS di sviluppo attendibile
sul PC. Il secret SQL esistente viene letto automaticamente. Le chiavi di firma,
cifratura e cookie sono persistenti, protette con DPAPI CurrentUser sotto
Documents\Codex\.secrets\reparatio; non nel repository. I certificati locali
durano due anni. La configurazione attuale rifiuta l'ambiente Production:
prima del deployment servono gestione e rotazione dei certificati di produzione.

`scripts/database.ps1 -Action Update` aggiorna Repairs e IdentityStore;
`-Action Script` genera due script idempotenti. AddMigration riguarda Repairs;
per Identity usare `dotnet ef migrations add NOME --project src/Reparatio.Repairs.Api --context IdentityStore`.

## Protocollo

- Discovery: `/.well-known/openid-configuration`.
- Authorization: `/connect/authorize`; token: `/connect/token`.
- Client pubblico first party: `reparatio-web`, Authorization Code, PKCE obbligatorio.
- Callback consentita: `https://localhost:4200/auth/callback`, configurabile tramite
  `Identity:RedirectUri`. Issuer fisso `https://localhost:7240/`, configurabile tramite
  `Identity:Issuer`; usare HTTPS e aggiornare coerentemente i collegamenti staff.
- Scope: `openid profile reparatio.api`; audience API: `reparatio-api`.
- Token di accesso dieci minuti, firmati e cifrati; validazione locale OpenIddict
  e controllo della registrazione del token nel database.
- Nessun grant password, client credentials o refresh token in questo incremento.
- Le API accettano soltanto Bearer con scope API; il cookie di login non basta.
- Identità emittente/subject collegate esplicitamente a StaffIdentities, mai tramite
  semplice coincidenza di email. Le autorizzazioni sono StaffGrants nel database.

Le pagine standard Identity includono login, registrazione, conferma account,
recupero password e gestione account, con protezione antiforgery. Account con
email non confermata o bloccato non ottiene nuovi codici/token. Nessun collegamento
di conferma demo è mostrato dalla registrazione. Per inviare conferme e recuperi
configurare REPARATIO_SMTP_HOST, REPARATIO_SMTP_FROM, REPARATIO_SMTP_PORT (587
predefinito), REPARATIO_SMTP_USER e REPARATIO_SMTP_PASSWORD nel processo locale,
fuori dal repository. TLS SMTP obbligatorio. Senza SMTP la consegna fallisce e
l'account resta non confermato. Consegna reale delle email non ancora verificata.

## Permessi

| Ruolo | Ricezione/lettura | Disponibilità/riassegnazione | Collaudo/ritorno al lavoro |
| --- | --- | --- | --- |
| TenantAdministrator | Tutte le sedi del proprio tenant | Sì | Sì |
| SiteManager | Sede autorizzata | Sì | Sì |
| Receptionist | Sede autorizzata | No | No |
| Technician | Lettura delle pratiche assegnate al tecnico collegato | No | Solo pratiche assegnate |

Role nel JWT non concede diritti. StaffIdentities deve essere attiva. I grant
sono riletti prima di ogni comando, anche dei replay; revocarli impedisce la
richiesta successiva senza attendere la scadenza del token. Un comando già
autorizzato e in esecuzione non viene annullato retroattivamente. Disabilitare
StaffIdentities revoca l'accesso staff; logout Identity elimina il cookie ma
non revoca automaticamente i token già emessi. Nessun bypass platform admin.
ActorId dello storico proviene dall'identità autorizzata, non dal payload.

## Endpoint

Prefisso: `/api/tenants/{tenantId}/sites/{siteId}`.

| Metodo | Percorso | Corpo |
| --- | --- | --- |
| GET | /repairs | —, prime 100 pratiche in ordine d'arrivo |
| GET | /repairs/{repairId} | — |
| POST | /repairs | RequestId, RepairId |
| POST | /technicians/{technicianId}/availability | RequestId, IsAvailable |
| POST | /repairs/{repairId}/reassign | RequestId, TechnicianId, Reason |
| POST | /repairs/{repairId}/submit-testing | RequestId |
| POST | /repairs/{repairId}/testing | RequestId, Passed obbligatorio, Notes facoltativo |
| POST | /repairs/{repairId}/return-work | RequestId, Reason |

Tenant/sede/pratica sono vincolati alla route. Campi JSON sconosciuti rifiutati,
compreso ActorId. Risposte: 401 autenticazione, 403 permessi, 404 pratica assente
nel contesto autorizzato, 400 dati, 409 conflitto/stato. Errori interni senza
dettagli SQL nella risposta. I comandi conservano transazioni/idempotenza/retry
dei servizi Application esistenti.

## Verifica e limiti

Test reale con Identity login e antiforgery, authorization code + PKCE e token
OpenIddict: accesso Bearer, isolamento tenant, rifiuto cookie/token alterato,
callback non registrata, PKCE assente, codice riutilizzato, campi extra, replay
e revoca grant. Riassegnazione, invio a collaudo, collaudo positivo e ritorno al
lavoro via HTTP verificano carichi e ActorId SQL. Quattro casi ruoli/scope sul SQL
reale verificano anche tecnico non assegnato, altra sede e identità disattivata.
Gli utenti/tenant sintetici sono rimossi; il client OpenIddict configurato resta.

Restano onboarding tenant/sedi e primo titolare, inviti/gestione grant staff,
Angular e callback client effettiva, Google per i clienti, portale clienti,
consegna SMTP e configurazione di produzione. Nessun account reale né grant
amministrativo è stato creato automaticamente. L'avvio lavorazione resta nel
dominio: non è esposto finché Quotes/Payments non producono l'autorizzazione
fidata. Il test HTTP prepara quello stato tramite la propria fixture SQL.
