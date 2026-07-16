// environment.prod.ts - production overrides, swapped in at build time
// via fileReplacements in angular.json. Same shape as environment.ts;
// drift between the two surfaces as a build error at the import site.

export const environment = {
  production: true,

  // Production build target for Sprint 1 (single-environment portfolio scope).
  // apiBaseUrl deliberately points at sydney-pulse-func-dev — no prod
  // Function App is provisioned yet (Sprint 2 will introduce the real prod
  // resource group + slot swap). Update this URL when prod SWA + prod
  // Function App go live.
  apiBaseUrl: 'https://sydney-pulse-func-dev.azurewebsites.net/api',

  features: {
    pulseMarkers: true,
  },

  evidence: {
    releaseTag: 'v0.1.0',
    releaseDate: '2026-07-14',
    liveUrl: 'https://proud-grass-020b12300.7.azurestaticapps.net/',

    // Loom video ID for the evidence page walkthrough. Empty string
    // renders the "coming soon" placeholder card; setting a real ID
    // (e.g. 'abc123def') swaps in the embedded iframe on next deploy.
    loomVideoId: '7726a3e69ec84db68a86a0290c46bf62',
    repoUrl: 'https://github.com/buddhika85/Sydney-Pulse',
  },

  // debugging
  debugging: {
    enableSignalRRealtime: true,
    enableFreshnessTimer: true,
  },
};
