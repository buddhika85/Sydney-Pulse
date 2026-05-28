# Cost model

Estimated monthly Azure cost for Sydney Pulse, with both the portfolio-scale
configuration (current) and the production-scale alternative (future, if
the project ever became a real service).

## Current configuration (portfolio scale)

Realistic estimate for a 24/7 running demo with handful of concurrent users.

| Component | SKU / tier | Estimated AUD/month |
|---|---|---|
| Azure Functions | Consumption | $2–4 |
| Event Grid (custom topic) | Pay-per-event | $0.20 |
| Service Bus | Existing Standard namespace (reused) | $0 marginal |
| Cosmos DB | Serverless | $3–8 |
| Data Lake Storage Gen2 | Standard hot, ~3 GB | $0.50 |
| SignalR Service | Free SKU | $0 |
| Static Web Apps | Free SKU | $0 |
| Application Insights | 5% sampling, 1 GB daily cap | $0–2 |
| Azure Maps | Gen2 S0 free | $0 |
| Key Vault | Standard | $0.05 |
| Storage account (Functions metadata) | Standard LRS | $0.10 |
| **Estimated total** | | **$6–15** |

Budget alert: $20/month at 80% and 100%.

## Production configuration (scaling scenario)

What we'd switch to if the project handled actual traffic (10k+ concurrent
users, sub-second SLA needs, regulatory data retention).

| Component | SKU / tier | Estimated AUD/month |
|---|---|---|
| Azure Functions | Elastic Premium EP1 | $230 |
| Event Grid | Pay-per-event (10× volume) | $5 |
| Service Bus | Standard (existing) or Premium MU 1 | $0 marginal or $980 |
| Cosmos DB | Provisioned 1000 RU/s autoscale | $90 |
| Data Lake Storage Gen2 | Standard hot, ~100 GB | $5 |
| SignalR Service | Standard 1 unit | $48 |
| Static Web Apps | Standard | $14 |
| Application Insights | No sampling, ~50 GB/month | $115 |
| Azure Maps | Gen2 S1 | $35 |
| Key Vault | Standard | $1 |
| Storage account | Standard ZRS | $5 |
| Front Door (custom domain + WAF) | Standard | $35 |
| **Estimated total** | | **~$583–$1,563** |

The Service Bus Premium row is the biggest spread — Premium starts at
~$980/month for a single messaging unit and is only needed if message
throughput or strict latency demands it.

## Tier choice rationale

Each cost decision is its own ADR. The summary:

- **Functions Consumption** — cold starts are tolerable at 30s poll cadence
  and reads are not user-facing. Premium adds $225/month for VNet
  integration we don't need.
- **Service Bus existing Standard** — pre-existing in the subscription, so
  marginal cost is zero. We get topic-based subscription filters and
  sessions (though we don't use sessions, per ADR-0010).
- **Cosmos Serverless** — bursty, low-volume workload. Pay-per-RU billing
  means idle hours cost $0. Provisioned 400 RU/s would cost $24/month
  even when no traffic flows.
- **SignalR Free** — 20 concurrent connections is enough for portfolio
  demos. Standard is $48/month per unit and we don't need it.
- **Application Insights with sampling** — 5 GB/month is free. Without
  sampling, verbose Function logs would exceed 50 GB within a week and
  cost ~$115/month.

## Scaling thresholds

When to upgrade each component:

| Component | Upgrade trigger | Target tier |
|---|---|---|
| Functions | Cold start > 1s impacting users, or VNet integration needed | Premium EP1 |
| Cosmos Serverless | Sustained > 500 RU/s sustained, or > $50/month | Provisioned autoscale 1000–10000 RU/s |
| SignalR Free | > 15 concurrent connections regularly | Standard 1 unit |
| App Insights sampling | Need every event for compliance | Disable sampling, raise daily cap |
| Static Web Apps Free | Need custom domain auth or larger app | Standard |
| Service Bus | > 10 alerts/sec sustained | Premium MU 1 |

## Monitoring cost

- Azure portal budget alert at $20/month sends email at 80% and 100%
- Cost Management report exported weekly to OneDrive for trend tracking
- Per-resource group cost breakdown reviewed at end of every sprint

## Assumptions

- ~30 unique demo visitors per week
- ~5 concurrent SignalR connections at peak
- Polling all 5 modes every 30s = ~14,400 TfNSW requests/day
- Each poll yields ~5,000 vehicle entities across modes
- Vehicle updates published to Event Grid: ~14M/month
- Cosmos upserts: ~14M/month, ~5 RU each = 70M RU/month
- Application Insights ingestion at 5% sampling: ~3 GB/month
- No Synapse, no Front Door, no API Management — these would push us
  toward the production scenario

## Cost-saving levers if needed

If steady-state costs drift above the $20/month budget alert:

1. Increase polling interval from 30s to 60s — halves event volume.
2. Increase App Insights sampling from 5% to 2% — halves ingestion.
3. Stop polling buses overnight (between 1 am and 4 am) — minimal
   service runs anyway.
4. Reduce Cosmos document TTL from 5 min to 2 min — less storage cost.
5. Tear down dev environment between active development sessions.

## Currency note

All figures in AUD as of pricing in 2026-Q2. Azure prices change; check
the Azure pricing calculator before making upgrade decisions:
https://azure.microsoft.com/en-au/pricing/calculator/
