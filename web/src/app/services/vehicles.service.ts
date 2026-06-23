// VehiclesService - reads the GET /api/vehicles HTTP endpoint.
// Returns the full envelope (VehiclesResponse) so callers can read
// `feedTimestamp` for stale-data UI per SP1-09 design decision A.
// Backend contract: docs/api.md "GET /api/vehicles" section.

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { VehiclesResponse } from '../models';

// Filter shape mirrors the two optional query params on /api/vehicles.
export interface VehiclesFilter {
  mode?: string;
  routeShortName?: string;
}

@Injectable({ providedIn: 'root' })
export class VehiclesService {
  // inject() over ctor injection per SP1-09 design decision C.
  private readonly http = inject(HttpClient);

  /**
   * Fetch current vehicle positions, optionally filtered by mode or route.
   * Returns the full envelope so callers can read `feedTimestamp` for
   * stale-data UI.
   */
  getVehicles(filter?: VehiclesFilter): Observable<VehiclesResponse> {
    const params = this.buildQueryString(filter);

    return this.http.get<VehiclesResponse>(
      `${environment.apiBaseUrl}/vehicles`,
      { params },
    );
  }

  // A helper to build query string values
  private buildQueryString(filter?: VehiclesFilter): HttpParams {
    let params = new HttpParams();

    if (filter?.mode) params = params.set('mode', filter.mode);
    if (filter?.routeShortName)
      params = params.set('routeShortName', filter.routeShortName);

    return params;
  }
}
