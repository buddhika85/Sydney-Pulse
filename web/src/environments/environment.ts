// environment.ts - default config used for `ng serve` and dev builds.
// Production build swaps this file with environment.prod.ts via the
// fileReplacements entry in angular.json (production configuration).
//
// Per SP1-16 handoff: ng serve on localhost:4200 talks to the deployed
// dev Function App. CORS for http://localhost:4200 added in SP1-09 step 8.

export const environment = {
  production: false,
  // No trailing slash - service callers concatenate `/vehicles`, `/alerts`, etc.
  apiBaseUrl: 'https://sydney-pulse-func-dev.azurewebsites.net/api',
};
