// RoutesService - reads the GET /api/routes HTTP endpoint.
// Envelope is flattened to `TransportRoute[]` per SP1-09 design decision A.
// Backend caches the GTFS static feed in-memory for 1h (ADR-0009), so this
// call is cheap; no client-side cache layer needed.
// Backend contract: docs/api.md "GET /api/routes" section.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { TransportRoute, TransportRoutesResponse } from '../models';

@Injectable({ providedIn: 'root' })
export class RoutesService {
  // inject() over ctor injection per SP1-09 design decision C.
  private readonly http = inject(HttpClient);

  /**
   * Fetch all static route metadata. Returns unwrapped array (envelope
   * dropped at the service boundary).
   */
  getRoutes(): Observable<TransportRoute[]> {
    return this.http
      .get<TransportRoutesResponse>(`${environment.apiBaseUrl}/routes`)
      .pipe(map((envelope) => envelope.routes));
  }
}
