# eShop Reference Application - "AdventureWorks"

A reference .NET application implementing an e-commerce website using a services-based architecture with [Aspire](https://aspire.dev/).

![eShop Reference Application architecture diagram](img/eshop_architecture.png)

![eShop homepage screenshot](img/eshop_homepage.png)

## Getting Started

This version of eShop is based on .NET 10.

Previous eShop versions:

* [.NET 8](https://github.com/dotnet/eShop/tree/release/8.0)

### Prerequisites

1. Install a [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) that satisfies [`global.json`](global.json).
2. Install the [Aspire CLI](https://aspire.dev/get-started/install-cli/) and verify that it is available:

    ```console
    aspire --version
    ```

3. Install and start an OCI-compatible container runtime. [Docker Desktop](https://www.docker.com/products/docker-desktop/) is the recommended default. [Podman](https://podman.io/docs/installation) 5.0 or later is also supported; see [Using Podman](#using-podman) below.
4. Clone the repository:

    ```console
    git clone https://github.com/dotnet/eShop.git
    cd eShop
    ```

No separate Aspire workload or Visual Studio component is required; the AppHost SDK and hosting integrations are referenced by the projects in this repository.

#### Using Podman

Use these steps instead of Docker Desktop. On Windows and macOS, Podman runs containers inside a Linux virtual machine called a Podman machine.

1. Verify that Podman 5.0 or later is installed:

    ```console
    podman --version
    ```

2. Create the Podman machine (one-time setup). eShop runs several containers, including PostgreSQL, Redis, and RabbitMQ, so allocate at least 8 GB of memory:

    ```console
    podman machine init --memory 8192
    ```

    If you already have a machine with less memory, resize it:

    ```console
    podman machine stop
    podman machine set --memory 8192
    ```

3. Start the Podman machine. It does not start automatically, so run this command after every reboot:

    ```console
    podman machine start
    ```

4. Confirm that Podman accepts connections. The command should print a table header (`CONTAINER ID  IMAGE ...`), not a `Cannot connect to Podman` error:

    ```console
    podman ps
    ```

5. Tell Aspire to use Podman. Aspire uses Docker by default when both runtimes are installed, so set the `ASPIRE_CONTAINER_RUNTIME` environment variable to `podman` and then open a new terminal:

    ```powershell
    # Windows (PowerShell)
    [Environment]::SetEnvironmentVariable('ASPIRE_CONTAINER_RUNTIME', 'podman', 'User')
    ```

    ```bash
    # macOS / Linux: add this line to your shell profile (for example, ~/.zshrc or ~/.bashrc)
    export ASPIRE_CONTAINER_RUNTIME=podman
    ```

6. From the repository root, verify the environment:

    ```console
    aspire doctor
    ```

    Under **Container Runtime**, the output should look like `✅ Podman v5.x.x: running (configured via ASPIRE_CONTAINER_RUNTIME=podman) ← active`.

| Symptom | Fix |
| --- | --- |
| `Cannot connect to Podman` or `connection refused` | The machine is stopped. Run `podman machine start`. |
| `aspire doctor` shows Docker as active | Set `ASPIRE_CONTAINER_RUNTIME=podman` and open a new terminal. |
| Containers exit unexpectedly or start slowly | Increase the machine's memory (see step 2). |

For more information, see the [Aspire prerequisites](https://aspire.dev/get-started/prerequisites/).

#### Optional IDE setup

- [Visual Studio](https://visualstudio.microsoft.com/vs/) with the `ASP.NET and web development` workload.
- [Visual Studio Code with C# Dev Kit](https://code.visualstudio.com/docs/csharp/get-started) and the [Aspire extension](https://aspire.dev/get-started/aspire-vscode-extension/).
- The [.NET MAUI workload](https://learn.microsoft.com/dotnet/maui/get-started/installation) if you want to run the client apps.

### Running the solution

> [!WARNING]
> Ensure that your container runtime is running before starting eShop. If you use Podman, run `podman machine start` first.

#### From the terminal

From the repository root, run:

```console
aspire run
```

The root [`aspire.config.json`](aspire.config.json) selects `src/eShop.AppHost/eShop.AppHost.csproj`, avoiding ambiguity with the test AppHosts in the repository. When startup completes, the CLI prints a dashboard URL similar to:

```text
Dashboard: https://localhost:<port>/login?t=<token>
```

Press <kbd>Ctrl</kbd>+<kbd>C</kbd> to stop the AppHost. See the [`aspire run` command](https://aspire.dev/reference/cli/commands/aspire-run/) for additional options.

To run the AppHost in the background instead:

```console
aspire start
aspire ps
```

When you are finished, run `aspire stop`. See the [`aspire start` command](https://aspire.dev/reference/cli/commands/aspire-start/) for details.

#### From Visual Studio

1. Open `eShop.Web.slnf`.
2. Set `src/eShop.AppHost/eShop.AppHost.csproj` as the startup project.
3. Press <kbd>Ctrl</kbd>+<kbd>F5</kbd> to start eShop and open the Aspire dashboard.

### Running tests

Run the server tests:

```powershell
dotnet test --solution eShop.Web.slnf
```

Run the Playwright browser journeys. Playwright starts the AppHost automatically, so ensure your container runtime is running first.

```powershell
npm ci
npx playwright install chromium
npm run test:e2e
```

The rules journeys (`PromotionTest`, `PartialFulfilmentTest`) publish and then restore rules through BusinessRules.API as `USERNAME1`. They read optional `IDENTITY_URL` (default `http://localhost:5223`) and `BUSINESSRULES_URL` (default `http://localhost:5230`) from the environment or `.env`. Start the AppHost with `ESHOP_USE_HTTP_ENDPOINTS=1` for these journeys so Identity is served at `http://localhost:5223`, BusinessRules.API trusts that issuer, and the https redirect is avoided (see `.github/workflows/playwright.yml` and `src/eShop.AppHost/AppHost.cs`).

### Optional: AI Chatbot with Microsoft Foundry

This option provisions a Microsoft Foundry resource during local development, so first authenticate to Azure and configure the subscription and location:

```powershell
az login
aspire secret set "Azure:SubscriptionId" "<subscription-id>"
aspire secret set "Azure:Location" "eastus"
```

Then enable Foundry and start eShop:

```powershell
$env:UseFoundry = "true"
aspire run
```

Aspire provisions the `gpt-4.1-mini` and `text-embedding-3-small` deployments and injects their connection information into the consuming projects. The Foundry hosting integration currently uses a preview package. See [local Azure provisioning](https://aspire.dev/integrations/cloud/azure/local-provisioning/) and the [Microsoft Foundry hosting integration](https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-host/) for details.

### Deploy to Azure Container Apps

The AppHost is already configured with an Azure Container Apps environment, so the Aspire CLI can deploy directly from the application model. See the [Aspire Azure Container Apps deployment guide](https://aspire.dev/deployment/azure/container-apps/) for details.

> [!WARNING]
> This sample deploys PostgreSQL, Redis, and RabbitMQ as containers in Azure Container Apps. This configuration is intended for evaluation and demonstrations, not production data.

Prerequisites:

- The prerequisites listed above, including a running container runtime.
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), an active Azure subscription, and permission to create resources.

Sign in, optionally preview the deployment pipeline, and deploy:

```console
az login
aspire deploy --list-steps
aspire deploy
```

For local interactive use, `aspire deploy` prompts for missing Azure settings. For non-interactive use, provide them explicitly:

```powershell
$env:Azure__SubscriptionId = "<subscription-id>"
$env:Azure__Location = "eastus"
$env:Azure__ResourceGroup = "rg-eshop-demo"
aspire deploy --non-interactive
```

Use [`aspire publish`](https://aspire.dev/reference/cli/commands/aspire-publish/) when you need deployment artifacts for inspection or another deployment tool. Running it first is not required: `aspire deploy` invokes the deployment pipeline and its dependencies directly rather than consuming an earlier publish output.

When you no longer need the deployment, run [`aspire destroy`](https://aspire.dev/reference/cli/commands/aspire-destroy/). This deletes the entire configured resource group, including resources that Aspire did not create, so review the target carefully before confirming.

## Contributing

For more information on contributing to this repo, read [the contribution documentation](./CONTRIBUTING.md) and [the Code of Conduct](CODE-OF-CONDUCT.md).

### Sample data

The sample catalog data is defined in [catalog.json](https://github.com/dotnet/eShop/blob/main/src/Catalog.API/Setup/catalog.json). Those product names, descriptions, and brand names are fictional and were generated using [GPT-35-Turbo](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/chatgpt), and the corresponding [product images](https://github.com/dotnet/eShop/tree/main/src/Catalog.API/Pics) were generated using [DALL·E 3](https://openai.com/dall-e-3).

## eShop on Azure

For a version of this app configured for deployment on Azure, please view [the eShop on Azure](https://github.com/Azure-Samples/eShopOnAzure) repo.
