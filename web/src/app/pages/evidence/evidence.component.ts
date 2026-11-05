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
      title: 'Cosmos partitioning strategy — vehicles container',
      claim:
        'Live vehicles partitioned by `routeShortName` (T1, T2, T5, T8, BMT, SCO...) matching how the UI groups them.',
      images: [
        {
          src: '/evidence/evidence-05-cosmos-data-explorer.png',
          alt: 'Azure Cosmos DB Data Explorer showing the vehicles container with real Sydney Trains vehicle documents, each with a routeShortName partition key (T1, T2, T5, T8, BMT, SCO visible) and a full vehicle JSON body open in the right pane.',
        },
      ],
      whyItMatters:
        'Single-partition reads for route-scoped queries. Cosmos read latency measured at 70 ms avg across 64 calls with 100% success (Application Map, 45-min AM-peak sample). Partition key choice locked in ADR-0002 + ADR-0011.',
    },
    {
      number: '5b',
      title: 'Cosmos partitioning — alerts container (bonus)',
      claim:
        'Alerts persisted in the same `sydneyPulse` database, keyed by the route each alert affects.',
      images: [
        {
          src: '/evidence/evidence-05b-cosmos-alerts.png',
          alt: 'Azure Cosmos DB Data Explorer alerts container showing service-alert documents with routeShortName partition keys (T4, T5, T8, BNK_1a, CCN, SCO visible) and a full alert JSON open — severity `modifiedService`, headerText, description, active-period timestamps.',
        },
      ],
      whyItMatters:
        'Alerts partition mixes user-facing route short names (T4, T5, T8) with internal GTFS route IDs (BNK_1a, CCN, SCO) — a subtle authenticity signal that the data is unedited GTFS-Realtime, not synthesised fixtures. Alert schema drives the alerts panel on the live dashboard.',
    },
    {
      number: '6',
      title: 'End-to-end observability — Application Map (45-min AM peak)',
      claim:
        '24K Function App requests across 4 instances, with dependency-level latencies and success rates visible per hop.',
      images: [
        {
          src: '/evidence/evidence-06-appinsights-e2e.png',
          alt: 'Azure Application Insights Application Map (45-min AM-peak sample). Function App sydney-pulse-func-dev at centre: 4 instances, 10% overall failure, 134.9 ms avg, 24K total requests. Dependency edges: TfNSW api.transport.nsw.gov.au 82.9 ms avg / 157 calls (green); SignalR sydney-pulse-signalr-dev 67.2 ms avg / 11K calls / 22.9% failure (red, HTTP 429); Cosmos sydney-pulse-cosmos-dev 70.3 ms avg / 64 calls (green); IMDS 169.254.169.254 24.1 ms avg / 156 calls / 100% success (green).',
        },
      ],
      whyItMatters:
        'Every hop is traced. The 10% Function App failure is dominated by the SignalR 429s cascading through the StateWriter — see #6d for the drill-in. IMDS at 24 ms averaged across 156 successful token exchanges proves Managed Identity is exercised at runtime, not just configured on paper (a secondary 4-call cold-start probe path shows the small red edge).',
    },
    {
      number: '6b',
      title: 'TfNSW dependency drill-in — three named endpoints',
      claim:
        'The TfNSW client makes three distinct GTFS calls, all visible and named in App Insights.',
      images: [
        {
          src: '/evidence/evidence-06b-appinsights-tfnsw-drill.png',
          alt: 'Application Map with api.transport.nsw.gov.au selected. Function App centre: 4 instances, 11% failure, 143 ms avg, 20K calls. TfNSW edge: 83.4 ms / 110 calls, green (no error markers). Right panel lists the three slowest calls: GET /v1/gtfs/schedule/sydneytrains (89.3 ms), GET /v2/gtfs/vehiclepos/sydneytrains (86.9 ms), GET /v2/gtfs/alerts/sydneytrains (79.8 ms).',
        },
      ],
      whyItMatters:
        'The three endpoints map exactly to TfNswFeedClient methods: FetchAndParseStaticRoutesAsync (`/v1/gtfs/schedule`, hourly per ADR-0009), GetVehiclePositionsAsync (`/v2/gtfs/vehiclepos`, 30-second cron), GetServiceAlertsAsync (`/v2/gtfs/alerts`, 30-second cron). No 4xx observed in the sample — Polly retry on 429/503 sits ready if TfNSW ever throttles.',
    },
    {
      number: '6c',
      title: 'Cosmos dependency drill-in — 0% failure across both endpoints',
      claim:
        'Both Cosmos endpoints (regional + account-scoped) show 0% failure across the sample.',
      images: [
        {
          src: '/evidence/evidence-06c-appinsights-cosmos-drill.png',
          alt: 'Application Map with sydney-pulse-cosmos-dev selected. Function App centre: 21K calls, 11% failure. Cosmos edge: 73.3 ms / 60 calls, green. Right panel Group Members: sydney-pulse-cosmos-dev-australiaeast (32 calls, 0% failures), sydney-pulse-cosmos-dev (28 calls, 0% failures). Slowest calls: Get collection (135.7 ms), GET (73.3 ms), GET /dbs/*/colls/*/pkranges (4.4 ms).',
        },
      ],
      whyItMatters:
        'Cosmos Serverless in Australia East with no failed reads in the observed sample. The two group-member endpoints are the regional endpoint plus the account-scoped SDK routing; `pkranges` lookups at 4.4 ms show the SDK caching partition-key ranges correctly — no per-query partition discovery.',
    },
    {
      number: '6d',
      title: 'SignalR Free SKU — 429 throttling caught in production ★',
      claim:
        'HTTP 429 responses from SignalR Free SKU visible at scale — 2 200 throttled calls out of 9 700 total (22.5%).',
      images: [
        {
          src: '/evidence/evidence-06d-appinsights-signalr-drill.png',
          alt: 'Application Map with sydney-pulse-signalr-dev.service.signalr.net selected. SignalR edge red: 71.8 ms / 22.5% failure / 9.7K calls. Right panel top failing status code: 429 with 2.2K count. Slowest calls: POST /api/hubs/vehicles/:send (17.5 ms), POST /api/hubs/alerts/:send (11.9 ms). Failed-dependencies panel: 100% of failures share resultCode 429; ~29% correlate with operation StateWriter.',
        },
      ],
      whyItMatters:
        'ADR-0008 chose SignalR Free SKU (20K messages/day cap) as a portfolio-scale trade-off; this shot is empirical proof the trade-off is real — at 216 vehicles broadcasting every 30 s during AM peak, we burn through the daily quota in ~1 h. Production upgrade to Standard SKU is a Bicep-parameter flip; the 429s cascade into the 10-11% Function App overall failure via StateWriter. Cost/capacity story you can tell precisely because you actually stressed the system.',
    },
    {
      number: '7',
      title: 'Live dashboard — real data, live SignalR',
      claim:
        'Two views — full network breadth and single-route filter — of the deployed dashboard.',
      images: [
        {
          src: '/evidence/evidence-07a-live-dashboard-full.png',
          alt: 'Sydney Pulse live dashboard during AM peak — 216 vehicles across the Sydney Trains network coloured by route (T1-T9 plus BMT, CCN, NRC, SCO, SHL). Alerts panel on the right shows 41 active alerts. Freshness pill "Live" (green) top-right. URL bar confirms deployed Static Web App.',
          label: '7a — Full network view (216 vehicles / 41 alerts)',
        },
        {
          src: '/evidence/evidence-07b-live-dashboard-filtered.png',
          alt: 'Same dashboard with T1 chip active — 44 of 221 vehicles remain (T1 orange only), alerts panel narrowed to 8 of 41 (T1-relevant). Freshness pill still green — filter didn\'t break the SignalR stream.',
          label: '7b — Filtered view (44/221 T1 vehicles · 8/41 alerts · stream intact)',
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
