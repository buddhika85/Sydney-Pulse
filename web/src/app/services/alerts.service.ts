// AlertsService - reads the GET /api/alerts HTTP endpoint.
// Envelope is flattened to `Alert[]` per SP1-09 design decision A;
// the response envelope carries no fields beyond `alerts`.
// Backend contract: docs/api.md "GET /api/alerts" section.

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { Alert, AlertsResponse } from '../models';

@Injectable({ providedIn: 'root' })
export class AlertsService {
  // inject() over ctor injection per SP1-09 design decision C.
  private readonly http = inject(HttpClient);

  /**
   * Fetch currently active alerts. Returns unwrapped array (envelope
   * dropped at the service boundary).
   */
  getAlerts(): Observable<Alert[]> {
    return this.http
      .get<AlertsResponse>(`${environment.apiBaseUrl}/alerts`)
      .pipe(map((envelope) => envelope.alerts));
  }
}
