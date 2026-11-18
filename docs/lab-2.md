# Lab 2: dalla pipeline al cloud, e poi dentro l'app

Oggi ShipBoard fa il viaggio completo: Dockerfile, immagine costruita da GitHub Actions, pubblicata su GitHub Container Registry, in produzione su un hosting gratuito (Render per tutti, Azure App Service per chi ha la subscription). Poi guardiamo dentro l'app che gira, con OpenTelemetry e .NET Aspire.

**Regole**
- A coppie, una issue e una PR per esercizio. La CI deve restare verde.
- Ogni esercizio ha un branch con la soluzione (`step-06` ... `step-10`). Se restate indietro:
  ```bash
  git remote add prof https://github.com/matteobortolazzo/shipboard-docker   # una volta sola
  git fetch prof
  git checkout prof/step-07 -- .     # copia i file della soluzione nella vostra branch
  ```
- Gli LLM servono per farsi spiegare un errore, non per scrivere la soluzione. Tutto quello che serve oggi e' in questo file, nei link alla documentazione e negli script in `infra/`.

## 0. Riallineamento Docker (prima ora)

**Tutti**, prima di partire:

```bash
git fetch prof && git merge prof/main      # oppure: git checkout prof/main -- . se il merge litiga
docker login                               # senza login Docker Hub limita i download per tutta l'aula
docker compose up --build                  # http://localhost:8080, create un ticket
```

Se la volta scorsa non avete finito il Dockerfile, leggete adesso `src/ShipBoard.Api/Dockerfile` e `web/Dockerfile`: ogni riga ha un commento. Sapete dire a voce cosa fa ogni riga? Se si', siete pronti.

### Gioco: Dockerfile rotti

In `docs/esempi/dockerfile-rotti/` ci sono 7 Dockerfile per l'API, ognuno con un solo problema. Regole e punteggio nel [README](esempi/dockerfile-rotti/README.md).

```bash
./docs/esempi/dockerfile-rotti/verifica.sh 1
```

Avete 40 minuti: fate piu' casi possibile. Chi finisce prima: aiutate una coppia vicina **senza toccare la sua tastiera**, poi provate le sfide.

**Sfide per chi e' avanti**
- Aggiungete un `healthcheck` al servizio `api` in `compose.yaml` e fate dipendere `web` da `api` con `condition: service_healthy`. Problema: l'immagine chiseled non ha ne' shell ne' `curl`. Come fate?
- `docker scout cves shipboard-api` (o `trivy image shipboard-api`): quante vulnerabilita' ha la vostra immagine? E con l'immagine `aspnet:11.0` normale?

## 6. Immagini su GitHub Container Registry (step-06)

Obiettivo: a ogni push su `main`, la CI costruisce le immagini di API e frontend e le pubblica su `ghcr.io/<vostro-utente>/`.

