# Lab 3: telemetria, due deploy, RBAC e Kubernetes

Mattina: finiamo OpenTelemetry e mandiamo la stessa immagine su Render **e** su Azure con un solo merge. Dopo pranzo: chi puo' fare cosa (RBAC) e Application Insights. Pomeriggio: Kubernetes con k3s.

**Regole**
- A coppie miste: in ogni coppia almeno una persona con Azure for Students. La parte Azure si fa sul suo repo, l'altra persona guida.
- Se restate indietro, ogni parte ha un branch con la soluzione:
  ```bash
  git remote add prof https://github.com/matteobortolazzo/shipboard-docker   # una volta sola
  git fetch prof
  git checkout prof/soluzione-otel -- src/ShipBoard.Api                                   # parte 1
  git checkout prof/soluzione-azure -- src/ShipBoard.Api .github/workflows/ci-api.yml     # parti 2 e 3
  git checkout prof/soluzione-k8s -- k8s                                                  # parte 4
  ```
  Prendete solo i percorsi indicati: il resto del vostro repo resta com'e'.
- Gli LLM servono per farsi spiegare un errore, non per scrivere la soluzione.

## 1. OpenTelemetry (fino alle 10:45)

L'AppHost di Aspire passa gia' all'API le variabili `OTEL_*`: basta il codice, la dashboard e' quella che usate con `dotnet run`.

1. Seguite la sezione 8 di [lab-2.md](lab-2.md), passi 2, 3, 5 e 6 (pacchetti alla versione `1.21.0`). Il passo 1, la dashboard in compose, non serve.
2. Avviate l'AppHost, create qualche ticket e trovate la traccia di `POST /tickets`: quanti span? C'e' la query SQL?
3. **Caccia allo span lento**: uno della coppia aggiunge `.WithEnvironment("DELAY_MS", "700")` all'API in `ShipBoard.AppHost/AppHost.cs` senza dirlo. L'altro, solo dalla dashboard, dice quale endpoint e' lento, in quale span e di quanto.

## 2. Un merge, due deploy (fino alle 12:30)

La CI pubblica l'immagine con due tag, lo SHA del commit e `latest`, e Render riceve lo SHA. Ora aggiungete Azure, **in parallelo**.

1. Azure (sul repo di chi ha la subscription):
   ```bash
   az login
   IMAGE=ghcr.io/<utente>/shipboard-docker-api:latest ./infra/app-service.sh
   REPO=<utente>/shipboard-docker NAME=shipboard-<utente> ./infra/github-oidc.sh
   ```
