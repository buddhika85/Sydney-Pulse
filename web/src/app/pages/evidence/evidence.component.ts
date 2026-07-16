// evidence.component.ts - portfolio evidence page at "/evidence".
// Curated proof-walkthrough of Sydney Pulse Sprint 1 shipped state:
// Loom demo (env-flagged) + 8 captioned screenshots covering IaC,
// CI/CD, backend tests, Cosmos partitioning, App Insights end-to-end,
// live dashboard, and Managed Identity / RBAC.
//
// Interview-demo optimised: one page, one URL, one nav click. Avoids
// the Azure Portal live-navigation risk during screen-shares.
//
// Standalone + OnPush per web/CLAUDE.md. RouterLink for internal SPA
// nav on the CTA footer, plain <a> for external repo/GitHub links.
// No service injection, no state - pure static content bound to a
// readonly section array.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';

import { environment } from '../../../environments/environment';

// Shape of one image inside a section. Section 7 has two (7a full +
// 7b filtered); every other section has one. Kept optional-friendly
// so the template can just iterate.
interface EvidenceImage {
  src: string;
  alt: string;
  // Optional sub-label (used only for 7a / 7b naming inside section 7).
  label?: string;
}

// One evidence "shot" — claim (headline), image(s), why-it-matters
// (interpretive layer). Number is a string so 7 can hold "7" while
// nested labels handle a/b.
interface EvidenceSection {
  number: string;
  title: string;
  claim: string;
  images: EvidenceImage[];
  whyItMatters: string;
}