1. In `.github/workflows/ci.yml` togliete il job `deploy` d'esempio (usa Azure Container Registry, che costa: oggi usiamo GHCR, gratis).
2. Aggiungete un job `image`. Completate i `___`:
   ```yaml
     image:
       if: github.event_name != 'pull_request' && github.ref == 'refs/heads/main'
       needs: ___                    # solo se build e test sono verdi
       runs-on: ubuntu-latest
       permissions:
         contents: read
         packages: ___               # cosa serve per pubblicare un pacchetto?
       steps:
         - uses: actions/checkout@v4
         # i nomi delle immagini devono essere minuscoli
         - run: echo "IMAGE_PREFIX=ghcr.io/${GITHUB_REPOSITORY_OWNER,,}" >> "$GITHUB_ENV"
         - uses: docker/login-action@v3
           with:
             registry: ghcr.io
             username: ${{ github.actor }}
             password: ${{ secrets.___ }}   # nessun secret da creare: esiste gia'
         - uses: docker/setup-buildx-action@v3
         - uses: docker/build-push-action@v6
           with:
             context: ___
             push: true
             build-args: APP_VERSION=${{ github.sha }}
             tags: |
               ${{ env.IMAGE_PREFIX }}/shipboard-api:${{ github.sha }}
               ${{ env.IMAGE_PREFIX }}/shipboard-api:latest
             cache-from: type=gha,scope=api
             cache-to: type=gha,mode=max,scope=api
         # ... e lo stesso per il frontend (context web, immagine shipboard-web)
   ```
   Documentazione: [Publishing Docker images](https://docs.github.com/en/actions/use-cases-and-examples/publishing-packages/publishing-docker-images).
3. Nel Dockerfile dell'API, nello stage finale, fate arrivare lo SHA a `/info`:
   ```dockerfile
   ARG APP_VERSION=dev
   ENV APP_VERSION=$APP_VERSION
   ```
4. PR, merge, guardate la run. Poi sul vostro profilo GitHub: **Packages**, `shipboard-api`, **Package settings**, **Change visibility: Public** (serve ad Azure per scaricarla senza password).
5. Verifica:
   ```bash
   docker run --rm -p 5080:8080 ghcr.io/<utente>/shipboard-api:latest
   curl localhost:5080/info        # "version" e' lo SHA del vostro commit
   ```
6. **Scambio**: fate girare l'immagine della coppia accanto. Che SHA ha? Corrisponde all'ultimo commit sul loro `main`?

Domande: perche' due tag (SHA e `latest`)? Quale usereste per un rollback? Perche' `packages: write` sta sul job e non in cima al file? La seconda run e' piu' veloce della prima: chi ha la cache?

## 7. Deploy gratuito (step-07)

Obiettivo: l'API gira su internet e ogni merge su `main` la aggiorna con l'immagine appena pubblicata. Tutti fanno **7a** (Render, gratis, senza carta di credito). Chi ha Azure for Students fa anche **7b**: in coppia basta che uno dei due ce l'abbia.

### 7a. Render, per tutti

1. Entrate su [render.com](https://render.com) con l'account GitHub.
2. **New, Web Service, Existing Image**: `ghcr.io/<utente>/shipboard-api:latest` (il pacchetto deve essere pubblico, esercizio 6).
3. Nome `shipboard-<utente>`, piano **Free**. In **Environment** aggiungete `PORT=8080`: Render inoltra il traffico su quella porta. In **Advanced**, health check path `/health`.
4. Create e aprite `https://shipboard-<utente>.onrender.com/info`. Il piano free si addormenta dopo 15 minuti senza richieste: la prima risposta puo' metterci un minuto.
5. Pipeline. Su Render, **Settings, Deploy Hook**: copiate l'URL. In GitHub, **Settings, Secrets and variables, Actions**:
   - secret di repository `RENDER_DEPLOY_HOOK_URL` con l'URL del deploy hook
   - variabile di repository `RENDER_URL`, es. `https://shipboard-mario.onrender.com`
6. Aggiungete il job dopo `image`. Completate i `___`:
   ```yaml
     deploy-render:
       if: vars.RENDER_URL != ''          # chi non ha configurato Render non fa fallire la CI
       needs: ___
       runs-on: ubuntu-latest
       environment:
         name: render
         url: ${{ vars.RENDER_URL }}/info
       steps:
         - name: Deploy
           env:
             HOOK: ${{ ___.RENDER_DEPLOY_HOOK_URL }}
           run: |
             IMAGE="ghcr.io/${GITHUB_REPOSITORY_OWNER,,}/shipboard-api:$GITHUB_SHA"
             curl -fsS -X POST "$HOOK&imgURL=$(jq -rn --arg v "$IMAGE" '$v|@uri')"
   ```
   Il parametro `imgURL` dice a Render quale tag usare: quello con lo SHA, non `latest`.
7. Verifica: cambiate qualcosa di visibile, PR, merge. Dopo qualche minuto `/info` ha lo SHA del commit nuovo. Bonus: aggiungete uno step di smoke test che aspetta lo SHA su `/info` e fallisce dopo 6 minuti.

Bonus, frontend: secondo Web Service da `ghcr.io/<utente>/shipboard-web:latest`, piano Free, con `PORT=8080` e `API_URL=https://shipboard-<utente>.onrender.com` (l'URL dell'API, senza `/` finale). Niente CORS: il browser parla solo con il frontend, ed e' nginx a inoltrare `/api` all'API.

**Rompetelo apposta** (una cosa alla volta, poi rimettete a posto):
- Nel `curl`, cambiate `shipboard-api` in `shipboard-web`. Cosa risponde Render? Perche' e' una buona cosa?
- Su Render rigenerate il deploy hook e rilanciate la pipeline senza aggiornare il secret. Cosa succede? Quando lo fareste davvero?

Domande: l'URL del deploy hook e' una password? Chi lo ha cosa puo' fare? Perche' e' un secret e `RENDER_URL` una variabile?

### 7b. Azure App Service con OIDC (con la subscription)

Stessa cosa, ma senza nessuna password: GitHub dimostra ad Azure chi e' con un token OIDC firmato, Azure decide cosa puo' fare con RBAC.

```bash
az login
az account show                            # la subscription giusta?
IMAGE=ghcr.io/<utente>/shipboard-api:latest ./infra/app-service.sh
REPO=<utente>/shipboard-docker NAME=shipboard-<utente> ./infra/github-oidc.sh
```

Leggete gli script mentre girano. `app-service.sh` crea resource group, piano **F1** (gratis) e web app con `WEBSITES_PORT=8080`. `github-oidc.sh` crea una **Managed Identity** che si fida solo dei job del vostro repo nell'environment `production`, le da' il ruolo `Website Contributor` **solo** sulla vostra web app e salva le variabili su GitHub (con la `gh` CLI, altrimenti ve le stampa: `AZURE_WEBAPP_NAME` va tra le variabili di repository, le altre nell'environment `production`).

Se il piano F1 da' errore: la subscription ne ha gia' uno in quella regione, oppure la regione non e' permessa. Riprovate con `LOCATION=italynorth` (o `swedencentral`).

Aggiungete il job `deploy-azure` dopo `image`. Completate i `___`:

```yaml
  deploy-azure:
    if: vars.AZURE_WEBAPP_NAME != ''
    needs: image
    runs-on: ubuntu-latest
    environment:
      name: ___
      url: https://${{ vars.AZURE_WEBAPP_NAME }}.azurewebsites.net/info
    permissions:
      contents: read
      id-token: ___                 # per chiedere a GitHub il token OIDC
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - run: echo "IMAGE=ghcr.io/${GITHUB_REPOSITORY_OWNER,,}/shipboard-api:$GITHUB_SHA" >> "$GITHUB_ENV"
      - uses: azure/webapps-deploy@v3
        with:
          app-name: ${{ vars.AZURE_WEBAPP_NAME }}
          images: ${{ env.___ }}
```

**Rompetelo apposta**:
- Rinominate l'environment nel job in `prod`. Che errore da' `azure/login`? Perche'?
- Togliete `id-token: write`. Che errore da'?

Domande: confrontate con Render. Dove sono le password? Cosa puo' fare un attaccante che ruba `AZURE_CLIENT_ID`? E uno che ruba il deploy hook di Render? Cosa potrebbe fare la Managed Identity se le aveste dato `Contributor` sulla subscription?

## 8. OpenTelemetry (step-08)

Obiettivo: vedere cosa succede **dentro** l'API: tracce (il percorso di una richiesta), metriche (numeri nel tempo), log (collegati alle tracce).

1. Dashboard in locale. Aggiungete a `compose.yaml`:
   ```yaml
     dashboard:
       image: mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5
       environment:
         ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"
       ports:
         - "18888:18888"
   ```
   e all'`api` le variabili `OTEL_SERVICE_NAME: shipboard-api` e `OTEL_EXPORTER_OTLP_ENDPOINT: http://dashboard:18889`.
2. Pacchetti per l'API:
   ```bash
   cd src/ShipBoard.Api
   dotnet add package OpenTelemetry.Extensions.Hosting --version 1.20.1
   dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol --version 1.20.1
   dotnet add package OpenTelemetry.Instrumentation.AspNetCore --version 1.20.2
   dotnet add package OpenTelemetry.Instrumentation.Http --version 1.20.2
   ```
3. In `Program.cs` (con `using OpenTelemetry;`, `using OpenTelemetry.Metrics;`, `using OpenTelemetry.Trace;` in cima), prima di `builder.Build()`:
   ```csharp
   builder.Logging.AddOpenTelemetry(o => { o.IncludeFormattedMessage = true; o.IncludeScopes = true; });
   builder.Services.AddOpenTelemetry()
       .WithTracing(t => t
           .AddSource("ShipBoard.Api")
           .AddSource("Npgsql")
           .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/health")
           .AddHttpClientInstrumentation())
       .WithMetrics(m => m
           .AddMeter("ShipBoard.Api")
           .AddAspNetCoreInstrumentation());
   if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
       builder.Services.AddOpenTelemetry().UseOtlpExporter();
   ```
4. `docker compose up --build`, create qualche ticket, aprite http://localhost:18888. Trovate la traccia di `POST /tickets`: quanti span? C'e' la query SQL? Quanto dura?
5. Telemetria vostra. Create `src/ShipBoard.Api/Telemetry.cs` con un `ActivitySource` e un `Meter` chiamati `ShipBoard.Api`, poi:
   - in `GET /tickets`: uno span `carica-ticket` con `Telemetry.Source.StartActivity("carica-ticket")` e un tag con il numero di ticket trovati
   - in `POST /tickets`: un contatore `shipboard.tickets.created` con la priorita' come tag, e un log strutturato `"Creato il ticket {TicketId} con priorita' {Priority}"`
   - in `GET /tickets`: se la configurazione ha `DELAY_MS`, `await Task.Delay(...)` dentro lo span
6. Verifica: nella dashboard il grafico di `shipboard.tickets.created` diviso per priorita'; nei log strutturati filtrate per `TicketId`; dal log saltate alla traccia.

**Caccia allo span lento**: uno della coppia imposta `DELAY_MS` in `compose.yaml` senza dirlo e riavvia. L'altro, **solo dalla dashboard**, deve dire quale endpoint e' lento, in quale span e di quanto. Poi si scambiano.

Domande: che differenza c'e' tra un log e uno span? Perche' filtriamo `/health` dalle tracce? Cosa succede alla telemetria se la dashboard e' spenta?

## 9. .NET Aspire (step-09)

Obiettivo: un solo `dotnet run` che avvia PostgreSQL, API e frontend, con la stessa dashboard.

1. Create `src/ShipBoard.AppHost/ShipBoard.AppHost.csproj`:
   ```xml
   <Project Sdk="Aspire.AppHost.Sdk/13.6.5">
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net11.0</TargetFramework>
       <ImplicitUsings>enable</ImplicitUsings>
       <Nullable>enable</Nullable>
     </PropertyGroup>
     <ItemGroup>
       <PackageReference Include="Aspire.Hosting.PostgreSQL" Version="13.6.5" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="..\ShipBoard.Api\ShipBoard.Api.csproj" />
     </ItemGroup>
   </Project>
   ```
   e aggiungetelo alla solution: `dotnet sln add src/ShipBoard.AppHost`.
2. `AppHost.cs`, la stessa architettura di `compose.yaml` ma in C#:
   - `AddPostgres("postgres").WithDataVolume().WithPgAdmin()` e `.AddDatabase("shipboard")`
   - `AddProject<Projects.ShipBoard_Api>("api").WithReference(db).WaitFor(db)`
   - `AddDockerfile("web", "../../web")` con `.WithHttpEndpoint(targetPort: 8080)`, `API_URL` uguale a `api.GetEndpoint("http")`, `.WithExternalHttpEndpoints()`
3. `dotnet run --project src/ShipBoard.AppHost` e aprite il link della dashboard. Avete cambiato una riga dell'API per farla funzionare con Aspire? Perche' no?
4. Mettete `.WithEnvironment("DELAY_MS", "500")` sull'API e ritrovate lo span lento.

Domande: cosa fa Aspire che compose non fa, e viceversa? Quale dei due usereste in produzione? In un'azienda con 10 servizi, dove mettereste la configurazione di OpenTelemetry per non ripeterla 10 volte? (cercate "Aspire ServiceDefaults")

## 10. Application Insights (step-10, con Azure)

La stessa telemetria, ma dall'app su Azure. Serve l'App Service dell'esercizio 7b: chi non ha Azure lo fa in coppia con chi ce l'ha, o lo segue alla demo del docente.

1. `dotnet add src/ShipBoard.Api package Azure.Monitor.OpenTelemetry.AspNetCore --version 1.6.0`
2. In `Program.cs`, se c'e' `APPLICATIONINSIGHTS_CONNECTION_STRING` usate `UseAzureMonitor()` al posto di `UseOtlpExporter()`.
3. `NAME=shipboard-<utente> ./infra/app-insights.sh`, poi merge per fare il deploy del codice nuovo.
4. Portale, Application Insights, **Logs**:
   ```kusto
   requests | summarize count() by name, resultCode
   dependencies | where name == "carica-ticket" | project timestamp, duration
   customMetrics | where name == "shipboard.tickets.created"
   ```

## Fine giornata

```bash
az group delete -n rg-shipboard --yes --no-wait
```

Oppure tenetelo: il piano F1 e' gratis, come il servizio free su Render. Application Insights costa solo oltre la quota gratuita, ma cancellatelo se non lo usate.
