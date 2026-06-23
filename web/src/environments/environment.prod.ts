// environment.prod.ts - production overrides, swapped in at build time
// via fileReplacements in angular.json. Same shape as environment.ts;
// drift between the two surfaces as a build error at the import site.

export const environment = {
  production: true,
  apiBaseUrl: 'https://sydney-pulse-func-prod.azurewebsites.net/api',
};