2. In `.github/workflows/ci-api.yml` aggiungete il job `deploy-azure` della sezione 7b di [lab-2.md](lab-2.md), con queste differenze:
   - `needs: api` (il job che pubblica l'immagine)
   - `if: github.event_name == 'push' && vars.AZURE_WEBAPP_NAME != ''`: chi non ha Azure resta verde
   - immagine: `ghcr.io/${GITHUB_REPOSITORY,,}-api:$GITHUB_SHA`
3. Verifica: una modifica visibile, PR, merge. Due URL, stesso SHA su `/info`.

Domande: se il deploy su Azure fallisce, Render si aggiorna lo stesso? Perche' deployare lo SHA e non `latest`? Come tornereste alla versione di ieri?

## 3. RBAC su Azure e Application Insights (fino alle 14:30)

RBAC risponde a tre domande: **chi** (un'identita'), **cosa** (un ruolo), **dove** (uno scope: una risorsa, un resource group, una subscription).

1. Chi puo' fare cosa:
   ```bash
   az role assignment list --assignee <AZURE_CLIENT_ID> --all -o table
   ```
   Che ruolo ha l'identita' della pipeline? Su quale scope?
2. Uscite dallo scope: nel job `deploy-azure`, dopo il login, aggiungete uno step `run: az group show -n rg-shipboard`. Merge. Che errore da'? Chi dovrebbe dare quel permesso, e lo dareste? Poi togliete lo step.
3. Application Insights: aggiungete il codice della sezione 10 di [lab-2.md](lab-2.md), poi
   ```bash
   NAME=shipboard-<utente> ./infra/app-insights.sh
   ```
   Merge, qualche richiesta, poi nel portale **Logs**: trovate lo span `carica-ticket` lento con una query KQL.
4. **Senza chiave**: la connection string contiene una chiave. Al suo posto usate l'identita' della web app:
   ```bash
   AUTH=managed-identity NAME=shipboard-<utente> ./infra/app-insights.sh
   ```
   Lo script da' alla web app il ruolo `Monitoring Metrics Publisher` **solo** su Application Insights. Nel codice:
   ```csharp
   builder.Services.AddOpenTelemetry().UseAzureMonitor(o =>
   {
       if (builder.Configuration["APPLICATIONINSIGHTS_AUTH"] == "managed-identity")
           o.Credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
   });
   ```
   (pacchetto `Azure.Identity`). Le identita' in gioco adesso sono due: quella della pipeline e quella dell'app. Disegnatele con i loro ruoli e scope.

## 4. Kubernetes con k3s (pomeriggio)

Usiamo **k3d**: e' k3s dentro Docker, con Traefik gia' pronto come Ingress.

```bash
curl -s https://raw.githubusercontent.com/k3d-io/k3d/main/install.sh | bash    # in WSL o Linux
k3d cluster create shipboard -p "8081:80@loadbalancer"
kubectl get nodes
```

### 4a. Le basi

1. Leggete i file in `k8s/`: per ognuno dite a voce cosa crea. Poi:
   ```bash
   kubectl apply -f k8s/
   kubectl get pods -w
   ```
   L'API puo' riavviarsi un paio di volte finche' PostgreSQL non e' pronto: guardate `RESTARTS`. E' un problema?
2. Aprite http://localhost:8081 e create un ticket. In alto c'e' il nome dell'host dell'API: e' il nome del pod.
3. `kubectl scale deployment shipboard-api --replicas=3` e ricaricate piu' volte: l'host cambia. Chi sceglie il pod?
4. **Self-healing**: il vicino fa `kubectl delete pod <un vostro pod>`. Voi guardate `kubectl get pods -w`. Cosa succede, e perche'?

Le immagini sono quelle del docente. Per usare le vostre cambiate `matteobortolazzo` in `k8s/api.yaml` e `k8s/web.yaml` (i pacchetti su GHCR devono essere pubblici).

### 4b. Rilascio, probe e RBAC

1. **Probe**: in `k8s/api.yaml` togliete i commenti a `readinessProbe` e `livenessProbe`, `kubectl apply -f k8s/api.yaml`. Qual e' la differenza tra le due?
2. **Rolling update**: prendete uno SHA dalla pagina Packages su GitHub e
   ```bash
   kubectl set image deployment/shipboard-api api=ghcr.io/<utente>/shipboard-docker-api:<sha>
   kubectl rollout status deployment/shipboard-api
   ```
   Poi un tag che non esiste (`:non-esiste`). Che stato hanno i pod nuovi? E quelli vecchi servono ancora traffico? Tornate indietro con `kubectl rollout undo deployment/shipboard-api`.
3. **RBAC**: create `k8s/rbac.yaml` con un `ServiceAccount` `lettore`, un `Role` che puo' solo `get`, `list` e `watch` sui pod, e un `RoleBinding` che li collega. Poi il quiz: prima rispondete, poi controllate.
   ```bash
   kubectl auth can-i list pods   --as=system:serviceaccount:default:lettore
   kubectl auth can-i delete pods --as=system:serviceaccount:default:lettore
   kubectl auth can-i get secrets --as=system:serviceaccount:default:lettore
   kubectl auth can-i list pods -n kube-system --as=system:serviceaccount:default:lettore
   ```
   Confronto con Azure: chi, cosa e dove sono qui?

Domande: cosa fa Kubernetes che compose non fa? E Render? Dove sta la password del database, e chi la puo' leggere?

## Fine giornata

```bash
k3d cluster delete shipboard
```