@Component({
  selector: 'sp-evidence',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './evidence.component.html',
  styleUrl: './evidence.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EvidenceComponent {
  // Angular treats iframe[src] as a security-critical binding and
  // silently strips plain string URLs (blank iframe + "sanitizing
  // unsafe URL" warning in DevTools). We explicitly trust the Loom
  // embed URL because it comes from our own environment config, not
  // user input — safe target for bypassSecurityTrustResourceUrl.
  private readonly sanitizer = inject(DomSanitizer);

  // External links — read from environment.ts
  // (do not change between dev/prod deploys).
  readonly repoUrl = environment.evidence.repoUrl;
  readonly liveUrl = environment.evidence.liveUrl;
  readonly releaseTag = environment.evidence.releaseTag;
  readonly releaseDate = environment.evidence.releaseDate;

  // Loom video config — env-flagged. Empty ID renders the "coming soon"
  // placeholder card; a real ID renders the sanitized iframe URL.
  readonly loomVideoId = environment.evidence.loomVideoId;
  readonly hasLoomVideo = !!environment.evidence.loomVideoId;
  readonly loomEmbedUrl: SafeResourceUrl | null = environment.evidence
    .loomVideoId
    ? this.sanitizer.bypassSecurityTrustResourceUrl(
        `https://www.loom.com/embed/${environment.evidence.loomVideoId}`,
      )
    : null;

  // Nav-back CTA target — internal SPA route.
  readonly liveDashboardRoute = '/live';

  // Eight evidence sections. Content adapted from docs/evidence.md but
  // shorter + punchier for on-screen scanning. Order matches the
  // Recommended capture order in the docs (Batch A → B → C).
  readonly sections: EvidenceSection[] = [
    {
      number: '1',
      title: 'Everything provisioned via Bicep',
      claim:
        'Zero click-ops. 11 top-level resources declared in one Bicep entrypoint.',
      images: [
        {
          src: '/evidence/evidence-01-resource-group.png',
          alt: 'Azure Portal resource group view showing 11 Sydney Pulse resources: Application Insights, Cosmos DB, Event Grid, Function App, App Service Plan, Key Vault, Log Analytics, SignalR, Static Web App, Data Lake storage, Functions storage — all in Australia East except SWA (East Asia).',
        },
      ],
      whyItMatters:
        'Reproducible in ~2 minutes to a fresh resource group. Sub-resources (Cosmos containers, Event Grid subscriptions, Key Vault secrets) sit below these and are also Bicep-managed.',
    },
    {
      number: '2',
      title: 'CI/CD pipeline — 4 jobs, ~5 min end-to-end',
      claim: 'Parallel deploy of backend + frontend after a single infra step.',
      images: [
        {
          src: '/evidence/evidence-02-deploy-dev-run.png',
          alt: 'GitHub Actions deploy-dev workflow run showing lint-test → deploy-infra → publish-app + publish-web in parallel, all green, total duration 5 min 8 sec.',
        },
      ],
      whyItMatters:
        'OIDC federated identity — zero static secrets stored in GitHub. Node 24 runtimes, zero deprecation warnings. Publish-app (Functions) and publish-web (SWA) fan out from deploy-infra to shave wall time.',
    },
    {
      number: '3',
      title: 'Branch + PR discipline',
      claim: 'Every sprint item gets a feature branch, PR, and squash-merge.',
      images: [
        {
          src: '/evidence/evidence-03-pr-13-merged.png',
          alt: 'Merged PR #13 "feat(sp1-13): live URL pipeline + demo polish for v0.1.0" showing summary, test plan, deferrals, and 8 branch commits collapsed to one via squash-merge.',
        },
      ],
      whyItMatters:
        'Clean linear main history. PR body carries summary + test plan + explicit deferrals so reviewers (and future me) understand what was intentional. This PR bundled 8 branch commits into one release-shaped commit.',
    },
    {
      number: '4',
      title: 'Backend tests green',
      claim:
        '64 xUnit tests passing across TfNSW client, State Writer, Alerter, Archiver, HTTP API, Cosmos, Event Grid schemas.',
      images: [
        {
          src: '/evidence/evidence-04-dotnet-test.png',
          alt: 'Terminal output of `dotnet test` showing Passed: 64, Failed: 0, Skipped: 0, Total: 64, Duration: 496 ms.',
        },
      ],
      whyItMatters:
        'TDD workflow: failing test → implementation → paired review. Frontend unit tests deferred to SP-21 by explicit sprint scoping — target roles favour .NET-senior with Angular secondary.',
    },
    {
      number: '5',
      title: 'Cosmos partitioning strategy',
      claim:
        'Live vehicles partitioned by `routeShortName` (T1, T2, T4, T8...) matching how the UI groups them.',
      images: [
        {
          src: '/evidence/evidence-05-cosmos-data-explorer.png',
          alt: 'Azure Cosmos DB Data Explorer showing the vehicles container with dozens of real Sydney Trains vehicle documents, each with a routeShortName partition key value and full vehicle JSON body.',
        },
      ],
      whyItMatters:
        'Single-partition reads for route-scoped queries. Sub-100ms Cosmos read latency measured in Application Insights (~74 ms avg). Partition key choice locked in ADR-0002 + ADR-0011.',
    },
    {
      number: '6',
      title: 'End-to-end observability',
      claim: '918 Function App requests over 24h with 99.4% success rate.',
      images: [
        {
          src: '/evidence/evidence-06-appinsights-e2e.png',
          alt: 'Azure Application Insights Application Map showing sydney-pulse-func-dev at the centre with dependency edges to TfNSW API (172ms avg), Cosmos DB (74ms avg), SignalR Service (148ms avg), and IMDS token endpoint (169.254.169.254, 33ms, 100% success).',
        },
      ],
      whyItMatters:
        'Every hop is traced. The IMDS node (169.254.169.254) is where the Function App exchanges its Managed Identity for tokens — proof that MI is exercised at runtime, not just configured on paper.',
    },
    {
      number: '7',
      title: 'Live dashboard — real data, live SignalR',
      claim:
        'Two views — full network breadth and single-route filter — of the deployed dashboard.',
      images: [
        {
          src: '/evidence/evidence-07a-live-dashboard-full.png',
          alt: 'Sydney Pulse live dashboard showing the full Sydney Trains network with 34+ vehicles coloured by route (T1-T9 plus BMT, CCN, NRC, SCO, SHL). Alerts panel on the right shows 75 active alerts. Freshness pill "Live" (green) top-right.',
          label: '7a — Full network view',
        },
        {
          src: '/evidence/evidence-07b-live-dashboard-filtered.png',
          alt: 'Same dashboard filtered to one route — filter chip active — proving the interactive filter surface is wired without breaking the SignalR stream.',
          label: '7b — Filtered view + interaction proof',
        },
      ],
      whyItMatters:
        'CircleMarkers colour-coded by route, pulse animation on each SignalR update (radius 100→130→100% over 450ms). Filter chips built on Angular Signals + RxJS — interaction state changes without disrupting the live stream.',
    },
    {
      number: '8',
      title: 'Managed Identity + RBAC — zero static credentials',
      claim:
        'Function App MI holds `Key Vault Secrets User`, scoped to This resource (least-privilege).',
      images: [
        {
          src: '/evidence/evidence-08-keyvault-rbac.png',
          alt: 'Azure Portal IAM view for the Key Vault showing 5 role assignments: dev account as Owner + Key Vault Secrets Officer, Function App system-assigned Managed Identity as Key Vault Secrets User (scoped to This resource), and GitHub Actions service principal as Contributor + User Access Administrator (OIDC federated identity for CI/CD).',
        },
      ],
      whyItMatters:
        'Runtime credentials: Managed Identity (no connection strings). CI/CD credentials: OIDC federated identity (no stored client secret). Management credentials: dev account has separate roles from the runtime MI. Three identities, three purposes, zero shared secrets.',
    },
  ];
}
